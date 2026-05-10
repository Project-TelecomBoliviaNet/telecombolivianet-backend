using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface IWhatsAppReceiptService
{
    Task<Result<Guid>>                       ReceiveFromBotAsync(SubmitWhatsappReceiptDto dto);
    Task<PagedResult<WhatsAppReceiptDto>>    GetQueueAsync(int page = 1, int pageSize = 25, string status = "Pendiente");
    Task<int>                                CountPendingAsync();
    Task<Result<Guid>>                       ApproveAsync(
        Guid receiptId, ApproveReceiptDto dto, Guid actorId, string actorName, string ip);
    Task<Result<bool>>                       RejectAsync(
        Guid receiptId, RejectReceiptDto dto, Guid actorId, string actorName, string ip);
    Task<Result<bool>>                       MarkNotRelatedAsync(
        Guid receiptId, Guid actorId, string actorName, string ip);
    Task<Result<bool>>                       AssignClientAsync(
        Guid receiptId, Guid clientId, Guid actorId, string actorName, string ip);
}
