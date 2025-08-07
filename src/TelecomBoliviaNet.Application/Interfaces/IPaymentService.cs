using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface IPaymentService
{
    Task<PagedResult<PaymentListItemDto>>               GetAllAsync(PaymentFilterDto filter);
    Task<PaymentDetailDto?>                             GetByIdAsync(Guid id);
    Task<Result<string>>                                AttachReceiptImageAsync(
        Guid paymentId, Stream stream, string fileName, string contentType,
        Guid actorId, string actorName, string ip);
    Task<Result<VoidPaymentResultDto>>                  VoidPaymentAsync(
        Guid paymentId, string justification, Guid actorId, string actorName, string ip);
    Task<CollectionReportDto>                           GetCollectionReportAsync(DateTime from, DateTime to);
    Task<DuplicateCheckResultDto>                       CheckDuplicateAsync(Guid clientId, decimal amount, DateTime paidAt);
    Task<ReminderJobResultDto>                          SendOverdueRemindersAsync(Guid? actorId = null, string actorName = "Sistema");
    Task<Result<CollectionReportByOperatorDto>>         GetCollectionByOperatorAsync(
        DateTime from, DateTime to, Guid? operatorId);
}
