using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Invoices;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface IInvoiceQueryService
{
    Task<InvoiceMonthStatsDto>             GetMonthStatsAsync(int year, int month);
    Task<InvoiceRangeStatsDto>             GetRangeStatsAsync(DateTime dateFrom, DateTime dateTo);
    Task<PagedResult<InvoiceDetailDto>>    GetPagedAsync(InvoiceFilterDto filter);
    Task<IEnumerable<AnnualReportRowDto>>  GetAnnualReportAsync(AnnualReportFilterDto filter);
    Task<IEnumerable<DeudorDto>>           GetDeudoresAsync(string? search = null, DateTime? from = null, DateTime? to = null);
    Task<IEnumerable<InvoiceLegacyListItemDto>> GetAllForExportAsync(int year, int month);
}
