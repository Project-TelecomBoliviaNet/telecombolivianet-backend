using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Payments;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Payments;

public class WhatsAppReceiptService
{
    private readonly IGenericRepository<WhatsAppReceipt>  _receiptRepo;
    private readonly IGenericRepository<Client>           _clientRepo;
    private readonly IGenericRepository<Invoice>          _invoiceRepo;
    private readonly IGenericRepository<Payment>          _paymentRepo;
    private readonly IGenericRepository<PaymentInvoice>   _piRepo;
    private readonly INotifPublisher                       _notif;      // US-30: vía outbox → Notifier Python
    private readonly AuditService                          _audit;

    public WhatsAppReceiptService(
        IGenericRepository<WhatsAppReceipt>  receiptRepo,
        IGenericRepository<Client>           clientRepo,
        IGenericRepository<Invoice>          invoiceRepo,
        IGenericRepository<Payment>          paymentRepo,
        IGenericRepository<PaymentInvoice>   piRepo,
        INotifPublisher                       notif,
        AuditService                          audit)
    {
        _receiptRepo = receiptRepo;
        _clientRepo  = clientRepo;
        _invoiceRepo = invoiceRepo;
        _paymentRepo = paymentRepo;
        _piRepo      = piRepo;
        _notif       = notif;
        _audit       = audit;
    }

    // ── Listar cola pendiente ─────────────────────────────────────────────────

