using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Tickets.Commands;

public record SubmitCsatCommand(
    Guid          TicketId,
    SubmitCsatDto Dto,
    string        ClientIp) : IRequest<Result<TicketDetailDto>>;

public class SubmitCsatHandler : IRequestHandler<SubmitCsatCommand, Result<TicketDetailDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly TicketHelper                      _helper;

    public SubmitCsatHandler(
        IGenericRepository<SupportTicket> ticketRepo,
        TicketHelper                      helper)
    {
        _ticketRepo = ticketRepo;
        _helper     = helper;
    }

    public async Task<Result<TicketDetailDto>> Handle(SubmitCsatCommand cmd, CancellationToken ct)
    {
        if (cmd.Dto.Score < 1 || cmd.Dto.Score > 5)
            return Result<TicketDetailDto>.Failure("El puntaje CSAT debe estar entre 1 y 5.");

        var ticket = await _helper.LoadAsync(cmd.TicketId);
        if (ticket is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        if (ticket.Status != TicketStatus.Resuelto && ticket.Status != TicketStatus.Cerrado)
            return Result<TicketDetailDto>.Failure("Solo se puede puntuar un ticket resuelto o cerrado.");

        ticket.CsatScore       = cmd.Dto.Score;
        ticket.CsatRespondedAt = DateTime.UtcNow;
        await _ticketRepo.UpdateAsync(ticket);

        var detail = await _helper.BuildDetailAsync(cmd.TicketId);
        if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        return Result<TicketDetailDto>.Success(detail);
    }
}
