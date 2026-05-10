using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Tickets.Commands;

public record AssignTicketCommand(
    Guid            TicketId,
    AssignTicketDto Dto,
    Guid            ActorId,
    string          ActorName,
    string          ClientIp,
    string?         UserAgent = null) : IRequest<Result<TicketDetailDto>>;

public class AssignTicketHandler : IRequestHandler<AssignTicketCommand, Result<TicketDetailDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly IGenericRepository<UserSystem>    _userRepo;
    private readonly IGenericRepository<TicketComment> _commentRepo;
    private readonly TicketHelper                      _helper;
    private readonly AuditService                      _audit;

    public AssignTicketHandler(
        IGenericRepository<SupportTicket> ticketRepo,
        IGenericRepository<UserSystem>    userRepo,
        IGenericRepository<TicketComment> commentRepo,
        TicketHelper                      helper,
        AuditService                      audit)
    {
        _ticketRepo  = ticketRepo;
        _userRepo    = userRepo;
        _commentRepo = commentRepo;
        _helper      = helper;
        _audit       = audit;
    }

    public async Task<Result<TicketDetailDto>> Handle(AssignTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await _helper.LoadAsync(cmd.TicketId);
        if (ticket is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketDetailDto>.Failure("No se puede asignar en ticket cerrado.");

        var tech = await _userRepo.GetByIdAsync(cmd.Dto.TechnicianId);
        if (tech is null) return Result<TicketDetailDto>.Failure("Técnico no encontrado.");
        if (tech.Role != UserRole.Tecnico && tech.Role != UserRole.Admin)
            return Result<TicketDetailDto>.Failure("Solo se puede asignar a Técnico o Admin.");

        ticket.AssignedToUserId = cmd.Dto.TechnicianId;
        ticket.AssignedTo       = tech;
        if (ticket.Status == TicketStatus.Abierto)
            ticket.Status = TicketStatus.EnProceso;

        await _ticketRepo.UpdateAsync(ticket);

        await _commentRepo.AddAsync(new TicketComment
        {
            TicketId = ticket.Id, AuthorId = cmd.ActorId,
            Type     = CommentType.NotaInterna,
            Body     = $"👤 Ticket asignado a {tech.FullName}.", CreatedAt = DateTime.UtcNow,
        });
        await _commentRepo.SaveChangesAsync();

        await _audit.LogAsync("Tickets", "TICKET_ASSIGNED",
            $"Ticket '{ticket.Subject}' → {tech.FullName}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp, cmd.UserAgent, entityId: ticket.Id);

        string? waWarning = null;
        if (ticket.Client is not null)
            waWarning = await _helper.SendNotificationAsync(ticket, tech, ticket.Client);

        var detail = await _helper.BuildDetailAsync(cmd.TicketId);
        if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        return Result<TicketDetailDto>.Success(detail with { WhatsAppWarning = waWarning });
    }
}
