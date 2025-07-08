using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Clients.Commands;

public record RegisterPaymentCommand(
    RegisterPaymentDto Dto,
    Guid               ActorId,
    string             ActorName,
    string             ClientIp) : IRequest<Result>;

public class RegisterPaymentHandler : IRequestHandler<RegisterPaymentCommand, Result>
{
    private readonly IGenericRepository<Client>         _clientRepo;
    private readonly IGenericRepository<Invoice>        _invoiceRepo;
    private readonly IGenericRepository<Payment>        _paymentRepo;
    private readonly IGenericRepository<PaymentInvoice> _piRepo;
    private readonly AuditService                       _audit;
    private readonly INotifPublisher                    _notif;

    public RegisterPaymentHandler(
        IGenericRepository<Client>         clientRepo,
        IGenericRepository<Invoice>        invoiceRepo,
        IGenericRepository<Payment>        paymentRepo,
        IGenericRepository<PaymentInvoice> piRepo,
        AuditService                       audit,
        INotifPublisher                    notif)
    {
        _clientRepo  = clientRepo;
        _invoiceRepo = invoiceRepo;
        _paymentRepo = paymentRepo;
        _piRepo      = piRepo;
        _audit       = audit;
        _notif       = notif;
    }

    public async Task<Result> Handle(RegisterPaymentCommand cmd, CancellationToken ct)
    {
        var dto = cmd.Dto;

        var client = await _clientRepo.GetAll()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == dto.ClientId, ct);
        if (client is null) return Result.Failure("Cliente no encontrado.");

        if (!Enum.TryParse<PaymentMethod>(dto.Method, out var method))
            return Result.Failure("Método de pago inválido.");

        var invoices = await _invoiceRepo.GetAll()
            .Where(i => dto.InvoiceIds.Contains(i.Id) && i.ClientId == dto.ClientId)
            .ToListAsync(ct);

        if (invoices.Count != dto.InvoiceIds.Count)
            return Result.Failure("Una o más facturas no pertenecen a este cliente.");

        if (invoices.Any(i => i.Status == InvoiceStatus.Pagada))
            return Result.Failure("Una o más facturas seleccionadas ya están pagadas.");

        var payment = new Payment
        {
            ClientId              = dto.ClientId,
            Amount                = dto.Amount,
            Method                = method,
            Bank                  = dto.Bank,
            BankReference         = dto.BankReference,
            PhysicalReceiptNumber = dto.PhysicalReceiptNumber,
            PaidAt                = DateTime.SpecifyKind(dto.PaidAt, DateTimeKind.Utc),
            RegisteredByUserId    = cmd.ActorId,
            RegisteredAt          = DateTime.UtcNow,
            FromWhatsApp          = false
        };
        await _paymentRepo.AddAsync(payment);

        foreach (var inv in invoices)
        {
            inv.Status = InvoiceStatus.Pagada;
            await _invoiceRepo.UpdateAsync(inv);
            await _piRepo.AddAsync(new PaymentInvoice { PaymentId = payment.Id, InvoiceId = inv.Id });
        }

        await _paymentRepo.SaveChangesAsync();

        var cultura     = new CultureInfo("es-BO");
        var coveredDesc = string.Join(", ", invoices.Select(i =>
            i.Type == InvoiceType.Instalacion
                ? "Instalación"
                : new DateTime(i.Year, i.Month, 1).ToString("MMMM yyyy", cultura)));

        var action = dto.ConfirmedDuplicate == true ? "PAYMENT_REGISTERED_DUPLICATE" : "PAYMENT_REGISTERED";
        await _audit.LogAsync("Pagos", action,
            $"Pago registrado: {client.TbnCode} — Bs. {dto.Amount:N2} — {coveredDesc}"
            + (dto.ConfirmedDuplicate == true ? " (DUPLICADO CONFIRMADO por el usuario)" : ""),
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp);

        await _notif.PublishAsync(
            NotifType.CONFIRMACION_PAGO,
            client.Id,
            client.PhoneMain,
            new Dictionary<string, string>
            {
                ["nombre"]  = client.FullName,
                ["monto"]   = $"{dto.Amount:F2}",
                ["periodo"] = coveredDesc,
            },
            referenciaId: payment.Id);

        return Result.Success();
    }
}
