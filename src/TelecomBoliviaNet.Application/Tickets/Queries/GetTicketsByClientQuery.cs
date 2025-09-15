using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetTicketsByClientQuery(Guid ClientId) : IRequest<IEnumerable<TicketListItemDto>>;

public class GetTicketsByClientHandler : IRequestHandler<GetTicketsByClientQuery, IEnumerable<TicketListItemDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;

    public GetTicketsByClientHandler(IGenericRepository<SupportTicket> ticketRepo) => _ticketRepo = ticketRepo;

    public async Task<IEnumerable<TicketListItemDto>> Handle(GetTicketsByClientQuery q, CancellationToken ct)
    {
        var items = await _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.WorkLogs)
            .Where(t => t.ClientId == q.ClientId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return items.Select(TicketHelper.MapToListItem);
    }
}
