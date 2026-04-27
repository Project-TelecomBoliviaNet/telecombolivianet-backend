using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.DTOs.Invoices;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Invoices;

/// <summary>
/// Servicio central de facturación — estándar ISP comercial.
///
/// REGLAS:
/// 1. Al registrar un cliente → GenerateBackfillInvoicesAsync crea todas las
///    facturas desde la fecha de instalación hasta el mes actual.
/// 2. El primer mes es PROPORCIONAL (días restantes del mes / días del mes).
/// 3. El job mensual solo genera para clientes instalados antes del fin del mes objetivo.
/// 4. Nunca se generan facturas para meses anteriores a la instalación del cliente.
/// </summary>
public class BillingService
{
    private readonly IGenericRepository<Client>  _clientRepo;
    private readonly IGenericRepository<Invoice> _invoiceRepo;
    private readonly AuditService                _audit;
    private readonly ILogger<BillingService>     _logger;
    private readonly IInvoiceNumberService       _invNumSvc;  // US-FAC-CORRELATIVO

    public BillingService(
        IGenericRepository<Client>  clientRepo,
        IGenericRepository<Invoice> invoiceRepo,
        AuditService                audit,
        ILogger<BillingService>     logger,
        IInvoiceNumberService       invNumSvc)
    {
        _clientRepo  = clientRepo;
        _invoiceRepo = invoiceRepo;
        _audit       = audit;
        _logger      = logger;
        _invNumSvc   = invNumSvc;
    }

    // ── Generación retroactiva al registrar un cliente ─────────────────────

