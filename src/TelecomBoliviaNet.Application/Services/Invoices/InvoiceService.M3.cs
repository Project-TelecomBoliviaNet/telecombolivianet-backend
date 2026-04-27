using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Services;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Invoices;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Invoices;

/// <summary>
/// M3 — Métodos adicionales de facturación:
/// US-FAC-02: Facturas extraordinarias
/// US-FAC-ESTADOS: Transición de estados
/// US-FAC-CREDITO: Aplicar crédito manualmente
/// US-FAC-CORRELATIVO: Asignar correlativo manual
/// </summary>
public class InvoiceM3Service
{
    private readonly IGenericRepository<Invoice>  _invoiceRepo;
    private readonly IGenericRepository<Client>   _clientRepo;
    private readonly InvoiceNumberService         _invNumSvc;
    private readonly INotifPublisher              _notif;
    private readonly AuditService                 _audit;

    // Motivos válidos para facturas extraordinarias
    public static readonly string[] MotivosExtraordinarios =
        { "Reconexion", "Equipo", "VisitaTecnica", "Instalacion", "Otro" };

    public InvoiceM3Service(
        IGenericRepository<Invoice> invoiceRepo,
        IGenericRepository<Client>  clientRepo,
        InvoiceNumberService        invNumSvc,
        INotifPublisher             notif,
        AuditService                audit)
    {
        _invoiceRepo = invoiceRepo;
        _clientRepo  = clientRepo;
        _invNumSvc   = invNumSvc;
        _notif       = notif;
        _audit       = audit;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-FAC-02 · Facturas extraordinarias
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea una factura extraordinaria (cargo único, no mensual).
    /// Correlativo: FE-AAAA-NNNN. Estado inicial: Emitida.
    /// </summary>
    public async Task<Result<InvoiceListItemDto>> CreateExtraordinaryAsync(
        CreateExtraordinaryInvoiceDto dto,
        Guid actorId, string actorName, string ip)
    {
        if (!MotivosExtraordinarios.Contains(dto.Motivo))
            return Result<InvoiceListItemDto>.Failure(
                $"Motivo inválido. Debe ser uno de: {string.Join(", ", MotivosExtraordinarios)}");

        if (dto.Amount <= 0)
            return Result<InvoiceListItemDto>.Failure("El monto debe ser mayor a cero.");

        var client = await _clientRepo.GetByIdAsync(dto.ClientId);
        if (client is null) return Result<InvoiceListItemDto>.Failure("Cliente no encontrado.");

        var invNumber = await _invNumSvc.NextInvoiceNumberAsync(isExtraordinary: true);
        var today     = DateTime.UtcNow;

        var invoice = new Invoice
        {
            ClientId             = dto.ClientId,
            Type                 = InvoiceType.Extraordinaria,
            Status               = InvoiceStatus.Emitida,
            Year                 = today.Year,
            Month                = today.Month,
            Amount               = dto.Amount,
            AmountPaid           = 0m,
            IssuedAt             = today,
            DueDate              = dto.DueDate.ToUniversalTime(),
            Notes                = dto.Notes,
            IsExtraordinary      = true,
            ExtraordinaryReason  = dto.Motivo,
            InvoiceNumber        = invNumber,
        };

        await _invoiceRepo.AddAsync(invoice);

        await _audit.LogAsync("Facturación", "EXTRAORDINARY_INVOICE_CREATED",
            $"Factura extraordinaria {invNumber}: {client.TbnCode} Bs.{dto.Amount:F2} motivo={dto.Motivo}",
            userId: actorId, userName: actorName, ip: ip,
            newData: System.Text.Json.JsonSerializer.Serialize(new {
                InvoiceNumber = invNumber, ClientId = dto.ClientId,
                Amount = dto.Amount, Motivo = dto.Motivo }));

        return Result<InvoiceListItemDto>.Success(MapToListItem(invoice, client));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-FAC-ESTADOS · Transición manual de estados
    // ═══════════════════════════════════════════════════════════════════════

    // CORRECCIÓN Problema #8: transiciones delegadas a InvoiceDomainService
    // La tabla de transiciones ya no está duplicada aquí.

    /// <summary>
    /// US-FAC-ESTADOS · Transiciona una factura a un nuevo estado con validación.
    /// El sistema envía notificación WhatsApp cuando pasa a Enviada.
    /// </summary>
    public async Task<Result> TransicionarEstadoAsync(
        Guid invoiceId, InvoiceStatus nuevoEstado,
        Guid actorId, string actorName, string ip)
    {
        var invoice = await _invoiceRepo.GetAll()
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice is null) return Result.Failure("Factura no encontrada.");

        if (!InvoiceDomainService.TransicionEsValida(invoice.Status, nuevoEstado))
        {
            var permitidas = InvoiceDomainService.GetTransicionesPermitidas(invoice.Status);
            return Result.Failure(
                $"Transición no válida: {invoice.Status} → {nuevoEstado}. " +
                $"Estados permitidos: {string.Join(", ", permitidas)}");
        }

        var prevStatus = invoice.Status;
        invoice.Status    = nuevoEstado;
        invoice.UpdatedAt = DateTime.UtcNow;
        await _invoiceRepo.UpdateAsync(invoice);

        // US-FAC-ESTADOS: notificación automática al pasar a Enviada
        if (nuevoEstado == InvoiceStatus.Enviada && invoice.Client is not null)
        {
            try
            {
                await _notif.PublishAsync(
                    NotifType.RECORDATORIO_R1,
                    invoice.ClientId,
                    invoice.Client.PhoneMain,
                    new Dictionary<string, string>
                    {
                        ["nombre"]            = invoice.Client.FullName,
                        ["monto"]             = invoice.Amount.ToString("F2"),
                        ["periodo"]           = $"{invoice.Month}/{invoice.Year}",
                        ["fecha_vencimiento"] = invoice.DueDate.ToString("dd/MM/yyyy"),
                        ["meses_pendientes"]  = "1",
                    });
            }
            catch { /* no fallar el flujo principal si falla la notif */ }
        }

        await _audit.LogAsync("Facturación", "INVOICE_STATUS_CHANGED",
            $"Estado factura {invoice.InvoiceNumber ?? invoice.Id.ToString()}: {prevStatus} → {nuevoEstado}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: prevStatus.ToString(), newData: nuevoEstado.ToString());

        return Result.Success();
    }

    /// <summary>US-FAC-ESTADOS · Transiciona masivamente facturas Emitidas → Enviadas.</summary>
    public async Task<int> MarcarFacturasEnviadasAsync(
        int year, int month, Guid actorId, string actorName, string ip)
    {
        var facturas = await _invoiceRepo.GetAll()
            .Include(i => i.Client)
            .Where(i => i.Year == year && i.Month == month
                     && i.Status == InvoiceStatus.Emitida)
            .ToListAsync();

        foreach (var inv in facturas)
        {
            inv.Status    = InvoiceStatus.Enviada;
            inv.UpdatedAt = DateTime.UtcNow;
        }
        await _invoiceRepo.UpdateRangeAsync(facturas);

        // Publicar recordatorio para cada cliente
        foreach (var inv in facturas.Where(i => i.Client is not null))
        {
            try
            {
                await _notif.PublishAsync(
                    NotifType.RECORDATORIO_R1,
                    inv.ClientId, inv.Client!.PhoneMain,
                    new Dictionary<string, string>
                    {
                        ["nombre"]            = inv.Client.FullName,
                        ["monto"]             = inv.Amount.ToString("F2"),
                        ["periodo"]           = $"{inv.Month}/{inv.Year}",
                        ["fecha_vencimiento"] = inv.DueDate.ToString("dd/MM/yyyy"),
                        ["meses_pendientes"]  = "1",
                    });
            }
            catch { /* continúa */ }
        }

        await _audit.LogAsync("Facturación", "INVOICES_MARKED_ENVIADAS",
            $"{facturas.Count} facturas {month}/{year} marcadas como Enviadas",
            userId: actorId, userName: actorName, ip: ip);

        return facturas.Count;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-FAC-CREDITO · Aplicar crédito manualmente a una factura
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result> AplicarCreditoAsync(
        Guid invoiceId, Guid actorId, string actorName, string ip)
    {
        var invoice = await _invoiceRepo.GetAll()
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice is null) return Result.Failure("Factura no encontrada.");
        if (invoice.Status == InvoiceStatus.Pagada) return Result.Failure("La factura ya está pagada.");
        if (invoice.Status == InvoiceStatus.Anulada) return Result.Failure("La factura está anulada.");
        if (invoice.Client is null) return Result.Failure("Cliente no encontrado.");

        var saldoPendiente = invoice.Amount - invoice.AmountPaid;
        if (saldoPendiente <= 0) return Result.Failure("La factura no tiene saldo pendiente.");

        var credito = invoice.Client.CreditBalance;
        if (credito <= 0) return Result.Failure("El cliente no tiene crédito a favor.");

        // CORRECCIÓN Problema #8: delegado a InvoiceDomainService
        var (aplicar, creditoRestante)  = InvoiceDomainService.AplicarCredito(credito, saldoPendiente);
        invoice.AmountPaid              += aplicar;
        invoice.CreditApplied           += aplicar;
        invoice.Client.CreditBalance     = creditoRestante;
        invoice.Status = InvoiceDomainService.CalcularEstado(invoice.Amount, invoice.AmountPaid);
        invoice.UpdatedAt = DateTime.UtcNow;

        await _invoiceRepo.UpdateAsync(invoice);
        await _clientRepo.UpdateAsync(invoice.Client);

        await _audit.LogAsync("Facturación", "CREDIT_APPLIED_TO_INVOICE",
            $"Crédito aplicado: factura {invoice.InvoiceNumber} Bs.{aplicar:F2}",
            userId: actorId, userName: actorName, ip: ip,
            newData: System.Text.Json.JsonSerializer.Serialize(new {
                InvoiceId = invoiceId, Aplicado = aplicar,
                NuevoSaldoCliente = invoice.Client.CreditBalance }));

        return Result.Success();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static InvoiceListItemDto MapToListItem(Invoice i, Client c) => new(
        i.Id,
        i.InvoiceNumber ?? i.Id.ToString()[..8],
        c.Id, c.TbnCode, c.FullName,
        i.Type.ToString(), i.Status.ToString(),
        i.Year, i.Month,
        i.Amount, i.AmountPaid, i.CreditApplied,
        i.IssuedAt, i.DueDate, i.UpdatedAt,
        i.Notes,
        i.IsExtraordinary, i.ExtraordinaryReason
    );
}
