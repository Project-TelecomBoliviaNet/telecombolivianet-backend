using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Services;

namespace TelecomBoliviaNet.Application.Tickets.Helpers;

/// <summary>
/// Shared helpers injected into ticket handlers.
/// Contains logic that multiple handlers need: detail projection, SLA calculation, notifications.
/// </summary>
public class TicketHelper
{
    private readonly IGenericRepository<SupportTicket>      _ticketRepo;
    private readonly IGenericRepository<TicketComment>      _commentRepo;
    private readonly IGenericRepository<TicketWorkLog>      _workLogRepo;
    private readonly IGenericRepository<TicketVisit>        _visitRepo;
    private readonly IGenericRepository<SlaPlan>            _slaPlanRepo;
    private readonly IGenericRepository<TicketNotification> _notifRepo;
    private readonly IGenericRepository<BusinessSchedule>   _scheduleRepo;
    private readonly INotifPublisher                        _notifPublisher;

    public TicketHelper(
        IGenericRepository<SupportTicket>      ticketRepo,
        IGenericRepository<TicketComment>      commentRepo,
        IGenericRepository<TicketWorkLog>      workLogRepo,
        IGenericRepository<TicketVisit>        visitRepo,
        IGenericRepository<SlaPlan>            slaPlanRepo,
        IGenericRepository<TicketNotification> notifRepo,
        IGenericRepository<BusinessSchedule>   scheduleRepo,
        INotifPublisher                        notifPublisher)
    {
        _ticketRepo     = ticketRepo;
        _commentRepo    = commentRepo;
        _workLogRepo    = workLogRepo;
        _visitRepo      = visitRepo;
        _slaPlanRepo    = slaPlanRepo;
        _notifRepo      = notifRepo;
        _scheduleRepo   = scheduleRepo;
        _notifPublisher = notifPublisher;
    }

