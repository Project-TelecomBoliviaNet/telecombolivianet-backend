using System.Globalization;
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

public class WhatsAppReceiptService : IWhatsAppReceiptService
{
    private readonly IGenericRepository<WhatsAppReceipt>  _receiptRepo;
    private readonly IGenericRepository<Client>           _clientRepo;
    private readonly IGenericRepository<Invoice>          _invoiceRepo;
    private readonly IGenericRepository<Payment>          _paymentRepo;
    private readonly IGenericRepository<PaymentInvoice>   _piRepo;
    private readonly INotifPublisher                       _notif;
    private readonly IChatbotDirectNotifier                _chatbotNotifier;
    private readonly AuditService                          _audit;
    private readonly IUnitOfWork                           _uow;

    public WhatsAppReceiptService(
        IGenericRepository<WhatsAppReceipt>  receiptRepo,
        IGenericRepository<Client>           clientRepo,
        IGenericRepository<Invoice>          invoiceRepo,
        IGenericRepository<Payment>          paymentRepo,
        IGenericRepository<PaymentInvoice>   piRepo,
        INotifPublisher                       notif,
        IChatbotDirectNotifier                chatbotNotifier,
        AuditService                          audit,
        IUnitOfWork                           uow)
    {
        _receiptRepo     = receiptRepo;
        _clientRepo      = clientRepo;
        _invoiceRepo     = invoiceRepo;
        _paymentRepo     = paymentRepo;
        _piRepo          = piRepo;
        _notif           = notif;
        _chatbotNotifier = chatbotNotifier;
        _audit           = audit;
        _uow             = uow;
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
        if (dto.InvoiceIds.Count == 0)
            return Result<Guid>.Failure("Debes seleccionar al menos una factura.");
        if (dto.Amount <= 0)
            return Result<Guid>.Failure("El monto del pago debe ser mayor a cero.");

        var receipt = await _receiptRepo.GetAll()
            .Include(r => r.Client)
            .FirstOrDefaultAsync(r => r.Id == receiptId);

        if (receipt is null) return Result<Guid>.Failure("Comprobante no encontrado.");
        if (receipt.Status != ReceiptQueueStatus.Pendiente)
            return Result<Guid>.Failure("Este comprobante ya fue procesado.");
        if (receipt.ClientId is null)
            return Result<Guid>.Failure("No se puede aprobar el comprobante de un cliente no identificado. Asígnelo manualmente a un cliente primero.");

        if (!Enum.TryParse<PaymentMethod>(dto.Method, out var method))
            return Result<Guid>.Failure("Método de pago inválido.");

        // Verificar que las facturas pertenecen al cliente
        var invoices = await _invoiceRepo.GetAll()
            .Where(i => dto.InvoiceIds.Contains(i.Id) && i.ClientId == receipt.ClientId)
            .ToListAsync();

        if (invoices.Count != dto.InvoiceIds.Count)
            return Result<Guid>.Failure("Una o más facturas no pertenecen a este cliente.");

        // El monto registrado debe cubrir el total de las facturas seleccionadas
        var invoicesTotal = invoices.Sum(i => i.Amount);
        if (dto.Amount < invoicesTotal)
            return Result<Guid>.Failure(
                $"El monto registrado (Bs. {dto.Amount:F2}) no cubre el total de las facturas seleccionadas (Bs. {invoicesTotal:F2}).");

        await _uow.BeginTransactionAsync();
        Guid paymentId;
        decimal creditGenerated;
        try
        {
            var payment = new Payment
            {
                ClientId              = receipt.ClientId.Value,
                Amount                = dto.Amount,
                Method                = method,
                Bank                  = dto.Bank,
                BankReference         = dto.BankReference,
                PhysicalReceiptNumber = dto.PhysicalReceiptNumber,
                PaidAt                = dto.PaidAt.ToUniversalTime(),
                RegisteredByUserId    = actorId,
                RegisteredAt          = DateTime.UtcNow,
                FromWhatsApp          = true,
                ReceiptImageUrl       = receipt.ImageUrl
            };
            await _paymentRepo.AddAsync(payment);

            foreach (var inv in invoices)
            {
                inv.Status = InvoiceStatus.Pagada;
                await _invoiceRepo.UpdateAsync(inv);
                await _piRepo.AddAsync(new PaymentInvoice { PaymentId = payment.Id, InvoiceId = inv.Id });
            }

            creditGenerated = dto.Amount - invoicesTotal;
            if (creditGenerated > 0 && receipt.Client is not null)
                receipt.Client.CreditBalance += creditGenerated;

            receipt.Status      = ReceiptQueueStatus.Aprobado;
            receipt.ProcessedAt = DateTime.UtcNow;
            receipt.PaymentId   = payment.Id;
            await _receiptRepo.UpdateAsync(receipt);

            await _uow.CommitAsync();
            paymentId = payment.Id;
        }
        catch (Exception)
        {
            await _uow.RollbackAsync();
            return Result<Guid>.Failure("Error al aprobar el comprobante. La operación fue revertida.");
        }

        await _audit.LogAsync("Pagos", "WHATSAPP_RECEIPT_APPROVED",
            $"Comprobante WhatsApp aprobado: {receipt.Client?.TbnCode} — Bs. {dto.Amount}" +
            (creditGenerated > 0 ? $" (crédito generado: Bs. {creditGenerated:F2})" : ""),
            userId: actorId, userName: actorName, ip: ip);

        if (receipt.Client is not null)
        {
            try
            {
                var cultura = new CultureInfo("es-BO");
                var coveredDesc = string.Join(", ", invoices.Select(i =>
                    i.Type == InvoiceType.Instalacion
                        ? "Instalación"
                        : new DateTime(i.Year, i.Month, 1).ToString("MMMM yyyy", cultura)));

                var notifPhone = receipt.PhoneNumber ?? receipt.Client.PhoneMain;

                var windowOpen = (DateTime.UtcNow - receipt.ReceivedAt).TotalHours < 23;
                var sentDirect = windowOpen && await _chatbotNotifier.SendPaymentConfirmationAsync(
                    notifPhone, receipt.Client.FullName, dto.Amount, coveredDesc);

                if (!sentDirect)
                {
                    await _notif.PublishAsync(
                        NotifType.CONFIRMACION_PAGO,
                        receipt.Client.Id,
                        notifPhone,
                        new Dictionary<string, string>
                        {
                            ["nombre"]  = receipt.Client.FullName,
                            ["monto"]   = $"{dto.Amount:F2}",
                            ["periodo"] = coveredDesc,
                        },
                        referenciaId: paymentId);
                }
            }
            catch (Exception ex)
            {
                await _audit.LogAsync("Pagos", "NOTIFICATION_FAILED",
                    $"Error enviando confirmación a {receipt.Client.PhoneMain}: {ex.Message}",
                    userId: actorId, userName: actorName, ip: ip);
            }
        }

        return Result<Guid>.Success(paymentId);
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
        // ── Deduplicación reforzada ───────────────────────────────────────────
        // Check 1: misma URL de imagen en cualquier estado no descartado (24 h).
        //   Cubre re-envíos del mismo archivo aunque el primer comprobante ya
        //   haya sido aprobado o rechazado.
        var urlDup = await _receiptRepo.GetAll()
            .Where(r => r.ImageUrl == dto.ImageUrl &&
                        r.ReceivedAt > DateTime.UtcNow.AddHours(-24) &&
                        r.Status != ReceiptQueueStatus.NoCorresponde)
            .Select(r => r.Id)
            .FirstOrDefaultAsync();
        if (urlDup != Guid.Empty)
            return Result<Guid>.Success(urlDup);

        // Check 2: mismo teléfono + mismo monto en los últimos 5 min (re-envío
        //   rápido con diferente URL — el chatbot genera UUID por cada descarga).
        if (!string.IsNullOrWhiteSpace(dto.PhoneNumber) && dto.DeclaredAmount.HasValue)
        {
            var fiveMinAgo = DateTime.UtcNow.AddMinutes(-5);
            var rapidDup = await _receiptRepo.GetAll()
                .Where(r => r.PhoneNumber == dto.PhoneNumber &&
                            r.DeclaredAmount == dto.DeclaredAmount &&
                            r.ReceivedAt > fiveMinAgo &&
                            r.Status == ReceiptQueueStatus.Pendiente)
                .Select(r => r.Id)
                .FirstOrDefaultAsync();
            if (rapidDup != Guid.Empty)
                return Result<Guid>.Success(rapidDup);
        }

        // Enriquecer MessageText con datos OCR si están disponibles
        var textParts = new List<string?> { dto.MessageText };
        if (!string.IsNullOrWhiteSpace(dto.OcrBank))    textParts.Add($"Banco: {dto.OcrBank}");
        if (!string.IsNullOrWhiteSpace(dto.OcrDate))    textParts.Add($"Fecha: {dto.OcrDate}");
        if (!string.IsNullOrWhiteSpace(dto.OcrRawText)) textParts.Add($"OCR: {dto.OcrRawText}");
        var enrichedText = string.Join(" | ", textParts.Where(p => !string.IsNullOrWhiteSpace(p)));

        // Validar que el ClientId del chatbot existe en la tabla Clients.
        // Una sesión Redis puede quedar desactualizada si el cliente fue eliminado
        // o re-creado en el backend, causando una violación de FK al insertar.
        // Si no existe, se guarda con ClientId null para asignación manual.
        Guid? resolvedClientId = null;
        if (dto.ClientId.HasValue)
        {
            var clientExists = await _clientRepo.GetAll()
                .AnyAsync(c => c.Id == dto.ClientId.Value);
            resolvedClientId = clientExists ? dto.ClientId : null;
        }

        var receipt = new WhatsAppReceipt
        {
            ClientId       = resolvedClientId,
            PhoneNumber    = dto.PhoneNumber,   // guardamos el número WhatsApp real
            ImageUrl       = dto.ImageUrl,
            MessageText    = enrichedText,
            DeclaredAmount = dto.DeclaredAmount,
            Status         = ReceiptQueueStatus.Pendiente,
            ReceivedAt     = DateTime.UtcNow,
        };

        await _receiptRepo.AddAsync(receipt);
        await _receiptRepo.SaveChangesAsync();
        return Result<Guid>.Success(receipt.Id);
    }

