using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Tickets;

/// <summary>
/// Servicio del Módulo 7 — Tickets de Soporte. US-01 a US-21.
///
/// Las notificaciones al técnico se publican en notif_outbox (INotifPublisher),
/// garantizando entrega eventual con reintentos (patrón Transactional Outbox).
/// Reemplaza el envío directo IWhatsAppNotifier que no tenía reintentos ni garantías.
/// </summary>
public class TicketService
{
    private readonly IGenericRepository<SupportTicket>      _ticketRepo;
    private readonly IGenericRepository<Client>             _clientRepo;
    private readonly IGenericRepository<UserSystem>         _userRepo;
    private readonly IGenericRepository<TicketNotification> _notifRepo;
    private readonly IGenericRepository<TicketComment>      _commentRepo;
    private readonly IGenericRepository<TicketWorkLog>      _workLogRepo;
    private readonly IGenericRepository<TicketVisit>        _visitRepo;
    private readonly IGenericRepository<SlaPlan>            _slaPlanRepo;
    private readonly AuditService                           _audit;
    private readonly INotifPublisher                        _notifPublisher;

    private readonly TicketNumberService  _ticketNumSvc;  // US-TKT-CORRELATIVO
    private readonly TicketBalanceoService _balanceoSvc;   // US-TKT-BALANCEO

    public TicketService(
        IGenericRepository<SupportTicket>      ticketRepo,
        IGenericRepository<Client>             clientRepo,
        IGenericRepository<UserSystem>         userRepo,
        IGenericRepository<TicketNotification> notifRepo,
        IGenericRepository<TicketComment>      commentRepo,
        IGenericRepository<TicketWorkLog>      workLogRepo,
        IGenericRepository<TicketVisit>        visitRepo,
        IGenericRepository<SlaPlan>            slaPlanRepo,
        AuditService                           audit,
        INotifPublisher                        notifPublisher,
        TicketNumberService                    ticketNumSvc,
        TicketBalanceoService                  balanceoSvc)
    {
        _ticketRepo     = ticketRepo;
        _clientRepo     = clientRepo;
        _userRepo       = userRepo;
        _notifRepo      = notifRepo;
        _commentRepo    = commentRepo;
        _workLogRepo    = workLogRepo;
        _visitRepo      = visitRepo;
        _slaPlanRepo    = slaPlanRepo;
        _audit          = audit;
        _notifPublisher = notifPublisher;
        _ticketNumSvc   = ticketNumSvc;
        _balanceoSvc    = balanceoSvc;
    }

