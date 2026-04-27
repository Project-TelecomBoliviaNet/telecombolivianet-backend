using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Invoices;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;
#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Invoices;

// InvoiceService implementa IBillingJob — es la implementación registrada en DI.
public class InvoiceService : IBillingJob
{
    private readonly IGenericRepository<Client>        _clientRepo;
    private readonly IGenericRepository<Invoice>       _invoiceRepo;
    private readonly IGenericRepository<Payment>       _paymentRepo;
    private readonly IGenericRepository<PaymentInvoice> _piRepo;
    private readonly AuditService     _audit;
    private readonly INotifPublisher  _notif;

    public InvoiceService(
        IGenericRepository<Client>        clientRepo,
        IGenericRepository<Invoice>       invoiceRepo,
        IGenericRepository<Payment>       paymentRepo,
        IGenericRepository<PaymentInvoice> piRepo,
        AuditService    audit,
        INotifPublisher notif)
    {
        _clientRepo  = clientRepo;
        _invoiceRepo = invoiceRepo;
        _paymentRepo = paymentRepo;
        _notif       = notif;
        _piRepo      = piRepo;
        _audit       = audit;
    }

    // US-21: Generar facturas mensuales (estándar ISP — filtra por fecha instalación + primer mes proporcional)
    // CORRECCIÓN (Fix #4): Reemplaza N llamadas AddAsync (N commits) por AddRangeAsync + 1 SaveChangesAsync.
    // También reemplaza N AnyAsync dentro del loop por 1 HashSet precargado.
    public async Task<BillingJobResult> GenerateMonthlyInvoicesAsync(int year, int month)
    {
        // Solo clientes instalados ANTES del fin del mes objetivo
        var monthEnd = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        var clients = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Where(c =>
                (c.Status == ClientStatus.Activo || c.Status == ClientStatus.Suspendido) &&
                c.InstallationDate < monthEnd)
            .ToListAsync();

        // Precarga de clientes que ya tienen factura — 1 query total, no N
        var existingClientIds = (await _invoiceRepo.GetAll()
            .Where(i => i.Year == year && i.Month == month && i.Type == InvoiceType.Mensualidad)
            .Select(i => i.ClientId)
            .ToListAsync()).ToHashSet();

        int generated = 0, existed = 0, skipped = 0, errors = 0;
        var dueDate = new DateTime(year, month, 5, 0, 0, 0, DateTimeKind.Utc);
        var invoicesToCreate = new List<Invoice>(clients.Count);

        foreach (var client in clients)
        {
            try
            {
                if (existingClientIds.Contains(client.Id)) { existed++; continue; }
                if (client.Plan is null) { errors++; continue; }

                // Primer mes proporcional si el cliente se instaló este mes
                bool isFirstMonth = client.InstallationDate.Year  == year &&
                                    client.InstallationDate.Month == month;
                decimal amount;
                string? notes = null;
                if (isFirstMonth)
                {
                    int daysInMonth   = DateTime.DaysInMonth(year, month);
                    int remainingDays = daysInMonth - client.InstallationDate.Day + 1;
                    amount = Math.Round(client.Plan.MonthlyPrice * remainingDays / daysInMonth, 2);
                    notes  = $"Mes proporcional: {remainingDays} de {daysInMonth} días";
                }
                else
                {
                    amount = client.Plan.MonthlyPrice;
                }

                invoicesToCreate.Add(new Invoice
                {
                    ClientId = client.Id,
                    Type     = InvoiceType.Mensualidad,
                    Status   = InvoiceStatus.Pendiente,
                    Year     = year,
                    Month    = month,
                    Amount   = amount,
                    IssuedAt = DateTime.UtcNow,
                    DueDate  = dueDate,
                    Notes    = notes
                });
                generated++;
            }
            catch { errors++; }
        }

        // Un único commit para todas las facturas — N commits → 1 round-trip
        if (invoicesToCreate.Count > 0)
        {
            await _invoiceRepo.AddRangeAsync(invoicesToCreate);
            await _invoiceRepo.SaveChangesAsync();
        }

        var summary = $"Facturación {month:D2}/{year}: {generated} generadas, {existed} ya existían, {errors} errores.";
        await _audit.LogAsync("Facturación", "BILLING_JOB_RUN", summary,
            newData: $"{{\"year\":{year},\"month\":{month},\"generated\":{generated},\"existed\":{existed},\"errors\":{errors}}}");

        return new BillingJobResult(generated, existed, skipped, errors, summary);
    }