    /// <summary>
    /// Genera todas las facturas mensuales faltantes desde la fecha de
    /// instalación hasta el mes actual (inclusive). Primer mes proporcional.
    ///
    /// Usa AddRangeAsync + un único SaveChangesAsync para garantizar atomicidad
    /// y evitar N round-trips a la BD (un commit por cada factura anterior).
    /// </summary>
    public async Task GenerateBackfillInvoicesAsync(Client client, decimal monthlyPrice)
    {
        var installDate = client.InstallationDate;
        var today       = DateTime.UtcNow;

        var cursor   = new DateTime(installDate.Year, installDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endMonth = new DateTime(today.Year,       today.Month,       1, 0, 0, 0, DateTimeKind.Utc);

        // Obtener meses que ya tienen factura para este cliente (una sola query)
        var existingMonths = await _invoiceRepo.GetAll()
            .Where(i => i.ClientId == client.Id && i.Type == InvoiceType.Mensualidad)
            .Select(i => new { i.Year, i.Month })
            .ToListAsync();

        var existingSet = existingMonths
            .Select(x => (x.Year, x.Month))
            .ToHashSet();

        var invoicesToCreate = new List<Invoice>();

        while (cursor <= endMonth)
        {
            if (!existingSet.Contains((cursor.Year, cursor.Month)))
            {
                var (amount, notes) = CalculateMonthlyAmount(monthlyPrice, installDate, cursor.Year, cursor.Month);

                var dueDate = new DateTime(cursor.Year, cursor.Month, 5, 0, 0, 0, DateTimeKind.Utc);
                var status  = dueDate < DateTime.UtcNow
                    ? InvoiceStatus.Vencida
                    : InvoiceStatus.Pendiente;

                var invNumber = await _invNumSvc.NextInvoiceNumberAsync(); // US-FAC-CORRELATIVO
                invoicesToCreate.Add(new Invoice
                {
                    ClientId      = client.Id,
                    Type          = InvoiceType.Mensualidad,
                    Status        = status,
                    Year          = cursor.Year,
                    Month         = cursor.Month,
                    Amount        = amount,
                    IssuedAt      = DateTime.UtcNow,
                    DueDate       = dueDate,
                    Notes         = notes,
                    InvoiceNumber = invNumber,                 // US-FAC-CORRELATIVO
                });
            }

            cursor = cursor.AddMonths(1);
        }

        if (invoicesToCreate.Count == 0)
        {
            _logger.LogInformation("Sin facturas retroactivas nuevas para {TbnCode}", client.TbnCode);
            return;
        }

        // BUG FIX: solo AddRangeAsync sin SaveChangesAsync propio.
        // El llamador (ClientService.RegisterAsync, PlanChangeService) ya envuelve
        // toda la operación en IUnitOfWork y llama SaveChangesAsync/CommitAsync al final.
        // Llamar SaveChangesAsync aquí genera commits parciales rompiendo la atomicidad.
        await _invoiceRepo.AddRangeAsync(invoicesToCreate);

        _logger.LogInformation(
            "Facturas retroactivas: {TbnCode} — {Count} facturas creadas (de {StartMonth}/{StartYear} a {EndMonth}/{EndYear})",
            client.TbnCode, invoicesToCreate.Count,
            installDate.Month, installDate.Year,
            today.Month, today.Year);
    }

    // ── US-21 / US-26 · Job mensual ────────────────────────────────────────

    /// <summary>
    /// Genera facturas del mes indicado SOLO para clientes instalados antes
    /// del fin de ese mes. Primer mes proporcional por cliente si aplica.
    ///
    /// Usa AddRangeAsync + un único SaveChangesAsync para todos los clientes
    /// del mes, reduciendo de N commits individuales a 1 round-trip a la BD.
    /// </summary>
    public async Task<BillingJobResultDto> GenerateMonthlyInvoicesAsync(
        int year, int month, Guid? actorId = null, string actorName = "Sistema")
    {
        _logger.LogInformation("Facturación {Month}/{Year} iniciada", month, year);

        // Fin del mes objetivo (clientes instalados antes de esta fecha aplican)
        var monthEnd = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        var dueDate  = new DateTime(year, month, 5, 0, 0, 0, DateTimeKind.Utc);
        var period   = $"{MonthName(month)} {year}";

        var clients = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Where(c =>
                (c.Status == ClientStatus.Activo || c.Status == ClientStatus.Suspendido) &&
                c.InstallationDate < monthEnd)
            .ToListAsync();

        var existingClientIds = (await _invoiceRepo.GetAll()
            .Where(i => i.Year == year && i.Month == month && i.Type == InvoiceType.Mensualidad)
            .Select(i => i.ClientId)
            .ToListAsync()).ToHashSet();

        int generated = 0, skipped = 0, excludedBaja = 0, errors = 0;
        var invoicesToCreate = new List<Invoice>(clients.Count);

        foreach (var client in clients)
        {
            if (existingClientIds.Contains(client.Id)) { skipped++; continue; }

            if (client.Plan is null)
            {
                _logger.LogWarning("Cliente {TbnCode} sin plan, omitido", client.TbnCode);
                errors++;
                continue;
            }

            try
            {
                var (amount, notes) = CalculateMonthlyAmount(
                    client.Plan.MonthlyPrice, client.InstallationDate, year, month);

                // US-FAC-CORRELATIVO: número correlativo
                var invNum = await _invNumSvc.NextInvoiceNumberAsync();

                // US-FAC-CREDITO: aplicar crédito a favor del cliente
                var creditoAplicado = 0m;
                var amountFinal     = amount;
                if (client.CreditBalance > 0)
                {
                    creditoAplicado      = Math.Min(client.CreditBalance, amount);
                    amountFinal          = amount - creditoAplicado;
                    client.CreditBalance -= creditoAplicado;
                    await _clientRepo.UpdateAsync(client);
                    _logger.LogInformation(
                        "Crédito aplicado a {TbnCode}: Bs.{Credito} → saldo factura Bs.{Monto}",
                        client.TbnCode, creditoAplicado, amountFinal);
                }

                // US-FAC-ESTADOS: emitida al crearse
                var statusInicial = amountFinal <= 0
                    ? InvoiceStatus.Pagada          // crédito cubrió todo
                    : InvoiceStatus.Emitida;        // US-FAC-ESTADOS

                invoicesToCreate.Add(new Invoice
                {
                    ClientId       = client.Id,
                    Type           = InvoiceType.Mensualidad,
                    Status         = statusInicial,
                    Year           = year,
                    Month          = month,
                    Amount         = amount,
                    AmountPaid     = creditoAplicado,
                    CreditApplied  = creditoAplicado,
                    IssuedAt       = DateTime.UtcNow,
                    DueDate        = dueDate,
                    Notes          = notes,
                    InvoiceNumber  = invNum,
                });

                generated++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preparando factura para {TbnCode}", client.TbnCode);
                errors++;
            }
        }

        // Un único commit para todas las facturas del mes — transacción atómica.
        if (invoicesToCreate.Count > 0)
        {
            await _invoiceRepo.AddRangeAsync(invoicesToCreate);
            await _invoiceRepo.SaveChangesAsync();
        }

        excludedBaja = await _clientRepo.GetAll()
            .CountAsync(c => c.Status == ClientStatus.DadoDeBaja);

        var result = new BillingJobResultDto(generated, skipped, excludedBaja, errors, period);

        await _audit.LogAsync(
            "Facturación", "BILLING_JOB_EXECUTED",
            $"Facturación {period}: {generated} generadas, {skipped} omitidas, {errors} errores",
            userId: actorId, userName: actorName,
            newData: System.Text.Json.JsonSerializer.Serialize(result));

        return result;
    }

    // ── US-22 · Marcar facturas vencidas ───────────────────────────────────

    // BUG E FIX: N commits individuales reemplazados por UpdateRangeAsync (1 sola transacción).
    // Con 500 facturas vencidas: antes = 500 × 3 queries (FindAsync + SetValues + SaveChanges).
    // Ahora = 1 SELECT + 1 UPDATE batch.
    public async Task<int> MarkOverdueInvoicesAsync(
        Guid? actorId = null, string actorName = "Sistema")
    {
        var now = DateTime.UtcNow;

        var overdue = await _invoiceRepo.GetAll()
            .Where(i => i.Status == InvoiceStatus.Pendiente && i.DueDate < now)
            .ToListAsync();

        if (overdue.Count == 0) return 0;

        // Marcar todas en memoria, luego un único commit
        foreach (var invoice in overdue)
        {
            invoice.Status    = InvoiceStatus.Vencida;
            invoice.UpdatedAt = now;
        }

        await _invoiceRepo.UpdateRangeAsync(overdue);

        await _audit.LogAsync(
            "Facturación", "OVERDUE_JOB_EXECUTED",
            $"{overdue.Count} facturas marcadas como vencidas al {now:dd/MM/yyyy}",
            userId: actorId, userName: actorName,
            newData: System.Text.Json.JsonSerializer.Serialize(new { Count = overdue.Count, Date = now }));

        return overdue.Count;
    }

    // ── US-25 · Anular factura ─────────────────────────────────────────────

    public async Task<r> VoidInvoiceAsync(
        Guid invoiceId, string justification, Guid actorId, string actorName, string ip)
    {
        var invoice = await _invoiceRepo.GetAll()
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice is null)                                   return Result.Failure("Factura no encontrada.");
        if (invoice.Status == InvoiceStatus.Pagada)            return Result.Failure("No se puede anular una factura pagada.");
        if (invoice.Status == InvoiceStatus.Anulada)           return Result.Failure("La factura ya está anulada.");
        if (justification.Trim().Length < 10)                  return Result.Failure("La justificación debe tener al menos 10 caracteres.");

        var prevStatus    = invoice.Status.ToString();
        invoice.Status    = InvoiceStatus.Anulada;
        invoice.Notes     = justification.Trim();
        invoice.UpdatedAt = DateTime.UtcNow;
        await _invoiceRepo.UpdateAsync(invoice);

        await _audit.LogAsync(
            "Facturación", "INVOICE_VOIDED",
            $"Factura anulada: {invoice.Client?.TbnCode} — {MonthName(invoice.Month)} {invoice.Year}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: System.Text.Json.JsonSerializer.Serialize(new { Status = prevStatus }),
            newData:  System.Text.Json.JsonSerializer.Serialize(new { Status = "Anulada", Justification = justification }));

        return Result.Success();
    }

    /// <summary>
    /// Calcula el monto mensual: proporcional (días restantes/días totales) si es el
    /// mes de instalación, o precio completo en caso contrario.
    /// </summary>
    private static (decimal Amount, string? Notes) CalculateMonthlyAmount(
        decimal monthlyPrice, DateTime installDate, int year, int month)
    {
        bool isFirstMonth = installDate.Year == year && installDate.Month == month;
        if (!isFirstMonth)
            return (monthlyPrice, null);

        int daysInMonth   = DateTime.DaysInMonth(year, month);
        int remainingDays = daysInMonth - installDate.Day + 1;
        var amount        = Math.Round(monthlyPrice * remainingDays / daysInMonth, 2);
        return (amount, $"Mes proporcional: {remainingDays} de {daysInMonth} días");
    }

    private static string MonthName(int month) => month == 0 ? "Instalación" :
        new[] { "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
                "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" }[month];
}