    // ── US-01 / US-02 · Crear ticket ─────────────────────────────────────────
    public async Task<Result<TicketDetailDto>> CreateAsync(
        CreateTicketDto dto, Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetByIdAsync(dto.ClientId);
        if (client is null) return Result<TicketDetailDto>.Failure("El cliente indicado no existe.");

        if (!Enum.TryParse<TicketType>(dto.Type, out var type))
            return Result<TicketDetailDto>.Failure("Tipo de ticket inválido.");
        if (!Enum.TryParse<TicketPriority>(dto.Priority, out var priority))
            return Result<TicketDetailDto>.Failure("Prioridad inválida.");

        UserSystem? assignedUser = null;
        if (dto.AssignedToUserId.HasValue)
        {
            assignedUser = await _userRepo.GetByIdAsync(dto.AssignedToUserId.Value);
            if (assignedUser is null) return Result<TicketDetailDto>.Failure("El técnico indicado no existe.");
            if (assignedUser.Role != UserRole.Tecnico && assignedUser.Role != UserRole.Admin)
                return Result<TicketDetailDto>.Failure("Solo se puede asignar a Técnico o Admin.");
        }

        // DueDate: manual si se indicó, sino plan SLA automático (US-05/US-07)
        DateTime? dueDate = dto.SlaDurationHours.HasValue
            ? DateTime.UtcNow.AddHours(dto.SlaDurationHours.Value)
            : await GetDueDateFromSlaAsync(priority);

        // US-TKT-CORRELATIVO
        var ticketNumber = await _ticketNumSvc.NextAsync();

        // US-TKT-SLA: calcular SlaDeadline
        var slaDeadline = dueDate;

        // US-TKT-BALANCEO: auto-asignación si no viene técnico explícito
        bool autoAssigned = false;
        if (!dto.AssignedToUserId.HasValue && dto.AutoAssign == true)
        {
            var autoId = await _balanceoSvc.GetTecnicoMenorCargaAsync(dto.SupportGroup);
            if (autoId.HasValue)
            {
                dto.AssignedToUserId = autoId;
                assignedUser = await _userRepo.GetByIdAsync(autoId.Value);
                autoAssigned = true;
            }
        }

        var ticket = new SupportTicket
        {
            ClientId         = dto.ClientId,
            Subject          = dto.Subject.Trim(),
            Type             = type,
            Priority         = priority,
            Status           = TicketStatus.Abierto,
            Origin           = dto.Origin != null && Enum.TryParse<TicketOrigin>(dto.Origin, out var parsedOrigin)
                               ? parsedOrigin : TicketOrigin.Manual,
            Description      = dto.Description.Trim(),
            SupportGroup     = dto.SupportGroup?.Trim(),
            AssignedToUserId = dto.AssignedToUserId,
            CreatedByUserId  = actorId,
            CreatedAt        = DateTime.UtcNow,
            DueDate          = dueDate,
            TicketNumber     = ticketNumber,  // US-TKT-CORRELATIVO
            SlaDeadline      = slaDeadline,   // US-TKT-SLA
            AutoAssigned     = autoAssigned,  // US-TKT-BALANCEO
        };

        await _ticketRepo.AddAsync(ticket);

        await _audit.LogAsync("Tickets", "TICKET_CREATED",
            $"Ticket '{ticket.Subject}' creado para {client.TbnCode} - {client.FullName}",
            actorId, actorName, ip);

        string? waWarning = null;
        if (assignedUser is not null)
            waWarning = await SendNotificationAsync(ticket, assignedUser, client);

        var detail = await BuildDetailAsync(ticket.Id);
        if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        return Result<TicketDetailDto>.Success(detail with { WhatsAppWarning = waWarning });
    }