    // US-22: Marcar vencidas
    // CORRECCIÓN (Fix #13): Reemplaza N UpdateAsync en loop por UpdateRangeAsync (1 SaveChanges).
    public async Task<int> MarkOverdueInvoicesAsync()
    {
        var now = DateTime.UtcNow;
        var overdue = await _invoiceRepo.GetAll()
            .Where(i => i.Status == InvoiceStatus.Pendiente && i.DueDate < now)
            .ToListAsync();

        if (overdue.Count == 0) return 0;

        // Agrupar por cliente para notificaciones (un mensaje por cliente, no uno por factura)
        var porCliente = overdue.GroupBy(i => i.ClientId).ToList();

        // Marcar todas como vencidas en memoria, luego un solo SaveChanges
        foreach (var inv in overdue)
        {
            inv.Status    = InvoiceStatus.Vencida;
            inv.UpdatedAt = now;
        }
        await _invoiceRepo.UpdateRangeAsync(overdue);

        // Notificar por cliente (un único mensaje WhatsApp, no uno por factura)
        foreach (var grupo in porCliente)
        {
            // El Notifier Python consume el outbox y envía el WhatsApp.
            // .NET NO envía WhatsApp directo aquí — único dueño = Notifier.
            var client = await _clientRepo.GetAll()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == grupo.Key);

            if (client is not null)
            {
                var totalVencido = grupo.Sum(i => i.Amount);
                var periodo = grupo.Count() == 1
                    ? $"{new DateTime(grupo.First().Year, grupo.First().Month, 1):MMMM yyyy}"
                    : $"{grupo.Count()} meses";

                await _notif.PublishAsync(
                    NotifType.FACTURA_VENCIDA,
                    client.Id,
                    client.PhoneMain,
                    new Dictionary<string, string>
                    {
                        ["nombre"]  = client.FullName,
                        ["monto"]   = $"{totalVencido:F2}",
                        ["periodo"] = periodo,
                        ["plan"]    = client.Plan?.Name ?? "internet",
                    },
                    referenciaId: grupo.First().Id
                );
            }
        }

        await _audit.LogAsync("Facturación", "OVERDUE_JOB_RUN",
            $"{overdue.Count} facturas marcadas como vencidas. Notificaciones encoladas: {porCliente.Count}.",
            newData: $"{{\"count\":{overdue.Count}}}");

