using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Tickets.Commands;

public record ScheduleVisitCommand(
    Guid             TicketId,
    ScheduleVisitDto Dto,
    Guid             ActorId,
    string           ActorName,
    string           ClientIp,
    string?          UserAgent = null) : IRequest<Result<TicketVisitDto>>;

public class ScheduleVisitHandler : IRequestHandler<ScheduleVisitCommand, Result<TicketVisitDto>>
{
    private readonly IGenericRepository<SupportTicket>   _ticketRepo;
    private readonly IGenericRepository<TicketVisit>     _visitRepo;
    private readonly IGenericRepository<TicketComment>   _commentRepo;
    private readonly IGenericRepository<TicketNotification> _notifRepo;
    private readonly IGenericRepository<UserSystem>      _userRepo;
    private readonly INotifPublisher                     _notifPublisher;
    private readonly AuditService                        _audit;

    public ScheduleVisitHandler(
        IGenericRepository<SupportTicket>      ticketRepo,
        IGenericRepository<TicketVisit>        visitRepo,
        IGenericRepository<TicketComment>      commentRepo,
        IGenericRepository<TicketNotification> notifRepo,
        IGenericRepository<UserSystem>         userRepo,
        INotifPublisher                        notifPublisher,
        AuditService                           audit)
    {
        _ticketRepo     = ticketRepo;
        _visitRepo      = visitRepo;
        _commentRepo    = commentRepo;
        _notifRepo      = notifRepo;
        _userRepo       = userRepo;
        _notifPublisher = notifPublisher;
        _audit          = audit;
    }

    public async Task<Result<TicketVisitDto>> Handle(ScheduleVisitCommand cmd, CancellationToken ct)
    {
        var ticket = await _ticketRepo.GetAll()
            .Include(t => t.Client)
            .FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null) return Result<TicketVisitDto>.Failure("Ticket no encontrado.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketVisitDto>.Failure("No se puede programar visita en ticket cerrado.");

        UserSystem? tech = null;
        if (cmd.Dto.TechnicianId.HasValue)
        {
            tech = await _userRepo.GetByIdAsync(cmd.Dto.TechnicianId.Value);
            if (tech is null) return Result<TicketVisitDto>.Failure("Técnico no encontrado.");
        }

        var visit = new TicketVisit
        {
            TicketId        = cmd.TicketId,
            ScheduledAt     = cmd.Dto.ScheduledAt,
            TechnicianId    = cmd.Dto.TechnicianId,
            Observations    = cmd.Dto.Observations?.Trim(),
            Status          = VisitStatus.Programada,
            CreatedByUserId = cmd.ActorId,
            CreatedAt       = DateTime.UtcNow,
        };
        await _visitRepo.AddAsync(visit);

        // Cambiar estado del ticket a EnVisita si está abierto/en proceso
        if (ticket.Status is TicketStatus.Abierto or TicketStatus.EnProceso)
            ticket.Status = TicketStatus.EnVisita;

        if (!string.IsNullOrWhiteSpace(cmd.Dto.Observations))
            await _commentRepo.AddAsync(new TicketComment
            {
                TicketId  = cmd.TicketId, AuthorId = cmd.ActorId,
                Type      = CommentType.NotaInterna,
                Body      = $"[Visita programada {cmd.Dto.ScheduledAt:dd/MM/yyyy HH:mm}] {cmd.Dto.Observations}",
                CreatedAt = DateTime.UtcNow,
            });

        await _visitRepo.SaveChangesAsync();

        // M08: Notificar al cliente sobre la visita programada (si tiene teléfono)
        var clientPhone = ticket.Client?.PhoneMain;
        if (!string.IsNullOrWhiteSpace(clientPhone) && ticket.ClientId.HasValue)
        {
            var techName = tech?.FullName ?? "Técnico asignado";
            var fechaLocal = TimeZoneInfo.ConvertTimeFromUtc(
                cmd.Dto.ScheduledAt.Kind == DateTimeKind.Utc
                    ? cmd.Dto.ScheduledAt
                    : DateTime.SpecifyKind(cmd.Dto.ScheduledAt, DateTimeKind.Utc),
                TelecomBoliviaNet.Application.Services.Notifications.NotifShared.BoliviaZone
            ).ToString("dd/MM/yyyy HH:mm");

            await _notifPublisher.PublishAsync(
                NotifType.VISITA_PROGRAMADA,
                ticket.ClientId.Value,
                clientPhone,
                new Dictionary<string, string>
                {
                    ["cliente"]   = ticket.Client?.FullName ?? "Cliente",
                    ["num_ticket"] = ticket.TicketNumber ?? ticket.Id.ToString()[..8].ToUpper(),
                    ["fecha"]     = fechaLocal,
                    ["tecnico"]   = techName,
                },
                referenciaId: visit.Id);

            await _notifRepo.AddAsync(new TicketNotification
            {
                TicketId  = ticket.Id, Type = NotificationType.VisitaProgramada,
                Status    = NotificationStatus.Enviado, Recipient = clientPhone,
                Message   = $"VISITA_PROGRAMADA: {ticket.TicketNumber} — {fechaLocal} con {techName}",
                SentAt    = DateTime.UtcNow,
            });
            await _notifRepo.SaveChangesAsync();
        }

        await _audit.LogAsync("Tickets", "VISIT_SCHEDULED",
            $"Visita {cmd.Dto.ScheduledAt:dd/MM} en ticket {cmd.TicketId}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp, cmd.UserAgent);

        return Result<TicketVisitDto>.Success(new TicketVisitDto(
            visit.Id, visit.ScheduledAt, tech?.FullName, visit.TechnicianId,
            visit.Observations, visit.CreatedAt, visit.Status.ToString()));
    }
}
