using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Payments;

public class PaymentService
{
    private readonly IGenericRepository<Payment>        _paymentRepo;
    private readonly IGenericRepository<Invoice>        _invoiceRepo;
    private readonly IGenericRepository<Client>         _clientRepo;
    private readonly IGenericRepository<PaymentInvoice> _piRepo;
    private readonly IFileStorage                       _fileStorage;
    private readonly INotifPublisher                    _notif;
    private readonly AuditService                       _audit;
    private readonly IUnitOfWork                        _uow;

    public PaymentService(
        IGenericRepository<Payment>        paymentRepo,
        IGenericRepository<Invoice>        invoiceRepo,
        IGenericRepository<Client>         clientRepo,
        IGenericRepository<PaymentInvoice> piRepo,
        IFileStorage                       fileStorage,
        INotifPublisher                    notif,
        AuditService                       audit,
        IUnitOfWork                        uow)
    {
        _paymentRepo = paymentRepo;
        _invoiceRepo = invoiceRepo;
        _clientRepo  = clientRepo;
        _piRepo      = piRepo;
        _fileStorage = fileStorage;
        _notif       = notif;
        _audit       = audit;
        _uow         = uow;
    }

    // ── US-28 · Listado centralizado de pagos ─────────────────────────────────

    public async Task<PagedResult<PaymentListItemDto>> GetAllAsync(PaymentFilterDto filter)
    {
        var query = _paymentRepo.GetAll()
            .Include(p => p.Client)
            .Include(p => p.RegisteredBy)
            .Include(p => p.PaymentInvoices).ThenInclude(pi => pi.Invoice)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var q = filter.Search.ToLower();
            query = query.Where(p =>
                (p.Client != null && (p.Client.FullName.ToLower().Contains(q) ||
                                      p.Client.TbnCode.ToLower().Contains(q))));
        }

        if (!string.IsNullOrWhiteSpace(filter.Method) && filter.Method != "all")
            if (Enum.TryParse<PaymentMethod>(filter.Method, out var m))
                query = query.Where(p => p.Method == m);

        if (!string.IsNullOrWhiteSpace(filter.Origin))
        {
            var fromWa = filter.Origin == "WhatsApp";
            query = query.Where(p => p.FromWhatsApp == fromWa);
        }

        if (filter.From.HasValue)
            query = query.Where(p => p.PaidAt >= filter.From.Value.ToUniversalTime());

        if (filter.To.HasValue)
            query = query.Where(p => p.PaidAt <= filter.To.Value.ToUniversalTime().AddDays(1));

        query = query.OrderByDescending(p => p.PaidAt);

