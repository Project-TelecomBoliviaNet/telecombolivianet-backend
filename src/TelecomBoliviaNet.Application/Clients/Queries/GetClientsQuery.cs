using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Clients.Helpers;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Clients.Queries;

public record GetClientsQuery(ClientFilterDto Filter) : IRequest<PagedResult<ClientListItemDto>>;

public class GetClientsHandler : IRequestHandler<GetClientsQuery, PagedResult<ClientListItemDto>>
{
    private readonly IGenericRepository<Client> _clientRepo;

    public GetClientsHandler(IGenericRepository<Client> clientRepo) => _clientRepo = clientRepo;

    public async Task<PagedResult<ClientListItemDto>> Handle(GetClientsQuery q, CancellationToken ct)
    {
        var filter = q.Filter;

        var includeBaja = string.IsNullOrWhiteSpace(filter.Status)
                          || filter.Status == "all"
                          || filter.Status == "DadoDeBaja";
        var baseQuery = includeBaja
            ? _clientRepo.GetAllReadOnly().IgnoreQueryFilters()
            : _clientRepo.GetAllReadOnly();

        var query = baseQuery
            .Include(c => c.Plan)
            .Include(c => c.Invoices)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = ClientProjectionHelpers.ApplyFullTextSearch(query, filter.Search);

        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "all")
            if (Enum.TryParse<ClientStatus>(filter.Status, out var status))
                query = query.Where(c => c.Status == status);

        if (filter.PlanId.HasValue)
            query = query.Where(c => c.PlanId == filter.PlanId.Value);

        query = filter.SortBy switch
        {
            "code" => query.OrderBy(c => c.TbnCode),
            "zone" => query.OrderBy(c => c.Zone),
            "name" => query.OrderBy(c => c.FullName),
            _      => query.OrderBy(c => c.TbnCode)
        };

        if (filter.DebtFilter == "paid")
            query = query.Where(c => !c.Invoices.Any(
                i => i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida));
        else if (filter.DebtFilter == "debt")
            query = query.Where(c => c.Invoices.Any(
                i => i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida));

        var total   = await query.CountAsync(ct);
        var clients = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var items = clients.Select(c => new ClientListItemDto(
            c.Id, c.TbnCode, c.FullName, c.Zone,
            c.PhoneMain, c.Plan?.Name ?? "—",
            c.HasTvCable, c.Status.ToString(),
            ClientProjectionHelpers.CalcDebt(c.Invoices),
            ClientProjectionHelpers.CalcPendingMonths(c.Invoices)));

        return new PagedResult<ClientListItemDto>(items, total, filter.PageNumber, filter.PageSize);
    }
}
