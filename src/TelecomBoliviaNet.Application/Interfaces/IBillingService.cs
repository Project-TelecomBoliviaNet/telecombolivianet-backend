using TelecomBoliviaNet.Application.DTOs.Invoices;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface IBillingService
{
    Task<BillingPreviewDto>   GetBillingPreviewAsync(int year, int month);
    Task<BillingJobResultDto> GenerateMonthlyInvoicesAsync(
        int year, int month, Guid? actorId = null, string actorName = "Sistema");
    Task<int> MarkOverdueInvoicesAsync(Guid? actorId = null, string actorName = "Sistema");
    Task<Result> VoidInvoiceAsync(
        Guid invoiceId, string justification, Guid actorId, string actorName, string ip);
}