    // ── BUG-09: Asignar cliente a comprobante huérfano ───────────────────────

    public async Task<Result<bool>> AssignClientAsync(
        Guid receiptId, Guid clientId, Guid actorId, string actorName, string ip)
    {
        var receipt = await _receiptRepo.GetAll()
            .FirstOrDefaultAsync(r => r.Id == receiptId);

        if (receipt is null)
            return Result<bool>.Failure("Comprobante no encontrado.");
        if (receipt.Status != ReceiptQueueStatus.Pendiente)
            return Result<bool>.Failure("Solo se puede asignar cliente a comprobantes pendientes.");
        if (receipt.ClientId is not null)
            return Result<bool>.Failure("Este comprobante ya tiene un cliente asignado.");

        var client = await _clientRepo.GetAll()
            .FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null)
            return Result<bool>.Failure("Cliente no encontrado.");

        receipt.ClientId = clientId;
        await _receiptRepo.UpdateAsync(receipt);
        await _receiptRepo.SaveChangesAsync();

        await _audit.LogAsync(
            "Payments", "WA_RECEIPT_ASSIGNED",
            $"Comprobante {receiptId} asignado manualmente a cliente {client.TbnCode} ({client.FullName})",
            actorId, actorName, ip,
            entityId: receiptId);

        return Result<bool>.Success(true);
    }

    private static WhatsAppReceiptDto MapToDto(WhatsAppReceipt r) =>
        new(r.Id, r.ClientId,
            r.Client?.TbnCode   ?? "—",
            r.Client?.FullName  ?? "—",
            r.Client?.PhoneMain ?? "—",
            r.ImageUrl, r.MessageText, r.DeclaredAmount,
            r.Status.ToString(), r.ReceivedAt);
}