        var total    = await query.CountAsync();
        var payments = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PagedResult<PaymentListItemDto>(
            payments.Select(MapToListItem), total, filter.PageNumber, filter.PageSize);
    }

    // ── US-28 · Detalle de un pago ────────────────────────────────────────────

    public async Task<PaymentDetailDto?> GetByIdAsync(Guid id)
    {
        var p = await _paymentRepo.GetAll()
            .Include(p => p.Client)
            .Include(p => p.RegisteredBy)
            .Include(p => p.PaymentInvoices).ThenInclude(pi => pi.Invoice)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (p is null) return null;

        var canVoid  = (DateTime.UtcNow - p.RegisteredAt).TotalDays <= 30;
        var isVoided = p.IsVoided;
        return new PaymentDetailDto(
            p.Id, p.ClientId,
            p.Client?.TbnCode   ?? "—",
            p.Client?.FullName  ?? "—",
            p.Client?.PhoneMain ?? "—",
            p.Amount, p.Method.ToString(), p.Bank,
            p.PaidAt, p.RegisteredAt,
            p.RegisteredBy?.FullName ?? "Sistema",
            p.FromWhatsApp, p.ReceiptImageUrl, p.PhysicalReceiptNumber,
            BuildCoveredMonths(p), canVoid, isVoided);
    }

    // ── US-29 · Adjuntar imagen de comprobante ────────────────────────────────

    public async Task<Result<string>> AttachReceiptImageAsync(
        Guid paymentId, Stream stream, string fileName, string contentType,
        Guid actorId, string actorName, string ip)
    {
        var payment = await _paymentRepo.GetAll()
            .Include(p => p.Client)
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (payment is null) return Result<string>.Failure("Pago no encontrado.");

        // Eliminar imagen anterior si existe
        if (!string.IsNullOrEmpty(payment.ReceiptImageUrl))
            await _fileStorage.DeleteAsync(payment.ReceiptImageUrl);

        // Nombre con código TBN y fecha
        var tbn = payment.Client?.TbnCode ?? "TBN";
        var newFileName = $"{tbn}_{payment.PaidAt:yyyyMMdd}_{fileName}";
        var url = await _fileStorage.SaveAsync(stream, newFileName, contentType);

        payment.ReceiptImageUrl = url;
        await _paymentRepo.UpdateAsync(payment);

        await _audit.LogAsync("Pagos", "RECEIPT_ATTACHED",
            $"Comprobante adjuntado al pago de {tbn}",
            userId: actorId, userName: actorName, ip: ip,
            newData: JsonSerializer.Serialize(new { PaymentId = paymentId, Url = url }));

        return Result<string>.Success(url);
    }

    // ── US-31 · Anular pago ───────────────────────────────────────────────────

    public async Task<Result<VoidPaymentResultDto>> VoidPaymentAsync(
        Guid paymentId, string justification, Guid actorId, string actorName, string ip)
    {
        if (justification.Trim().Length < 10)
            return Result<VoidPaymentResultDto>.Failure(
                "La justificación debe tener al menos 10 caracteres.");

        var payment = await _paymentRepo.GetAll()
            .Include(p => p.Client)
            .Include(p => p.PaymentInvoices).ThenInclude(pi => pi.Invoice)
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (payment is null)
            return Result<VoidPaymentResultDto>.Failure("Pago no encontrado.");

        // US-31: solo dentro de 30 días
        if ((DateTime.UtcNow - payment.RegisteredAt).TotalDays > 30)
            return Result<VoidPaymentResultDto>.Failure(
                "Solo se pueden anular pagos registrados en los últimos 30 días.");

        var now = DateTime.UtcNow;

        // Revertir facturas en un único SaveChangesAsync (reduce ventana de inconsistencia)
        var invoicesToRevert = payment.PaymentInvoices
            .Where(pi => pi.Invoice is not null)
            .Select(pi =>
            {
                pi.Invoice!.Status    = pi.Invoice.DueDate < now
                                        ? InvoiceStatus.Vencida
                                        : InvoiceStatus.Pendiente;
                pi.Invoice.UpdatedAt = now;
                return pi.Invoice;
            })
            .ToList();

        int reverted = invoicesToRevert.Count;

        // US-31: marcar como anulado sin corromper los datos históricos
        payment.IsVoided          = true;
        payment.VoidJustification = justification.Trim();
        payment.VoidedAt          = now;
        payment.VoidedByUserId    = actorId;

        // Transacción explícita: facturas + pago anulado en un único commit atómico.
        // Si el segundo SaveChangesAsync falla, el primero se deshace junto con él.
        await _uow.BeginTransactionAsync();
        try
        {
            await _invoiceRepo.UpdateRangeAsync(invoicesToRevert);
            await _paymentRepo.UpdateAsync(payment);
            await _uow.CommitAsync();
        }
        catch
        {
            await _uow.RollbackAsync();
            return Result<VoidPaymentResultDto>.Failure(
                "Error al anular el pago. Los datos no fueron modificados.");
        }

        // Auditoría y notificación fuera de la transacción:
        // un fallo aquí no debe deshacer la anulación ya confirmada en BD.
        var tbn = payment.Client?.TbnCode ?? "—";

        await _audit.LogAsync("Pagos", "PAYMENT_VOIDED",
            $"Pago anulado: {tbn} — {justification}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: JsonSerializer.Serialize(new { payment.Amount, Status = "Activo" }),
            newData:  JsonSerializer.Serialize(new { Amount = 0, Status = "Anulado", Justification = justification }));

        // US-31: notificar al cliente por WhatsApp vía outbox (Notifier Python lo envía)
        if (payment.Client is not null)
        {
            // Reutilizamos CONFIRMACION_PAGO con contexto especial de anulación
            // El admin puede personalizar la plantilla en el panel de notificaciones
            await _notif.PublishAsync(
                NotifType.CONFIRMACION_PAGO,
                payment.Client.Id,
                payment.Client.PhoneMain,
                new Dictionary<string, string>
                {
                    ["nombre"]  = payment.Client.FullName,
                    ["monto"]   = $"{payment.Amount:F2}",
                    ["periodo"] = "Pago anulado — sus facturas vuelven a estar pendientes",
                });
        }

        return Result<VoidPaymentResultDto>.Success(new VoidPaymentResultDto(
            reverted, $"Pago anulado. {reverted} factura(s) volvieron a estado pendiente/vencida."));
    }

    // ── US-32 · Reporte de cobranza ───────────────────────────────────────────

    public async Task<CollectionReportDto> GetCollectionReportAsync(DateTime from, DateTime to)
    {
        var utcFrom = from.ToUniversalTime();
        var utcTo   = to.ToUniversalTime().AddDays(1);

        var payments = await _paymentRepo.GetAll()
            .Include(p => p.Client)
            .Include(p => p.RegisteredBy)
            .Include(p => p.PaymentInvoices).ThenInclude(pi => pi.Invoice)
            .Where(p => p.PaidAt >= utcFrom && p.PaidAt < utcTo && !p.IsVoided)
            .ToListAsync();

        var total   = payments.Sum(p => p.Amount);
        var count   = payments.Count;
        var avg     = count > 0 ? Math.Round(total / count, 2) : 0;

        var cash    = payments.Where(p => p.Method == PaymentMethod.Efectivo).Sum(p => p.Amount);
        var deposit = payments.Where(p => p.Method == PaymentMethod.DepositoBancario).Sum(p => p.Amount);
        var qr      = payments.Where(p => p.Method == PaymentMethod.QR).Sum(p => p.Amount);

        var byUser = payments
            .GroupBy(p => p.RegisteredBy?.FullName ?? "Sistema")
            .Select(g => new CollectionByUserDto(g.Key, g.Count(), g.Sum(p => p.Amount)))
            .OrderByDescending(u => u.Total);

        return new CollectionReportDto(
            from, to, total, count, avg,
            cash, deposit, qr, byUser,
            payments.Select(MapToListItem));
    }

    // ── US-35 · Verificar duplicados ──────────────────────────────────────────

    public async Task<DuplicateCheckResultDto> CheckDuplicateAsync(
        Guid clientId, decimal amount, DateTime paidAt)
    {
        var threeDaysAgo = paidAt.AddDays(-3).ToUniversalTime();
        var paidAtUtc    = paidAt.ToUniversalTime().AddDays(1);

        var existing = await _paymentRepo.GetAll()
            .Include(p => p.RegisteredBy)
            .Where(p => p.ClientId == clientId
                     && p.Amount   == amount
                     && p.PaidAt   >= threeDaysAgo
                     && p.PaidAt   <= paidAtUtc
                     && p.Amount   > 0)
            .FirstOrDefaultAsync();

        if (existing is null)
            return new DuplicateCheckResultDto(false, null, null, null, null, null);

        return new DuplicateCheckResultDto(
            true, existing.Id, existing.PaidAt, existing.Amount,
            existing.Method.ToString(), existing.RegisteredBy?.FullName);
    }

    // ── US-34 · Job de recordatorio (lógica reutilizable) ────────────────────
    // NOTA (Bloque 3): Los recordatorios automáticos R1/R2/R3 los gestiona el
    // ReminderJob de Python. Este método es para recordatorios manuales desde
    // el panel admin (botón "Enviar recordatorios" en PaymentsController).
    // Se publican al outbox con tipo FACTURA_VENCIDA — Notifier Python los envía.

    public async Task<ReminderJobResultDto> SendOverdueRemindersAsync(
        Guid? actorId = null, string actorName = "Sistema")
    {
        var overdueClients = await _clientRepo.GetAll()
            .Include(c => c.Invoices)
            .Where(c => (c.Status == ClientStatus.Activo || c.Status == ClientStatus.Suspendido)
                     && c.Invoices.Any(i => i.Status == InvoiceStatus.Vencida))
            .ToListAsync();

        int sent = 0, skipped = 0, errors = 0;

        foreach (var client in overdueClients)
        {
            try
            {
                var vencidas = client.Invoices.Where(i => i.Status == InvoiceStatus.Vencida).ToList();
                var deuda    = vencidas.Sum(i => i.Amount);
                var meses    = vencidas.Count;

                // Publicar al outbox — Notifier Python consume y envía WhatsApp
                await _notif.PublishAsync(
                    NotifType.FACTURA_VENCIDA,
                    client.Id,
                    client.PhoneMain,
                    new Dictionary<string, string>
                    {
                        ["nombre"]  = client.FullName,
                        ["monto"]   = $"{deuda:F2}",
                        ["periodo"] = $"{meses} mes(es) vencido(s)",
                    },
                    referenciaId: vencidas.First().Id
                );
                sent++;
            }
            catch
            {
                errors++;
            }
        }

        await _audit.LogAsync(
            "Pagos", "REMINDER_JOB_EXECUTED",
            $"Recordatorios encolados en outbox: {sent} clientes, {errors} errores",
            userId: actorId, userName: actorName,
            newData: JsonSerializer.Serialize(new { Enqueued = sent, Errors = errors }));

        return new ReminderJobResultDto(sent, skipped, errors,
            DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PaymentListItemDto MapToListItem(Payment p) =>
        new(p.Id, p.ClientId,
            p.Client?.TbnCode   ?? "—",
            p.Client?.FullName  ?? "—",
            p.Amount, p.Method.ToString(), p.Bank,
            p.PaidAt, p.RegisteredAt,
            p.RegisteredBy?.FullName ?? "Sistema",
            p.FromWhatsApp, p.ReceiptImageUrl, p.PhysicalReceiptNumber,
            BuildCoveredMonths(p));

    private static IEnumerable<string> BuildCoveredMonths(Payment p) =>
        p.PaymentInvoices.Select(pi =>
            pi.Invoice?.Type == InvoiceType.Instalacion
                ? "Instalación"
                : pi.Invoice is null ? "—"
                : $"{new DateTime(pi.Invoice.Year, pi.Invoice.Month, 1):MMMM yyyy}");
}

// ════════════════════════════════════════════════════════════════════════════
// M2 — NUEVOS MÉTODOS
// ════════════════════════════════════════════════════════════════════════════

// NOTE: To avoid editing the existing class brace, these methods are added
// as extension methods in PaymentService.M2Extensions.cs
