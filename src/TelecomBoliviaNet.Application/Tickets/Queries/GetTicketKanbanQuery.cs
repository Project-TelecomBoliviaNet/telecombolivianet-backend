using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Domain.Entities.Tickets;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetTicketKanbanQuery(TicketFilterDto Filter)
    : IRequest<Dictionary<string, IEnumerable<TicketListItemDto>>>;

public class GetTicketKanbanHandler
    : IRequestHandler<GetTicketKanbanQuery, Dictionary<string, IEnumerable<TicketListItemDto>>>
{
    private readonly IMediator _mediator;

    public GetTicketKanbanHandler(IMediator mediator) => _mediator = mediator;

    public async Task<Dictionary<string, IEnumerable<TicketListItemDto>>> Handle(
        GetTicketKanbanQuery q, CancellationToken ct)
    {
        var f = q.Filter;
        var allFilter = new TicketFilterDto
        {
            Search       = f.Search,       Status       = f.Status,
            Priority     = f.Priority,     Type         = f.Type,
            AssignedToId = f.AssignedToId, OverdueSla   = f.OverdueSla,
            DateFrom     = f.DateFrom,     DateTo       = f.DateTo,
            SlaCompliant = f.SlaCompliant,
            PageNumber   = 1,              PageSize     = int.MaxValue,
        };
        var paged = await _mediator.Send(new GetTicketsQuery(allFilter), ct);
        var groups = paged.Items.GroupBy(t => t.Status)
            .ToDictionary(g => g.Key, g => g.AsEnumerable());
        foreach (var s in Enum.GetNames<TicketStatus>())
            groups.TryAdd(s, Enumerable.Empty<TicketListItemDto>());
        return groups;
    }
}
