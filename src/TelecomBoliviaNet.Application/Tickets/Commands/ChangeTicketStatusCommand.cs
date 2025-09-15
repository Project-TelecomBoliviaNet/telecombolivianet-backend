using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Tickets.Commands;

public record ChangeTicketStatusCommand(
    Guid                  TicketId,
    ChangeTicketStatusDto Dto,
    Guid                  ActorId,
    string                ActorName,
    string                ClientIp,
    string?               UserAgent = null) : IRequest<Result<TicketDetailDto>>;

public class ChangeTicketStatusHandler : IRequestHandler<ChangeTicketStatusCommand, Result<TicketDetailDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly IGenericRepository<TicketComment> _commentRepo;
    private readonly IGenericRepository<UserSystem>    _userRepo;
    private readonly TicketHelper                      _helper;
    private readonly AuditService                      _audit;
    private readonly INotifPublisher                   _notifPublisher;

    public ChangeTicketStatusHandler(
        IGenericRepository<SupportTicket> ticketRepo,
        IGenericRepository<TicketComment> commentRepo,
        IGenericRepository<UserSystem>    userRepo,
        TicketHelper                      helper,
        AuditService                      audit,
        INotifPublisher                   notifPublisher)
    {
        _ticketRepo     = ticketRepo;
        _commentRepo    = commentRepo;
        _userRepo       = userRepo;
        _helper         = helper;
        _audit          = audit;
        _notifPublisher = notifPublisher;
    }

    public async Task<Result<TicketDetailDto>> Handle(ChangeTicketStatusCommand cmd, CancellationToken ct)
    {
        var ticket = await _helper.LoadAsync(cmd.TicketId);
        if (ticket is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        if (!Enum.TryParse<TicketStatus>(cmd.Dto.Status, out var newStatus))
            return Result<TicketDetailDto>.Failure("Estado inválido.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketDetailDto>.Failure("Un ticket cerrado no puede modificarse.");
        if (newStatus == TicketStatus.Resuelto && string.IsNullOrWhiteSpace(cmd.Dto.ResolutionMessage))
            return Result<TicketDetailDto>.Failure("Se requiere mensaje de resolución al marcar como Resuelto.");

        var prevStatus = ticket.Status;
        ticket.Status  = newStatus;

        if (newStatus == TicketStatus.EnProceso && ticket.FirstRespondedAt is null)
            ticket.FirstRespondedAt = DateTime.UtcNow;

        if (newStatus == TicketStatus.Resuelto && ticket.ResolvedAt is null)
        {
            ticket.ResolvedAt        = DateTime.UtcNow;
            ticket.ResolutionMessage = cmd.Dto.ResolutionMessage!.Trim();
            ticket.SlaCompliant      = !ticket.DueDate.HasValue || ticket.ResolvedAt <= ticket.DueDate;

            // FIX-B: notificar al cliente vía WhatsApp cuando su ticket es resuelto.
            // Solo si el ticket tiene cliente con teléfono; fallo silencioso no bloquea el flujo.
            if (ticket.Client?.PhoneMain is not null && ticket.ClientId.HasValue)
            {
                var tecnicoNombre = ticket.AssignedTo?.FullName ?? "el equipo técnico";
                await _notifPublisher.PublishAsync(
                    NotifType.TICKET_RESUELTO,
                    ticket.ClientId.Value,
                    ticket.Client.PhoneMain,
                    new Dictionary<string, string>
                    {
                        ["nombre"]     = ticket.Client.FullName,
                        ["num_ticket"] = ticket.TicketNumber ?? ticket.Id.ToString()[..8].ToUpper(),
                        ["tecnico"]    = tecnicoNombre,
                        ["empresa"]    = "TelecomBoliviaNet",
                    },
                    referenciaId: ticket.Id);
            }
        }

        if (prevStatus == TicketStatus.Resuelto && newStatus == TicketStatus.EnProceso)
        {
            ticket.ResolvedAt        = null;
            ticket.ResolutionMessage = null;
            ticket.SlaCompliant      = null;
            ticket.DueDate           = await _helper.GetDueDateFromSlaAsync(ticket.Priority);

            if (ticket.AssignedToUserId.HasValue && ticket.Client is not null)
            {
                var tech = ticket.AssignedTo ?? await _userRepo.GetByIdAsync(ticket.AssignedToUserId.Value);
                if (tech is not null)
                    await _helper.SendNotificationAsync(ticket, tech, ticket.Client, reopen: true);
            }
        }

        if (newStatus == TicketStatus.PendienteCliente)
            ticket.SlaPausedAt = DateTime.UtcNow;

        if (prevStatus == TicketStatus.PendienteCliente && ticket.SlaPausedAt.HasValue)
        {
            var pausedMinutes = (int)(DateTime.UtcNow - ticket.SlaPausedAt.Value).TotalMinutes;
            ticket.SlaTotalPausedMinutes += pausedMinutes;
            ticket.SlaPausedAt = null;
            if (ticket.DueDate.HasValue)
                ticket.DueDate = ticket.DueDate.Value.AddMinutes(pausedMinutes);
        }

        await _ticketRepo.UpdateAsync(ticket);

        var pauseInfo = ticket.SlaTotalPausedMinutes > 0
            ? $" ({ticket.SlaTotalPausedMinutes}min acumulados en espera)" : string.Empty;
        var historyText = (newStatus, prevStatus) switch
        {
            (TicketStatus.EnProceso,        TicketStatus.Abierto)          => "▶ Ticket en proceso de atención.",
            (TicketStatus.EnProceso,        TicketStatus.Resuelto)         => "🔄 Ticket reabierto — problema persiste. SLA reiniciado.",
            (TicketStatus.EnProceso,        TicketStatus.PendienteCliente) => $"▶ Cliente respondió. SLA reanudado{pauseInfo}.",
            (TicketStatus.PendienteCliente, _)                             => "⏸ Ticket en espera de respuesta del cliente. SLA pausado.",
            (TicketStatus.Resuelto,         _)                             => $"✅ Ticket resuelto. {cmd.Dto.ResolutionMessage}",
            (TicketStatus.Cerrado,          _)                             => "🔒 Ticket cerrado.",
            _                                                              => $"Estado cambiado a {newStatus}.",
        };
        await _commentRepo.AddAsync(new TicketComment
        {
            TicketId = ticket.Id, AuthorId = cmd.ActorId,
            Type = CommentType.NotaInterna, Body = historyText, CreatedAt = DateTime.UtcNow,
        });
        await _commentRepo.SaveChangesAsync();

        await _audit.LogAsync("Tickets", "STATUS_CHANGED",
            $"Ticket '{ticket.Subject}': {prevStatus} → {newStatus}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp, cmd.UserAgent, entityId: ticket.Id);
        if (newStatus == TicketStatus.PendienteCliente)
            await _audit.LogAsync("SLA", "SLA_PAUSED",
                $"SLA pausado — ticket '{ticket.Subject}' en espera de cliente.",
                cmd.ActorId, cmd.ActorName, cmd.ClientIp, cmd.UserAgent, entityId: ticket.Id);
        else if (prevStatus == TicketStatus.PendienteCliente)
            await _audit.LogAsync("SLA", "SLA_RESUMED",
                $"SLA reanudado — ticket '{ticket.Subject}': +{ticket.SlaTotalPausedMinutes}min acumulados.",
                cmd.ActorId, cmd.ActorName, cmd.ClientIp, cmd.UserAgent, entityId: ticket.Id);

        var detail = await _helper.BuildDetailAsync(cmd.TicketId);
        if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        return Result<TicketDetailDto>.Success(detail);
    }
}
