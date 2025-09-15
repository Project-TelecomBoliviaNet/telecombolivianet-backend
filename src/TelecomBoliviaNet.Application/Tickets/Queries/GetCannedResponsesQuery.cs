using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetCannedResponsesQuery(bool IncludeInactive = false)
    : IRequest<IEnumerable<CannedResponseDto>>;

public class GetCannedResponsesHandler
    : IRequestHandler<GetCannedResponsesQuery, IEnumerable<CannedResponseDto>>
{
    private readonly IGenericRepository<TicketCannedResponse> _repo;

    public GetCannedResponsesHandler(IGenericRepository<TicketCannedResponse> repo)
        => _repo = repo;

    public async Task<IEnumerable<CannedResponseDto>> Handle(
        GetCannedResponsesQuery query, CancellationToken ct)
    {
        var q = _repo.GetAllReadOnly().AsQueryable();
        if (!query.IncludeInactive) q = q.Where(r => r.IsActive);

        var items = await q.OrderBy(r => r.Category).ThenBy(r => r.Title).ToListAsync(ct);
        return items.Select(r => new CannedResponseDto(
            r.Id, r.Title, r.Body, r.Category, r.IsActive, r.CreatedAt));
    }
}
