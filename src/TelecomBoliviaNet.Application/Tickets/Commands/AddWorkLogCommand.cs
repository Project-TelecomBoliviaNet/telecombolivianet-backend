using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Tickets.Commands;

public record AddWorkLogCommand(
    Guid          TicketId,
    AddWorkLogDto Dto,
    Guid          ActorId,
    string        ActorName,
    string        ClientIp,
    string?       UserAgent = null) : IRequest<Result<TicketWorkLogDto>>;

public class AddWorkLogHandler : IRequestHandler<AddWorkLogCommand, Result<TicketWorkLogDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly IGenericRepository<TicketWorkLog> _workLogRepo;
    private readonly IGenericRepository<UserSystem>    _userRepo;
    private readonly AuditService                      _audit;

    public AddWorkLogHandler(
        IGenericRepository<SupportTicket> ticketRepo,
        IGenericRepository<TicketWorkLog> workLogRepo,
        IGenericRepository<UserSystem>    userRepo,
        AuditService                      audit)
    {
        _ticketRepo  = ticketRepo;
        _workLogRepo = workLogRepo;
        _userRepo    = userRepo;
        _audit       = audit;
    }

    public async Task<Result<TicketWorkLogDto>> Handle(AddWorkLogCommand cmd, CancellationToken ct)
    {
        var ticket = await _ticketRepo.GetByIdAsync(cmd.TicketId);
        if (ticket is null) return Result<TicketWorkLogDto>.Failure("Ticket no encontrado.");

        var totalMinutes = cmd.Dto.Hours * 60 + cmd.Dto.Minutes;
        if (totalMinutes <= 0) return Result<TicketWorkLogDto>.Failure("El tiempo debe ser mayor a 0.");

        var user = await _userRepo.GetByIdAsync(cmd.ActorId);
        var log  = new TicketWorkLog
        {
            TicketId = cmd.TicketId, UserId = cmd.ActorId,
            Minutes  = totalMinutes, Notes = cmd.Dto.Notes?.Trim(), LoggedAt = DateTime.UtcNow,
        };
        await _workLogRepo.AddAsync(log);

        await _audit.LogAsync("Tickets", "WORKLOG_ADDED",
            $"{totalMinutes}min en ticket {cmd.TicketId}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp, cmd.UserAgent);

        return Result<TicketWorkLogDto>.Success(new TicketWorkLogDto(
            log.Id, user?.FullName ?? cmd.ActorName, cmd.ActorId, totalMinutes, log.Notes, log.LoggedAt));
    }
}
