using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Domain.Entities.Payments;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>CORRECCIÓN Problema #7: interfaz para PaymentCreditService.</summary>
public interface IPaymentCreditService
{
    Task<Result<PaymentRegisteredDto>> RegisterPaymentWithCreditAsync(
        RegisterPaymentDto dto, Guid actorId, string actorName, string ip);
    Task<Result> ReembolsarCreditoAsync(
        Guid clientId, string justificacion, Guid actorId, string actorName, string ip);
    Task<CashCloseDto>            GetOrCreateActiveTurnoAsync(Guid userId, string userName);
    Task<Result<CashCloseDto>>    CerrarTurnoAsync(Guid userId, string userName, string ip);
    Task<List<CashCloseDto>>      GetCashClosesAsync(Guid? userId, DateTime? desde, DateTime? hasta);
    Task<PaymentReceipt?>         GetReceiptByPaymentAsync(Guid paymentId);
    Task<Result<CollectionReportByOperatorDto>> GetCollectionByOperatorAsync(
        DateTime from, DateTime to, Guid? operatorId);
}
