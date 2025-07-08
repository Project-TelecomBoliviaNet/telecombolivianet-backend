using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Clients.Helpers;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Clients.Queries;

public record SearchClientsQuery(ClientSearchDto Dto) : IRequest<ClientSearchResultDto>;

public class SearchClientsHandler : IRequestHandler<SearchClientsQuery, ClientSearchResultDto>
{
    private readonly IGenericRepository<Client> _clientRepo;

    public SearchClientsHandler(IGenericRepository<Client> clientRepo) => _clientRepo = clientRepo;

    public async Task<ClientSearchResultDto> Handle(SearchClientsQuery q, CancellationToken ct)
    {
        var dto   = q.Dto;
        var query = _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Include(c => c.Invoices)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(dto.Query))
            query = ClientProjectionHelpers.ApplyFullTextSearch(query, dto.Query);

        if (!string.IsNullOrWhiteSpace(dto.Zone))
            query = query.Where(c => c.Zone == dto.Zone);

        if (!string.IsNullOrWhiteSpace(dto.Status) && Enum.TryParse<ClientStatus>(dto.Status, out var st))
            query = query.Where(c => c.Status == st);

        if (Guid.TryParse(dto.PlanId, out var planId))
            query = query.Where(c => c.PlanId == planId);

        if (dto.HasEmail == true)  query = query.Where(c => c.Email != null && c.Email != "");
        if (dto.HasEmail == false) query = query.Where(c => c.Email == null || c.Email == "");

        if (dto.HasDebt == true)
            query = query.Where(c => c.Invoices.Any(
                i => i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida));
        if (dto.HasDebt == false)
            query = query.Where(c => !c.Invoices.Any(
                i => i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida));

        var total   = await query.CountAsync(ct);
        var clients = await query
            .OrderBy(c => c.TbnCode)
            .Skip((dto.Page - 1) * dto.PageSize)
            .Take(dto.PageSize)
            .ToListAsync(ct);

        var items = clients.Select(c => new ClientListItemDto(
            c.Id, c.TbnCode, c.FullName, c.Zone,
            c.PhoneMain, c.Plan?.Name ?? "—",
            c.HasTvCable, c.Status.ToString(),
            ClientProjectionHelpers.CalcDebt(c.Invoices),
            ClientProjectionHelpers.CalcPendingMonths(c.Invoices))).ToList();

        return new ClientSearchResultDto(items, total, dto.Page, dto.PageSize, dto.Query);
    }
}
