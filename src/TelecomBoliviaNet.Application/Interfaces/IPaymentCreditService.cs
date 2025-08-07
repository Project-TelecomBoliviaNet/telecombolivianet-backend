using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Domain.Entities.Payments;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface IPaymentCreditService
{
    Task<Result<PaymentRegisteredDto>> RegisterPaymentWithCreditAsync(
        RegisterPaymentDto dto, Guid actorId, string actorName, string ip);
    Task<Result> ReembolsarCreditoAsync(
        Guid clientId, string justificacion, Guid actorId, string actorName, string ip);
    Task<PaymentReceipt?> GetReceiptByPaymentAsync(Guid paymentId);
}
