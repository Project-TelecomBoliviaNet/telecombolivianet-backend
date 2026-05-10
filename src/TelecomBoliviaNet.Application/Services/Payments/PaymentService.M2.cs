using System.Globalization;
using TelecomBoliviaNet.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Payments;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;
#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Payments;

/// <summary>
/// Crédito a favor y recibos de pago.
/// US-PAG-CREDITO · US-PAG-RECIBO
/// CashClose extraído a CashCloseService; GetCollectionByOperator extraído a PaymentService.
/// </summary>
public class PaymentCreditService : IPaymentCreditService
{
    private readonly IGenericRepository<Payment>        _paymentRepo;
    private readonly IGenericRepository<Invoice>        _invoiceRepo;
    private readonly IGenericRepository<Client>         _clientRepo;
    private readonly IGenericRepository<PaymentInvoice> _piRepo;
    private readonly IGenericRepository<PaymentReceipt> _receiptRepo;
    private readonly ISequenceGenerator                 _seqGen;
    private readonly INotifPublisher                    _notif;
    private readonly AuditService                       _audit;
    private readonly IUnitOfWork                        _uow;

    public PaymentCreditService(
        IGenericRepository<Payment>        paymentRepo,
        IGenericRepository<Invoice>        invoiceRepo,
        IGenericRepository<Client>         clientRepo,
        IGenericRepository<PaymentInvoice> piRepo,
        IGenericRepository<PaymentReceipt> receiptRepo,
        ISequenceGenerator                 seqGen,
        INotifPublisher                    notif,
        AuditService                       audit,
        IUnitOfWork                        uow)
    {
        _paymentRepo = paymentRepo;
        _invoiceRepo = invoiceRepo;
        _clientRepo  = clientRepo;
        _piRepo      = piRepo;
        _receiptRepo = receiptRepo;
        _seqGen      = seqGen;
        _notif       = notif;
        _audit       = audit;
        _uow         = uow;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-PAG-CREDITO · Crédito a favor cuando el pago excede la deuda
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result<PaymentRegisteredDto>> RegisterPaymentWithCreditAsync(
        RegisterPaymentDto dto, Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c => c.Id == dto.ClientId);
        if (client is null)
            return Result<PaymentRegisteredDto>.Failure("Cliente no encontrado.");

        var invoices = await LoadAndValidateInvoicesAsync(dto);
        if (invoices is null)
            return Result<PaymentRegisteredDto>.Failure("Facturas inválidas o no pertenecen al cliente.");

        var deudaTotal            = invoices.Sum(i => i.Amount - i.AmountPaid);
        var (excedente, creditoUsado) = CalculateCredit(dto.Amount, client.CreditBalance, deudaTotal);
        var prevCredit            = client.CreditBalance;

        await _uow.BeginTransactionAsync();
        try
        {
            var payment = new Payment
            {
                ClientId              = dto.ClientId,
                Amount                = dto.Amount,
                Method                = Enum.Parse<PaymentMethod>(dto.Method),
                Bank                  = dto.Bank,
                BankReference         = dto.BankReference,
                PaidAt                = dto.PaidAt.ToUniversalTime(),
                RegisteredAt          = DateTime.UtcNow,
                RegisteredByUserId    = actorId,
                PhysicalReceiptNumber = dto.PhysicalReceiptNumber,
            };
            await _paymentRepo.AddAsync(payment);

            var invoiceNumbers = await ApplyPaymentToInvoicesAsync(
                payment, invoices, dto.Amount + client.CreditBalance);

            client.CreditBalance = excedente;
            await _clientRepo.UpdateAsync(client);

            var receiptNumber = await _seqGen.NextReceiptNumberAsync();
            await _receiptRepo.AddAsync(new PaymentReceipt
            {
                ReceiptNumber     = receiptNumber,
                PaymentId         = payment.Id,
                ClientId          = dto.ClientId,
                GeneratedByUserId = actorId,
                Amount            = dto.Amount,
                Method            = dto.Method,
                Bank              = dto.Bank,
                PaidAt            = payment.PaidAt,
                InvoiceNumbers    = string.Join(", ", invoiceNumbers),
                PdfPath           = string.Empty,
                GeneratedAt       = DateTime.UtcNow,
            });

            await _uow.CommitAsync();

            try { await PublishPaymentNotificationAsync(client, dto.Amount, invoices); }
            catch { /* notificación no crítica */ }

            await _audit.LogAsync("Pagos", "PAYMENT_REGISTERED_WITH_CREDIT",
                $"Pago registrado: {client.TbnCode} monto={dto.Amount:F2} excedente={excedente:F2}",
                userId: actorId, userName: actorName, ip: ip,
                prevData: JsonSerializer.Serialize(new { PrevCredit = prevCredit }),
                newData:  JsonSerializer.Serialize(new { NewCredit = excedente, PaymentId = payment.Id }));

            return Result<PaymentRegisteredDto>.Success(new PaymentRegisteredDto(
                payment.Id, receiptNumber, dto.Amount, excedente, creditoUsado,
                $"Pago registrado. {(excedente > 0 ? $"Crédito a favor: Bs. {excedente:F2}" : string.Empty)}"));
        }
        catch (Exception)
        {
            await _uow.RollbackAsync();
            return Result<PaymentRegisteredDto>.Failure(
                "Error al registrar el pago. La operación fue revertida completamente.");
        }
    }