    public async Task<SupportTicket?> LoadAsync(Guid id) =>
        await _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo).Include(t => t.CreatedBy)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<TicketDetailDto?> BuildDetailAsync(Guid ticketId)
    {
        var t = await _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo).Include(t => t.CreatedBy)
            .FirstOrDefaultAsync(x => x.Id == ticketId);
        if (t is null) return null;

        var comments = await _commentRepo.GetAll().Include(c => c.Author)
            .Where(c => c.TicketId == ticketId).OrderBy(c => c.CreatedAt).ToListAsync();
        var workLogs = await _workLogRepo.GetAll().Include(w => w.User)
            .Where(w => w.TicketId == ticketId).OrderByDescending(w => w.LoggedAt).ToListAsync();
        var visits = await _visitRepo.GetAll().Include(v => v.Technician)
            .Where(v => v.TicketId == ticketId).OrderBy(v => v.ScheduledAt).ToListAsync();

        return new TicketDetailDto(
            t.Id, t.Client?.FullName ?? t.ProspectName ?? "", t.Client?.TbnCode ?? "", t.ClientId,
            t.Subject, t.Type.ToString(), t.Priority.ToString(), t.Status.ToString(), t.Origin.ToString(),
            t.Description, t.SupportGroup,
            t.AssignedTo?.FullName, t.AssignedToUserId,
            t.CreatedBy?.FullName ?? "", t.CreatedByUserId,
            t.CreatedAt, t.DueDate, t.ResolvedAt, t.ClosedAt, t.FirstRespondedAt,
            t.SlaCompliant, t.ResolutionMessage, t.RootCause,
            t.CsatScore, t.CsatRespondedAt,
            workLogs.Sum(w => w.Minutes),
            comments.Select(c => new TicketCommentDto(c.Id, c.Type.ToString(), c.Body,
                c.Author?.FullName ?? "Sistema", c.AuthorId, c.CreatedAt)),
            workLogs.Select(w => new TicketWorkLogDto(w.Id, w.User?.FullName ?? "?", w.UserId,
                w.Minutes, w.Notes, w.LoggedAt)),
            visits.Select(v => new TicketVisitDto(v.Id, v.ScheduledAt, v.Technician?.FullName,
                v.TechnicianId, v.Observations, v.CreatedAt, v.Status.ToString())),
            SlaTotalPausedMinutes: t.SlaTotalPausedMinutes,
            SlaPausedAt: t.SlaPausedAt,
            TicketNumber: t.TicketNumber,
            ImageUrl: t.ImageUrl,
            ClientGpsLatitude:  t.Client?.GpsLatitude,
            ClientGpsLongitude: t.Client?.GpsLongitude);
    }

    public async Task<DateTime?> GetDueDateFromSlaAsync(
        TicketPriority priority, DateTime? startTime = null)
    {
        var plan = await _slaPlanRepo.GetAll()
            .FirstOrDefaultAsync(p => p.Priority == priority.ToString() && p.IsActive);
        if (plan is null) return null;
        var from = startTime ?? DateTime.UtcNow;

        if (plan.Schedule != SlaSchedule.Laboral)
            return from.AddMinutes(plan.ResolutionMinutes);

        var schedule = plan.BusinessScheduleId.HasValue
            ? await _scheduleRepo.GetAll().Include(s => s.Holidays)
                  .FirstOrDefaultAsync(s => s.Id == plan.BusinessScheduleId.Value && s.IsActive)
            : await _scheduleRepo.GetAll().Include(s => s.Holidays)
                  .FirstOrDefaultAsync(s => s.IsDefault && s.IsActive);

        if (schedule is null || schedule.Is24x7)
            return from.AddMinutes(plan.ResolutionMinutes);

        var holidays = schedule.Holidays.Select(h => h.Date).ToList();
        return SlaCalculator.AddBusinessMinutes(from, plan.ResolutionMinutes, schedule, holidays);
    }

    public async Task<string?> SendNotificationAsync(
        SupportTicket ticket, UserSystem technician, Client? client, bool reopen = false)
    {
        if (string.IsNullOrWhiteSpace(technician.Phone))
        {
            await _notifRepo.AddAsync(new TicketNotification
            {
                TicketId    = ticket.Id, Type = NotificationType.AsignacionTecnico,
                Status      = NotificationStatus.Fallido, Recipient = string.Empty,
                Message     = string.Empty,
                ErrorDetail = $"El técnico {technician.FullName} no tiene número WhatsApp registrado.",
                SentAt      = DateTime.UtcNow,
            });
            return $"El técnico {technician.FullName} no tiene número WhatsApp registrado. Notificación no enviada.";
        }

        var prefix  = reopen ? "REAPERTURA" : "ASIGNACIÓN";
        var dueText = ticket.DueDate?.ToString("dd/MM/yyyy HH:mm") ?? "Sin fecha límite";

        await _notifPublisher.PublishAsync(
            NotifType.TICKET_ASIGNADO,
            client?.Id ?? Guid.Empty,
            technician.Phone,
            new Dictionary<string, string>
            {
                ["prefijo"]   = prefix,
                ["ticket_id"] = ticket.Id.ToString()[..8].ToUpper(),
                ["asunto"]    = ticket.Subject,
                ["cliente"]   = client?.FullName ?? ticket.ProspectName ?? "Prospecto",
                ["prioridad"] = ticket.Priority.ToString(),
                ["vence"]     = dueText,
                ["tecnico"]   = technician.FullName,
            },
            referenciaId: ticket.Id);

        await _notifRepo.AddAsync(new TicketNotification
        {
            TicketId  = ticket.Id, Type = NotificationType.AsignacionTecnico,
            Status    = NotificationStatus.Enviado,
            Recipient = technician.Phone,
            Message   = $"[{prefix}] Ticket #{ticket.Id.ToString()[..8].ToUpper()} — publicado en outbox",
            SentAt    = DateTime.UtcNow,
        });

        return null;
    }

    public static TicketListItemDto MapToListItem(SupportTicket t) => new(
        t.Id, t.Client?.FullName ?? t.ProspectName ?? "", t.Client?.TbnCode ?? "", t.ClientId,
        t.Subject, t.Type.ToString(), t.Priority.ToString(), t.Status.ToString(), t.Origin.ToString(),
        t.Description, t.SupportGroup, t.AssignedTo?.FullName, t.AssignedToUserId,
        t.CreatedBy?.FullName ?? "", t.CreatedAt, t.DueDate, t.ResolvedAt,
        t.FirstRespondedAt, t.SlaCompliant, t.CsatScore, t.WorkLogs?.Sum(w => w.Minutes) ?? 0,
        t.TicketNumber, t.SlaDeadline, t.AutoAssigned);

    public static SlaPlanDto MapSlaPlan(SlaPlan p) =>
        new(p.Id, p.Name, p.Priority, p.FirstResponseMinutes, p.ResolutionMinutes, p.Schedule.ToString(), p.IsActive);
}