    public async Task<PagedResult<WhatsAppReceiptDto>> GetQueueAsync(
        int page = 1, int pageSize = 25, string status = "Pendiente")
    {
        // BUG FIX: Enum.Parse<ReceiptQueueStatus>(status) lanza FormatException si el
        // usuario pasa un valor inválido. Usar TryParse con fallback a Pendiente.
        if (!Enum.TryParse<ReceiptQueueStatus>(status, ignoreCase: true, out var parsedStatus))
            parsedStatus = ReceiptQueueStatus.Pendiente;

        var query = _receiptRepo.GetAll()
            .Include(r => r.Client)
            .Where(r => r.Status == parsedStatus)
            .OrderByDescending(r => r.ReceivedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return new PagedResult<WhatsAppReceiptDto>(items.Select(MapToDto),
            total, page, pageSize);
    }

    // ── Contar pendientes (para badge del sidebar) ────────────────────────────

    public async Task<int> CountPendingAsync() =>
        await _receiptRepo.GetAll()
            .CountAsync(r => r.Status == ReceiptQueueStatus.Pendiente);

    // ── Aprobar comprobante (US-30) ───────────────────────────────────────────

    public async Task<Result<Guid>> ApproveAsync(
        Guid receiptId, ApproveReceiptDto dto,
        Guid actorId, string actorName, string ip)
    {
        var receipt = await _receiptRepo.GetAll()
            .Include(r => r.Client)
            .FirstOrDefaultAsync(r => r.Id == receiptId);

        if (receipt is null) return Result<Guid>.Failure("Comprobante no encontrado.");
        if (receipt.Status != ReceiptQueueStatus.Pendiente)
            return Result<Guid>.Failure("Este comprobante ya fue procesado.");

        if (!Enum.TryParse<PaymentMethod>(dto.Method, out var method))
            return Result<Guid>.Failure("Método de pago inválido.");

        // Verificar que las facturas pertenecen al cliente
        var invoices = await _invoiceRepo.GetAll()
            .Where(i => dto.InvoiceIds.Contains(i.Id) && i.ClientId == receipt.ClientId)
            .ToListAsync();

        if (invoices.Count != dto.InvoiceIds.Count)
            return Result<Guid>.Failure("Una o más facturas no pertenecen a este cliente.");

        // Crear el pago
        var payment = new Payment
        {
            ClientId              = receipt.ClientId,
            Amount                = dto.Amount,
            Method                = method,
            Bank                  = dto.Bank,
            PhysicalReceiptNumber = dto.PhysicalReceiptNumber,
            PaidAt                = dto.PaidAt.ToUniversalTime(),
            RegisteredByUserId    = actorId,
            RegisteredAt          = DateTime.UtcNow,
            FromWhatsApp          = true,
            ReceiptImageUrl       = receipt.ImageUrl  // adjuntar imagen automáticamente
        };
        await _paymentRepo.AddAsync(payment);

        // Marcar facturas como pagadas
        foreach (var inv in invoices)
        {
            inv.Status = InvoiceStatus.Pagada;
            await _invoiceRepo.UpdateAsync(inv);
            await _piRepo.AddAsync(new PaymentInvoice { PaymentId = payment.Id, InvoiceId = inv.Id });
        }

        // Actualizar estado del comprobante en la cola
        receipt.Status      = ReceiptQueueStatus.Aprobado;
        receipt.ProcessedAt = DateTime.UtcNow;
        receipt.PaymentId   = payment.Id;
        await _receiptRepo.UpdateAsync(receipt);

        await _audit.LogAsync("Pagos", "WHATSAPP_RECEIPT_APPROVED",
            $"Comprobante WhatsApp aprobado: {receipt.Client?.TbnCode} — Bs. {dto.Amount}",
            userId: actorId, userName: actorName, ip: ip);

        // US-30: confirmación al cliente vía outbox → Notifier Python envía WhatsApp
        if (receipt.Client is not null)
        {
            var coveredDesc = string.Join(", ", invoices.Select(i =>
                i.Type == InvoiceType.Instalacion
                    ? "Instalación"
                    : $"{new DateTime(i.Year, i.Month, 1):MMMM yyyy}"));

            await _notif.PublishAsync(
                NotifType.CONFIRMACION_PAGO,
                receipt.Client.Id,
                receipt.Client.PhoneMain,
                new Dictionary<string, string>
                {
                    ["nombre"]  = receipt.Client.FullName,
                    ["monto"]   = $"{dto.Amount:F2}",
                    ["periodo"] = coveredDesc,
                },
                referenciaId: payment.Id);
        }

        return Result<Guid>.Success(payment.Id);
    }

    // ── Rechazar comprobante (US-30) ──────────────────────────────────────────

    public async Task<Result<bool>> RejectAsync(
        Guid receiptId, RejectReceiptDto dto,
        Guid actorId, string actorName, string ip)
    {
        var receipt = await _receiptRepo.GetAll()
            .Include(r => r.Client)
            .FirstOrDefaultAsync(r => r.Id == receiptId);

        if (receipt is null) return Result<bool>.Failure("Comprobante no encontrado.");
        if (receipt.Status != ReceiptQueueStatus.Pendiente)
            return Result<bool>.Failure("Este comprobante ya fue procesado.");

        receipt.Status        = ReceiptQueueStatus.Rechazado;
        receipt.ProcessedAt   = DateTime.UtcNow;
        receipt.RejectionNote = dto.Reason;
        await _receiptRepo.UpdateAsync(receipt);

        await _audit.LogAsync("Pagos", "WHATSAPP_RECEIPT_REJECTED",
            $"Comprobante rechazado: {receipt.Client?.TbnCode} — {dto.Reason}",
            userId: actorId, userName: actorName, ip: ip);

        // US-30: notificar rechazo al cliente vía outbox → Notifier Python envía WhatsApp
        // Reutilizamos CONFIRMACION_PAGO con periodo descriptivo del motivo de rechazo
        if (receipt.Client is not null)
        {
            await _notif.PublishAsync(
                NotifType.CONFIRMACION_PAGO,
                receipt.Client.Id,
                receipt.Client.PhoneMain,
                new Dictionary<string, string>
                {
                    ["nombre"]  = receipt.Client.FullName,
                    ["monto"]   = $"{receipt.DeclaredAmount:F2}",
                    ["periodo"] = $"⚠️ Comprobante rechazado — {dto.Reason}. Por favor envíe el comprobante correcto.",
                });
        }

        return Result<bool>.Success(true);
    }

    // ── Marcar como No Corresponde (US-30) ────────────────────────────────────

    public async Task<Result<bool>> MarkNotRelatedAsync(
        Guid receiptId, Guid actorId, string actorName, string ip)
    {
        var receipt = await _receiptRepo.GetByIdAsync(receiptId);
        if (receipt is null) return Result<bool>.Failure("Comprobante no encontrado.");

        receipt.Status        = ReceiptQueueStatus.NoCorresponde;
        receipt.ProcessedAt   = DateTime.UtcNow;
        receipt.RejectionNote = "No corresponde a un comprobante de pago.";
        await _receiptRepo.UpdateAsync(receipt);

        await _audit.LogAsync("Pagos", "WHATSAPP_RECEIPT_DISCARDED",
            $"Comprobante descartado (no corresponde) — ID {receiptId}",
            userId: actorId, userName: actorName, ip: ip);

        return Result<bool>.Success(true);
    }

    // ── Recibir comprobante desde el chatbot NestJS (US-06) ───────────────────
    // El bot llama POST /api/payments/whatsapp-receipt cuando un cliente
    // envía una imagen de pago por WhatsApp. Crea la entrada en la cola
    // para revisión manual por el admin/operador.
    public async Task<Result<Guid>> ReceiveFromBotAsync(SubmitWhatsappReceiptDto dto)
    {
        // Si el bot no identificó al cliente, ClientId viene null → Guid.Empty como señal.
        var clientId = dto.ClientId ?? Guid.Empty;

        // Enriquecer MessageText con datos OCR si están disponibles
        var textParts = new List<string?> { dto.MessageText };
        if (!string.IsNullOrWhiteSpace(dto.OcrBank))    textParts.Add($"Banco: {dto.OcrBank}");
        if (!string.IsNullOrWhiteSpace(dto.OcrDate))    textParts.Add($"Fecha: {dto.OcrDate}");
        if (!string.IsNullOrWhiteSpace(dto.OcrRawText)) textParts.Add($"OCR: {dto.OcrRawText}");
        if (!string.IsNullOrWhiteSpace(dto.PhoneNumber)) textParts.Add($"Tel: {dto.PhoneNumber}");
        var enrichedText = string.Join(" | ", textParts.Where(p => !string.IsNullOrWhiteSpace(p)));

        var receipt = new WhatsAppReceipt
        {
            ClientId       = clientId,
            ImageUrl       = dto.ImageUrl,
            MessageText    = enrichedText,
            DeclaredAmount = dto.DeclaredAmount,
            Status         = ReceiptQueueStatus.Pendiente,
            ReceivedAt     = DateTime.UtcNow,
        };

        await _receiptRepo.AddAsync(receipt);
        return Result<Guid>.Success(receipt.Id);
    }

    private static WhatsAppReceiptDto MapToDto(WhatsAppReceipt r) =>
        new(r.Id, r.ClientId,
            r.Client?.TbnCode   ?? "—",
            r.Client?.FullName  ?? "—",
            r.Client?.PhoneMain ?? "—",
            r.ImageUrl, r.MessageText, r.DeclaredAmount,
            r.Status.ToString(), r.ReceivedAt);
}
