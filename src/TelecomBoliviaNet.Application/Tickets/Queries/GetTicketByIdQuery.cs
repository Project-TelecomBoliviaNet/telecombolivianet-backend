using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Tickets.Helpers;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetTicketByIdQuery(Guid TicketId) : IRequest<TicketDetailDto?>;

public class GetTicketByIdHandler : IRequestHandler<GetTicketByIdQuery, TicketDetailDto?>
{
    private readonly TicketHelper _helper;

    public GetTicketByIdHandler(TicketHelper helper) => _helper = helper;

    public Task<TicketDetailDto?> Handle(GetTicketByIdQuery q, CancellationToken ct)
        => _helper.BuildDetailAsync(q.TicketId);
}