    // ── US-T03 · Listar con filtros y paginación ──────────────────────────────
    public async Task<PagedResult<TicketListItemDto>> GetAllAsync(TicketFilterDto filter)
    {
        // BUG FIX: GetAllReadOnly para listado de tickets
        var query = _ticketRepo.GetAllReadOnly()
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.WorkLogs)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(t =>
                (t.Client != null && t.Client.FullName.ToLower().Contains(s)) ||
                (t.Client != null && t.Client.TbnCode.ToLower().Contains(s)) ||
                t.Subject.ToLower().Contains(s) ||
                t.Description.ToLower().Contains(s));
        }
        if (!string.IsNullOrEmpty(filter.Status) && filter.Status != "all"
            && Enum.TryParse<TicketStatus>(filter.Status, out var st))
            query = query.Where(t => t.Status == st);
        if (!string.IsNullOrEmpty(filter.Priority) && filter.Priority != "all"
            && Enum.TryParse<TicketPriority>(filter.Priority, out var pr))
            query = query.Where(t => t.Priority == pr);
        if (!string.IsNullOrEmpty(filter.Type) && filter.Type != "all"
            && Enum.TryParse<TicketType>(filter.Type, out var tp))
            query = query.Where(t => t.Type == tp);
        if (filter.AssignedToId.HasValue)
            query = query.Where(t => t.AssignedToUserId == filter.AssignedToId.Value);
        if (filter.OverdueSla == true)
        {
            var now = DateTime.UtcNow;
            query = query.Where(t =>
                t.DueDate.HasValue && t.DueDate.Value < now &&
                t.Status != TicketStatus.Resuelto && t.Status != TicketStatus.Cerrado);
        }
        if (filter.DateFrom.HasValue)
            query = query.Where(t => t.CreatedAt >= filter.DateFrom.Value);
        if (filter.DateTo.HasValue)
            query = query.Where(t => t.CreatedAt <= filter.DateTo.Value.AddDays(1).AddTicks(-1));
        if (filter.SlaCompliant.HasValue)
            query = query.Where(t => t.SlaCompliant == filter.SlaCompliant.Value);

        query = query
            .OrderBy(t => t.Status == TicketStatus.Resuelto || t.Status == TicketStatus.Cerrado ? 1 : 0)
            .ThenBy(t => t.Priority == TicketPriority.Critica ? 0
                       : t.Priority == TicketPriority.Alta    ? 1
                       : t.Priority == TicketPriority.Media   ? 2
                       : 3)
            .ThenBy(t => t.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PagedResult<TicketListItemDto>(
            items.Select(MapToListItem), total, filter.PageNumber, filter.PageSize);
    }

    // ── US-T04 · Detalle ──────────────────────────────────────────────────────
    public async Task<TicketDetailDto?> GetByIdAsync(Guid id)
    {
        var exists = await _ticketRepo.AnyAsync(t => t.Id == id);
        return exists ? await BuildDetailAsync(id) : null;
    }

    // ── US-08/US-15/US-17 · Cambiar estado ───────────────────────────────────
    public async Task<Result<TicketDetailDto>> ChangeStatusAsync(
        Guid id, ChangeTicketStatusDto dto, Guid actorId, string actorName, string ip)
    {
        var ticket = await LoadTicketAsync(id);
        if (ticket is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        if (!Enum.TryParse<TicketStatus>(dto.Status, out var newStatus))
            return Result<TicketDetailDto>.Failure("Estado inválido.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketDetailDto>.Failure("Un ticket cerrado no puede modificarse.");

        // US-15: mensaje de resolución obligatorio
        if (newStatus == TicketStatus.Resuelto && string.IsNullOrWhiteSpace(dto.ResolutionMessage))
            return Result<TicketDetailDto>.Failure("Se requiere mensaje de resolución al marcar como Resuelto.");

        var prevStatus = ticket.Status;
        ticket.Status  = newStatus;

        // US-08 AC-2: registrar primera respuesta
        if (newStatus == TicketStatus.EnProceso && ticket.FirstRespondedAt is null)
            ticket.FirstRespondedAt = DateTime.UtcNow;

        // US-15: guardar resolución y SLA
        if (newStatus == TicketStatus.Resuelto && ticket.ResolvedAt is null)
        {
            ticket.ResolvedAt        = DateTime.UtcNow;
            ticket.ResolutionMessage = dto.ResolutionMessage!.Trim();
            ticket.SlaCompliant      = !ticket.DueDate.HasValue || ticket.ResolvedAt <= ticket.DueDate;
        }

        // US-17: reapertura reinicia SLA y notifica técnico
        if (prevStatus == TicketStatus.Resuelto && newStatus == TicketStatus.EnProceso)
        {
            ticket.ResolvedAt        = null;
            ticket.ResolutionMessage = null;
            ticket.SlaCompliant      = null;
            ticket.DueDate           = await GetDueDateFromSlaAsync(ticket.Priority);

            // US-17 AC-3: notificar técnico asignado
            if (ticket.AssignedToUserId.HasValue && ticket.Client is not null)
            {
                var tech = ticket.AssignedTo ?? await _userRepo.GetByIdAsync(ticket.AssignedToUserId.Value);
                if (tech is not null)
                    await SendNotificationAsync(ticket, tech, ticket.Client, reopen: true);
            }
        }

        await _ticketRepo.UpdateAsync(ticket);

        // US-08 AC-3 / US-15 / US-17: historial en el ticket
        var historyText = newStatus switch
        {
            TicketStatus.EnProceso when prevStatus == TicketStatus.Abierto => "▶ Ticket en proceso de atención.",
            TicketStatus.EnProceso when prevStatus == TicketStatus.Resuelto => "🔄 Ticket reabierto — problema persiste. SLA reiniciado.",
            TicketStatus.Resuelto  => $"✅ Ticket resuelto. {dto.ResolutionMessage}",
            TicketStatus.Cerrado   => "🔒 Ticket cerrado.",
            _ => $"Estado cambiado a {newStatus}."
        };
        await _commentRepo.AddAsync(new TicketComment
        {
            TicketId  = ticket.Id, AuthorId = actorId,
            Type      = CommentType.NotaInterna,
            Body      = historyText, CreatedAt = DateTime.UtcNow,
        });

        await _audit.LogAsync("Tickets", "STATUS_CHANGED",
            $"Ticket '{ticket.Subject}': {prevStatus} → {newStatus}", actorId, actorName, ip);

        { var detail = await BuildDetailAsync(id); if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado."); return Result<TicketDetailDto>.Success(detail); }
    }

    // ── US-T06/US-T11 · Asignar técnico ──────────────────────────────────────
    public async Task<Result<TicketDetailDto>> AssignTechnicianAsync(
        Guid id, AssignTicketDto dto, Guid actorId, string actorName, string ip)
    {
        var ticket = await LoadTicketAsync(id);
        if (ticket is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketDetailDto>.Failure("No se puede asignar en ticket cerrado.");

        var tech = await _userRepo.GetByIdAsync(dto.TechnicianId);
        if (tech is null) return Result<TicketDetailDto>.Failure("Técnico no encontrado.");
        if (tech.Role != UserRole.Tecnico && tech.Role != UserRole.Admin)
            return Result<TicketDetailDto>.Failure("Solo se puede asignar a Técnico o Admin.");

        ticket.AssignedToUserId = dto.TechnicianId;
        ticket.AssignedTo       = tech;
        if (ticket.Status == TicketStatus.Abierto)
            ticket.Status = TicketStatus.EnProceso;

        await _ticketRepo.UpdateAsync(ticket);

        // US-11 AC-3: historial de asignación
        await _commentRepo.AddAsync(new TicketComment
        {
            TicketId = ticket.Id, AuthorId = actorId,
            Type     = CommentType.NotaInterna,
            Body     = $"👤 Ticket asignado a {tech.FullName}.", CreatedAt = DateTime.UtcNow,
        });

        await _audit.LogAsync("Tickets", "TICKET_ASSIGNED",
            $"Ticket '{ticket.Subject}' → {tech.FullName}", actorId, actorName, ip);

        string? waWarning = null;
        if (ticket.Client is not null)
            waWarning = await SendNotificationAsync(ticket, tech, ticket.Client);

        var detail = await BuildDetailAsync(id);
        if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        return Result<TicketDetailDto>.Success(detail with { WhatsAppWarning = waWarning });
    }

    // ── US-T07/US-12 · Editar ticket ─────────────────────────────────────────
    public async Task<Result<TicketDetailDto>> UpdateAsync(
        Guid id, UpdateTicketDto dto, Guid actorId, string actorName, string ip)
    {
        var ticket = await LoadTicketAsync(id);
        if (ticket is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketDetailDto>.Failure("Un ticket cerrado no puede modificarse.");

        if (dto.Subject is not null)      ticket.Subject      = dto.Subject.Trim();
        if (dto.Description is not null)  ticket.Description  = dto.Description.Trim();
        if (dto.SupportGroup is not null) ticket.SupportGroup = dto.SupportGroup.Trim();
        if (dto.RootCause is not null)    ticket.RootCause    = dto.RootCause.Trim();

        // US-12: cambio de prioridad ajusta SLA
        if (dto.Priority is not null && Enum.TryParse<TicketPriority>(dto.Priority, out var pr))
        {
            var oldPriority = ticket.Priority;
            ticket.Priority = pr;
            ticket.DueDate  = await GetDueDateFromSlaAsync(pr);

            // US-12 AC-3: historial cambio de prioridad
            await _commentRepo.AddAsync(new TicketComment
            {
                TicketId = ticket.Id, AuthorId = actorId,
                Type     = CommentType.NotaInterna,
                Body     = $"⚡ Prioridad cambiada: {oldPriority} → {pr}. SLA actualizado.",
                CreatedAt = DateTime.UtcNow,
            });
        }

        await _ticketRepo.UpdateAsync(ticket);
        await _audit.LogAsync("Tickets", "TICKET_UPDATED",
            $"Ticket '{ticket.Subject}' actualizado.", actorId, actorName, ip);

        { var detail = await BuildDetailAsync(id); if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado."); return Result<TicketDetailDto>.Success(detail); }
    }

    // ── US-19 · Cerrar ticket ─────────────────────────────────────────────────
    public async Task<Result<TicketDetailDto>> CloseAsync(
        Guid id, Guid actorId, string actorName, string ip)
    {
        var ticket = await LoadTicketAsync(id);
        if (ticket is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketDetailDto>.Failure("El ticket ya está cerrado.");
        if (ticket.Status != TicketStatus.Resuelto)
            return Result<TicketDetailDto>.Failure("Solo se pueden cerrar tickets en estado Resuelto.");

        ticket.Status   = TicketStatus.Cerrado;
        ticket.ClosedAt = DateTime.UtcNow;
        await _ticketRepo.UpdateAsync(ticket);

        await _commentRepo.AddAsync(new TicketComment
        {
            TicketId = ticket.Id, AuthorId = actorId,
            Type     = CommentType.NotaInterna,
            Body     = "🔒 Ticket cerrado manualmente.", CreatedAt = DateTime.UtcNow,
        });

        await _audit.LogAsync("Tickets", "TICKET_CLOSED",
            $"Ticket '{ticket.Subject}' cerrado.", actorId, actorName, ip);

        { var detail = await BuildDetailAsync(id); if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado."); return Result<TicketDetailDto>.Success(detail); }
    }

    // ── US-09/US-10/US-16 · Comentar / nota interna ───────────────────────────
    public async Task<Result<TicketCommentDto>> AddCommentAsync(
        Guid ticketId, AddCommentDto dto, Guid actorId, string actorName, string ip)
    {
        var ticket = await _ticketRepo.GetByIdAsync(ticketId);
        if (ticket is null) return Result<TicketCommentDto>.Failure("Ticket no encontrado.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketCommentDto>.Failure("No se puede comentar en un ticket cerrado.");
        if (!Enum.TryParse<CommentType>(dto.Type, out var commentType))
            return Result<TicketCommentDto>.Failure("Tipo de comentario inválido.");

        var author = await _userRepo.GetByIdAsync(actorId);
        var comment = new TicketComment
        {
            TicketId = ticketId, AuthorId = actorId,
            Type = commentType, Body = dto.Body.Trim(), CreatedAt = DateTime.UtcNow,
        };
        await _commentRepo.AddAsync(comment);

        await _audit.LogAsync("Tickets", "COMMENT_ADDED",
            $"Comentario ({commentType}) en ticket {ticketId}", actorId, actorName, ip);

        return Result<TicketCommentDto>.Success(new TicketCommentDto(
            comment.Id, comment.Type.ToString(), comment.Body,
            author?.FullName ?? actorName, actorId, comment.CreatedAt));
    }

    // ── US-13 · Tiempo trabajado ──────────────────────────────────────────────
    public async Task<Result<TicketWorkLogDto>> AddWorkLogAsync(
        Guid ticketId, AddWorkLogDto dto, Guid actorId, string actorName, string ip)
    {
        var ticket = await _ticketRepo.GetByIdAsync(ticketId);
        if (ticket is null) return Result<TicketWorkLogDto>.Failure("Ticket no encontrado.");

        var totalMinutes = dto.Hours * 60 + dto.Minutes;
        if (totalMinutes <= 0) return Result<TicketWorkLogDto>.Failure("El tiempo debe ser mayor a 0.");

        var user = await _userRepo.GetByIdAsync(actorId);
        var log  = new TicketWorkLog
        {
            TicketId = ticketId, UserId = actorId,
            Minutes  = totalMinutes, Notes = dto.Notes?.Trim(), LoggedAt = DateTime.UtcNow,
        };
        await _workLogRepo.AddAsync(log);

        await _audit.LogAsync("Tickets", "WORKLOG_ADDED",
            $"{totalMinutes}min en ticket {ticketId}", actorId, actorName, ip);

        return Result<TicketWorkLogDto>.Success(new TicketWorkLogDto(
            log.Id, user?.FullName ?? actorName, actorId, totalMinutes, log.Notes, log.LoggedAt));
    }

    // ── US-14 · Visita técnica ────────────────────────────────────────────────
    public async Task<Result<TicketVisitDto>> ScheduleVisitAsync(
        Guid ticketId, ScheduleVisitDto dto, Guid actorId, string actorName, string ip)
    {
        var ticket = await _ticketRepo.GetByIdAsync(ticketId);
        if (ticket is null) return Result<TicketVisitDto>.Failure("Ticket no encontrado.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketVisitDto>.Failure("No se puede programar visita en ticket cerrado.");

        UserSystem? tech = null;
        if (dto.TechnicianId.HasValue)
        {
            tech = await _userRepo.GetByIdAsync(dto.TechnicianId.Value);
            if (tech is null) return Result<TicketVisitDto>.Failure("Técnico no encontrado.");
        }

        var visit = new TicketVisit
        {
            TicketId = ticketId, ScheduledAt = dto.ScheduledAt,
            TechnicianId = dto.TechnicianId, Observations = dto.Observations?.Trim(),
            CreatedByUserId = actorId, CreatedAt = DateTime.UtcNow,
        };
        await _visitRepo.AddAsync(visit);

        // US-14 AC-03: nota interna automática
        if (!string.IsNullOrWhiteSpace(dto.Observations))
            await _commentRepo.AddAsync(new TicketComment
            {
                TicketId = ticketId, AuthorId = actorId,
                Type = CommentType.NotaInterna,
                Body = $"[Visita programada {dto.ScheduledAt:dd/MM/yyyy HH:mm}] {dto.Observations}",
                CreatedAt = DateTime.UtcNow,
            });

        await _audit.LogAsync("Tickets", "VISIT_SCHEDULED",
            $"Visita {dto.ScheduledAt:dd/MM} en ticket {ticketId}", actorId, actorName, ip);

        return Result<TicketVisitDto>.Success(new TicketVisitDto(
            visit.Id, visit.ScheduledAt, tech?.FullName, visit.TechnicianId, visit.Observations, visit.CreatedAt));
    }

    // ── US-18 · CSAT ──────────────────────────────────────────────────────────
    public async Task<Result<TicketDetailDto>> SubmitCsatAsync(Guid ticketId, SubmitCsatDto dto, string ip)
    {
        if (dto.Score < 1 || dto.Score > 5)
            return Result<TicketDetailDto>.Failure("El puntaje CSAT debe estar entre 1 y 5.");
        var ticket = await LoadTicketAsync(ticketId);
        if (ticket is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        if (ticket.Status != TicketStatus.Resuelto && ticket.Status != TicketStatus.Cerrado)
            return Result<TicketDetailDto>.Failure("Solo se puede puntuar un ticket resuelto o cerrado.");

        ticket.CsatScore       = dto.Score;
        ticket.CsatRespondedAt = DateTime.UtcNow;
        await _ticketRepo.UpdateAsync(ticket);

        { var detail = await BuildDetailAsync(ticketId); if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado."); return Result<TicketDetailDto>.Success(detail); }
    }

    // ── US-T09 · SLA vencidos ─────────────────────────────────────────────────
    public async Task<IEnumerable<TicketListItemDto>> GetOverdueSlaAsync()
    {
        var now   = DateTime.UtcNow;
        var items = await _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.WorkLogs)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value < now
                     && t.Status != TicketStatus.Resuelto && t.Status != TicketStatus.Cerrado)
            .OrderBy(t => t.DueDate)
            .ToListAsync();
        return items.Select(MapToListItem);
    }

    // ── SLA Report con filtros reales ────────────────────────────────────────
    // BUG FIX: GetSlaReportAsync aplica los filtros desde/hasta/tecnico en BD
    // en lugar de ignorarlos como hacía el controller.
    public async Task<IEnumerable<TicketListItemDto>> GetSlaReportAsync(
        DateTime? desde, DateTime? hasta, string? tecnico)
    {
        var now   = DateTime.UtcNow;
        var query = _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.WorkLogs)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value < now
                     && t.Status != TicketStatus.Resuelto && t.Status != TicketStatus.Cerrado);

        if (desde.HasValue)
            query = query.Where(t => t.CreatedAt >= desde.Value);
        if (hasta.HasValue)
            query = query.Where(t => t.CreatedAt <= hasta.Value);
        if (!string.IsNullOrWhiteSpace(tecnico))
            query = query.Where(t => t.AssignedTo != null &&
                t.AssignedTo.FullName.Contains(tecnico));

        var items = await query.OrderBy(t => t.DueDate).ToListAsync();
        return items.Select(MapToListItem);
    }

    // ── US-T10 · Kanban ───────────────────────────────────────────────────────
    public async Task<Dictionary<string, IEnumerable<TicketListItemDto>>> GetKanbanAsync(TicketFilterDto filter)
    {
        filter.PageNumber = 1; filter.PageSize = int.MaxValue;
        var paged = await GetAllAsync(filter);
        var groups = paged.Items.GroupBy(t => t.Status)
            .ToDictionary(g => g.Key, g => g.AsEnumerable());
        foreach (var s in Enum.GetNames<TicketStatus>())
            groups.TryAdd(s, Enumerable.Empty<TicketListItemDto>());
        return groups;
    }

    // ── US-T12/US-21 · KPI ────────────────────────────────────────────────────
    public async Task<TicketKpiDto> GetKpiAsync()
    {
        var now   = DateTime.UtcNow;
        var today = now.Date;
        var q     = _ticketRepo.GetAll();

        // BUG FIX: Queries agregadas en BD — evita cargar todos los tickets a memoria
        var abierto   = await q.CountAsync(t => t.Status == TicketStatus.Abierto);
        var enProceso = await q.CountAsync(t => t.Status == TicketStatus.EnProceso);
        var resuelto  = await q.CountAsync(t => t.Status == TicketStatus.Resuelto);
        var cerrado   = await q.CountAsync(t => t.Status == TicketStatus.Cerrado);
        var vencidos  = await q.CountAsync(t =>
            t.DueDate.HasValue && t.DueDate.Value < now &&
            t.Status != TicketStatus.Resuelto && t.Status != TicketStatus.Cerrado);
        var hoy       = await q.CountAsync(t => t.CreatedAt.Date == today);
        var slaCump   = await q.CountAsync(t => t.SlaCompliant == true);
        var slaIncump = await q.CountAsync(t => t.SlaCompliant == false);
        var csatProm  = await q
            .Where(t => t.CsatScore.HasValue)
            .AverageAsync(t => (double?)t.CsatScore!.Value);

        return new TicketKpiDto(abierto, enProceso, resuelto, cerrado,
            vencidos, hoy, slaCump, slaIncump, csatProm);
    }

    // ── Por cliente ───────────────────────────────────────────────────────────
    public async Task<IEnumerable<TicketListItemDto>> GetByClientAsync(Guid clientId)
    {
        var items = await _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.WorkLogs)
            .Where(t => t.ClientId == clientId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        return items.Select(MapToListItem);
    }

    // ── SLA Plans (US-05, US-07) ─────────────────────────────────────────────
    public async Task<IEnumerable<SlaPlanDto>> GetSlaPlansAsync()
        => (await _slaPlanRepo.GetAll().OrderBy(p => p.Priority).ToListAsync()).Select(MapSlaPlan);

    public async Task<Result<SlaPlanDto>> CreateSlaPlanAsync(CreateSlaPlanDto dto)
    {
        if (!new[] { "Critica", "Alta", "Media", "Baja" }.Contains(dto.Priority))
            return Result<SlaPlanDto>.Failure("Prioridad inválida.");
        if (await _slaPlanRepo.AnyAsync(p => p.Priority == dto.Priority))
            return Result<SlaPlanDto>.Failure($"Ya existe un plan SLA para la prioridad {dto.Priority}.");
        if (!Enum.TryParse<SlaSchedule>(dto.Schedule, out var sched))
            return Result<SlaPlanDto>.Failure("Horario inválido. Use: Veinticuatro7 | Laboral");

        var plan = new SlaPlan
        {
            Name = dto.Name.Trim(), Priority = dto.Priority,
            FirstResponseMinutes = dto.FirstResponseMinutes,
            ResolutionMinutes    = dto.ResolutionMinutes,
            Schedule             = sched, IsActive = true, CreatedAt = DateTime.UtcNow,
        };
        await _slaPlanRepo.AddAsync(plan);
        return Result<SlaPlanDto>.Success(MapSlaPlan(plan));
    }

    public async Task<Result<SlaPlanDto>> UpdateSlaPlanAsync(Guid id, UpdateSlaPlanDto dto)
    {
        var plan = await _slaPlanRepo.GetByIdAsync(id);
        if (plan is null) return Result<SlaPlanDto>.Failure("Plan SLA no encontrado.");
        if (dto.Name is not null)                plan.Name                 = dto.Name.Trim();
        if (dto.FirstResponseMinutes.HasValue)   plan.FirstResponseMinutes = dto.FirstResponseMinutes.Value;
        if (dto.ResolutionMinutes.HasValue)      plan.ResolutionMinutes    = dto.ResolutionMinutes.Value;
        if (dto.IsActive.HasValue)               plan.IsActive             = dto.IsActive.Value;
        if (dto.Schedule is not null)
        {
            if (!Enum.TryParse<SlaSchedule>(dto.Schedule, out var sched))
                return Result<SlaPlanDto>.Failure("Horario inválido.");
            plan.Schedule = sched;
        }
        await _slaPlanRepo.UpdateAsync(plan);
        return Result<SlaPlanDto>.Success(MapSlaPlan(plan));
    }

    public async Task<Result<bool>> DeleteSlaPlanAsync(Guid id)
    {
        var plan = await _slaPlanRepo.GetByIdAsync(id);
        if (plan is null) return Result<bool>.Failure("Plan SLA no encontrado.");
        await _slaPlanRepo.DeleteAsync(plan.Id);
        return Result<bool>.Success(true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private async Task<DateTime?> GetDueDateFromSlaAsync(TicketPriority priority)
    {
        var plan = await _slaPlanRepo.GetAll()
            .FirstOrDefaultAsync(p => p.Priority == priority.ToString() && p.IsActive);
        return plan is not null ? DateTime.UtcNow.AddMinutes(plan.ResolutionMinutes) : null;
    }

    private async Task<SupportTicket?> LoadTicketAsync(Guid id) =>
        await _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo).Include(t => t.CreatedBy)
            .FirstOrDefaultAsync(t => t.Id == id);

    /// <summary>Construye el DTO de detalle cargando siempre colecciones frescas desde DB.</summary>
    /// BUG FIX: retorna null en lugar de lanzar excepción para evitar 500 cuando el ticket
    /// fue eliminado entre la verificación y la carga. Los callers deben manejar null.
    private async Task<TicketDetailDto?> BuildDetailAsync(Guid ticketId)
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
            t.Id, t.Client?.FullName ?? "", t.Client?.TbnCode ?? "", t.ClientId,
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
                v.TechnicianId, v.Observations, v.CreatedAt))
        );
    }

    private static TicketListItemDto MapToListItem(SupportTicket t) => new(
        t.Id, t.Client?.FullName ?? "", t.Client?.TbnCode ?? "", t.ClientId,
        t.Subject, t.Type.ToString(), t.Priority.ToString(), t.Status.ToString(), t.Origin.ToString(),
        t.Description, t.SupportGroup, t.AssignedTo?.FullName, t.AssignedToUserId,
        t.CreatedBy?.FullName ?? "", t.CreatedAt, t.DueDate, t.ResolvedAt,
        t.FirstRespondedAt, t.SlaCompliant, t.CsatScore, t.WorkLogs?.Sum(w => w.Minutes) ?? 0,
        // M9 fields
        t.TicketNumber, t.SlaDeadline, t.AutoAssigned);

    private static SlaPlanDto MapSlaPlan(SlaPlan p) =>
        new(p.Id, p.Name, p.Priority, p.FirstResponseMinutes, p.ResolutionMinutes, p.Schedule.ToString(), p.IsActive);

    private async Task<string?> SendNotificationAsync(
        SupportTicket ticket, UserSystem technician, Client client, bool reopen = false)
    {
        // Si el técnico no tiene número WhatsApp configurado, registrar advertencia y continuar.
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

        // Publicar en outbox — el Worker Python envía con reintentos y respeto de ventana horaria.
        await _notifPublisher.PublishAsync(
            NotifType.TICKET_ASIGNADO,
            client.Id,
            technician.Phone,
            new Dictionary<string, string>
            {
                ["prefijo"]     = prefix,
                ["ticket_id"]   = ticket.Id.ToString()[..8].ToUpper(),
                ["asunto"]      = ticket.Subject,
                ["cliente"]     = client.FullName,
                ["prioridad"]   = ticket.Priority.ToString(),
                ["vence"]       = dueText,
                ["tecnico"]     = technician.FullName,
            },
            referenciaId: ticket.Id);

        // Registrar en TicketNotification para trazabilidad en el panel.
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
}