        return overdue.Count;
    }

    // US-23: Estadísticas del mes
    public async Task<InvoiceMonthStatsDto> GetMonthStatsAsync(int year, int month)
    {
        var invoices = await _invoiceRepo.GetAll()
            .Where(i => i.Year == year && i.Month == month && i.Type == InvoiceType.Mensualidad)
            .ToListAsync();

        var billed    = invoices.Sum(i => i.Amount);
        var collected = invoices.Where(i => i.Status == InvoiceStatus.Pagada).Sum(i => i.Amount);
        var pending   = invoices.Where(i => i.Status is InvoiceStatus.Pendiente or InvoiceStatus.Vencida).Sum(i => i.Amount);
        var rate      = billed > 0 ? (decimal)Math.Round((double)collected / (double)billed * 100, 1) : 0m;

        return new InvoiceMonthStatsDto(year, month, billed, collected, pending,
            invoices.Count,
            invoices.Count(i => i.Status == InvoiceStatus.Pagada),
            invoices.Count(i => i.Status == InvoiceStatus.Pendiente),
            invoices.Count(i => i.Status == InvoiceStatus.Vencida),
            rate);
    }

    // US-23: Listado paginado con filtros
    public async Task<PagedResult<InvoiceLegacyListItemDto>> GetInvoicesAsync(InvoiceFilterDto f)
    {
        var query = _invoiceRepo.GetAll()
            .Include(i => i.Client).ThenInclude(c => c!.Plan)
            .Include(i => i.PaymentInvoices)
            .Where(i => i.Type == InvoiceType.Mensualidad);

        if (f.Year.HasValue)  query = query.Where(i => i.Year  == f.Year.Value);
        if (f.Month.HasValue) query = query.Where(i => i.Month == f.Month.Value);

        if (!string.IsNullOrWhiteSpace(f.Status) && Enum.TryParse<InvoiceStatus>(f.Status, out var status))
            query = query.Where(i => i.Status == status);

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var q = f.Search.ToLower();
            query = query.Where(i =>
                i.Client!.FullName.ToLower().Contains(q) ||
                i.Client.TbnCode.ToLower().Contains(q));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(i => i.Client!.TbnCode)
            .Skip((f.PageNumber - 1) * f.PageSize)
            .Take(f.PageSize)
            .ToListAsync();

        return new PagedResult<InvoiceLegacyListItemDto>(
            items.Select(i => new InvoiceLegacyListItemDto(
                i.Id, i.Client!.TbnCode, i.Client.FullName,
                i.Client.Plan?.Name ?? "—", i.Amount,
                i.Type.ToString(), i.Status.ToString(),
                i.Year, i.Month, i.IssuedAt, i.DueDate,
                i.Notes, i.PaymentInvoices.FirstOrDefault()?.PaymentId)),
            total, f.PageNumber, f.PageSize);
    }

    // US-25: Anular factura
    public async Task<r> VoidInvoiceAsync(Guid invoiceId, VoidInvoiceDto dto,
        Guid actorId, string actorName, string ip)
    {
        var invoice = await _invoiceRepo.GetAll()
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice is null)     return Result.Failure("Factura no encontrada.");
        if (invoice.Status == InvoiceStatus.Pagada)  return Result.Failure("No se puede anular una factura pagada.");
        if (invoice.Status == InvoiceStatus.Anulada) return Result.Failure("La factura ya está anulada.");
        if (dto.Justification.Trim().Length < 10)    return Result.Failure("La justificación debe tener al menos 10 caracteres.");

        var prevStatus    = invoice.Status.ToString();
        invoice.Status    = InvoiceStatus.Anulada;
        invoice.Notes     = dto.Justification.Trim();
        invoice.UpdatedAt = DateTime.UtcNow;
        await _invoiceRepo.UpdateAsync(invoice);

        // BUG #6 FIX: la interpolación manual de JSON rompía el formato si
        // la justificación contenía comillas (ej: 'Error "duplicado"' → JSON inválido).
        // Usar JsonSerializer garantiza escape correcto de cualquier carácter especial.
        await _audit.LogAsync("Facturación", "INVOICE_VOIDED",
            $"Factura anulada: {invoice.Client?.TbnCode} — {invoice.Month:D2}/{invoice.Year}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: System.Text.Json.JsonSerializer.Serialize(new { status = prevStatus }),
            newData:  System.Text.Json.JsonSerializer.Serialize(new
            {
                status        = "Anulada",
                justification = dto.Justification.Trim()
            }));

        return Result.Success();
    }

    // US-26: Ejecución manual
    public async Task<BillingJobResult> RunManualBillingAsync(ManualBillingDto dto,
        Guid actorId, string actorName, string ip)
    {
        var result = await GenerateMonthlyInvoicesAsync(dto.Year, dto.Month);
        await _audit.LogAsync("Facturación", "MANUAL_BILLING_RUN",
            $"Facturación manual: {result.Summary}",
            userId: actorId, userName: actorName, ip: ip);
        return result;
    }

    // US-27: Reporte anual
    public async Task<IEnumerable<AnnualReportRowDto>> GetAnnualReportAsync(AnnualReportFilterDto f)
    {
        var clientQuery = _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Include(c => c.Invoices)
                .ThenInclude(i => i.PaymentInvoices)
                    .ThenInclude(pi => pi.Payment)
            .Where(c => c.InstallationDate.Year <= f.Year);

        if (!string.IsNullOrWhiteSpace(f.Zone))
            clientQuery = clientQuery.Where(c => c.Zone.ToLower().Contains(f.Zone.ToLower()));
        if (f.PlanId.HasValue)
            clientQuery = clientQuery.Where(c => c.PlanId == f.PlanId.Value);

        clientQuery = f.SortBy == "name"
            ? clientQuery.OrderBy(c => c.FullName)
            : clientQuery.OrderBy(c => c.TbnCode);

        var clients = await clientQuery.ToListAsync();

        // Nombres de los meses para la etiqueta de cada celda
        var monthNames = new[] { "", "Ene", "Feb", "Mar", "Abr", "May", "Jun",
                                      "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        var rows = clients.Select(c =>
        {
            var instInv = c.Invoices.FirstOrDefault(i => i.Type == InvoiceType.Instalacion);

            var cells = new List<AnnualReportCellDto>();

            // Celda de instalación (Month = 0)
            if (instInv is null)
            {
                cells.Add(new AnnualReportCellDto(0, "Instalación", "NoGenerada", 0, null, null, null));
            }
            else
            {
                var instPay = instInv.PaymentInvoices.FirstOrDefault()?.Payment;
                cells.Add(new AnnualReportCellDto(
                    0, "Instalación", instInv.Status.ToString(), instInv.Amount,
                    instPay?.Method.ToString(), instPay?.PaidAt, instInv.Id));
            }

            // 12 meses
            for (int m = 1; m <= 12; m++)
            {
                if (c.InstallationDate.Year > f.Year ||
                    (c.InstallationDate.Year == f.Year && c.InstallationDate.Month > m))
                {
                    cells.Add(new AnnualReportCellDto(m, monthNames[m], "NoAplica", 0, null, null, null));
                    continue;
                }

                var inv = c.Invoices.FirstOrDefault(i =>
                    i.Type == InvoiceType.Mensualidad && i.Year == f.Year && i.Month == m);

                if (inv is null)
                {
                    cells.Add(new AnnualReportCellDto(m, monthNames[m], "NoGenerada", 0, null, null, null));
                    continue;
                }

                var pay = inv.PaymentInvoices.FirstOrDefault()?.Payment;
                cells.Add(new AnnualReportCellDto(
                    m, monthNames[m], inv.Status.ToString(), inv.Amount,
                    pay?.Method.ToString(), pay?.PaidAt, inv.Id));
            }

            bool hasDebt = cells.Any(cell => cell.Status is "Pendiente" or "Vencida");

            return new
            {
                Row = new AnnualReportRowDto(
                    c.Id, c.TbnCode, c.FullName, c.Zone, c.Plan?.Name ?? "—",
                    cells),
                HasDebt = hasDebt
            };
        });

        if (f.DebtFilter == "paid")  rows = rows.Where(x => !x.HasDebt);
        else if (f.DebtFilter == "debt") rows = rows.Where(x => x.HasDebt);

        return rows.Select(x => x.Row);
    }

    public async Task<InvoiceLegacyListItemDto?> GetByIdAsync(Guid id)
    {
        var inv = await _invoiceRepo.GetAll()
            .Include(i => i.Client).ThenInclude(c => c!.Plan)
            .Include(i => i.PaymentInvoices)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv is null) return null;

        return new InvoiceLegacyListItemDto(
            inv.Id, inv.Client!.TbnCode, inv.Client.FullName,
            inv.Client.Plan?.Name ?? "—", inv.Amount,
            inv.Type.ToString(), inv.Status.ToString(),
            inv.Year, inv.Month, inv.IssuedAt, inv.DueDate,
            inv.Notes, inv.PaymentInvoices.FirstOrDefault()?.PaymentId);
    }
}