    private async Task<List<Invoice>?> LoadAndValidateInvoicesAsync(RegisterPaymentDto dto)
    {
        var clientIds = await _invoiceRepo.GetAll()
            .Where(i => dto.InvoiceIds.Contains(i.Id))
            .Select(i => i.ClientId)
            .Distinct()
            .ToListAsync();

        if (clientIds.Count == 0 || clientIds.Any(id => id != dto.ClientId))
            return null;

        var invoices = await _invoiceRepo.GetAll()
            .Where(i => dto.InvoiceIds.Contains(i.Id) && i.ClientId == dto.ClientId)
            .ToListAsync();

        return invoices.Count > 0 ? invoices : null;
    }

    private static (decimal excedente, decimal creditoUsado) CalculateCredit(
        decimal amount, decimal creditBalance, decimal totalDebt)
    {
        var effective = amount + creditBalance;
        if (effective > totalDebt)
            return (effective - totalDebt, Math.Min(creditBalance, totalDebt));
        return (0m, Math.Min(creditBalance, effective));
    }

    private async Task<List<string>> ApplyPaymentToInvoicesAsync(
        Payment payment, List<Invoice> invoices, decimal effectiveAmount)
    {
        var invoiceNumbers = new List<string>();
        var remaining      = effectiveAmount;

        foreach (var inv in invoices.OrderBy(i => i.DueDate))
        {
            if (remaining <= 0) break;
            var applied   = Math.Min(inv.Amount - inv.AmountPaid, remaining);
            inv.AmountPaid += applied;
            remaining      -= applied;
            inv.Status    = inv.AmountPaid >= inv.Amount
                ? InvoiceStatus.Pagada
                : InvoiceStatus.ParcialmentePagada;
            inv.UpdatedAt = DateTime.UtcNow;
            await _invoiceRepo.UpdateAsync(inv);
            await _piRepo.AddAsync(new PaymentInvoice { PaymentId = payment.Id, InvoiceId = inv.Id });
            if (inv.InvoiceNumber is not null) invoiceNumbers.Add(inv.InvoiceNumber);
        }

        return invoiceNumbers;
    }

    private async Task PublishPaymentNotificationAsync(
        Client client, decimal amount, List<Invoice> invoices)
    {
        var cultura     = new CultureInfo("es-BO");
        var periodoDesc = string.Join(", ", invoices.Select(i =>
            i.Type == InvoiceType.Instalacion
                ? "Instalación"
                : new DateTime(i.Year, i.Month, 1).ToString("MMMM yyyy", cultura)));
        await _notif.PublishAsync(
            NotifType.CONFIRMACION_PAGO,
            client.Id,
            client.PhoneMain,
            new Dictionary<string, string>
            {
                ["nombre"]  = client.FullName,
                ["monto"]   = $"{amount:F2}",
                ["periodo"] = periodoDesc.Length > 0 ? periodoDesc : "Varios periodos",
            });
    }

    public async Task<r> ReembolsarCreditoAsync(
        Guid clientId, string justificacion, Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetByIdAsync(clientId);
        if (client is null) return Result.Failure("Cliente no encontrado.");
        if (client.CreditBalance <= 0) return Result.Failure("El cliente no tiene crédito a favor.");

        var prev = client.CreditBalance;
        client.CreditBalance = 0m;
        await _clientRepo.UpdateAsync(client);

        await _audit.LogAsync("Pagos", "CREDIT_REIMBURSED",
            $"Crédito reembolsado: {client.TbnCode} monto={prev:F2} razón={justificacion}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: JsonSerializer.Serialize(new { CreditBalance = prev }),
            newData:  JsonSerializer.Serialize(new { CreditBalance = 0m, Justificacion = justificacion }));

        return Result.Success();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-PAG-RECIBO · Recibo PDF
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<PaymentReceipt?> GetReceiptByPaymentAsync(Guid paymentId)
        => await _receiptRepo.GetAll().FirstOrDefaultAsync(r => r.PaymentId == paymentId);
}
