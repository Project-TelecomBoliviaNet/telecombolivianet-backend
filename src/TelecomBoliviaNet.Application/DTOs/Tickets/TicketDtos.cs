namespace TelecomBoliviaNet.Application.DTOs.Tickets;

// ── Filtro ────────────────────────────────────────────────────────────────────
public class TicketFilterDto
{
    public string?   Search       { get; set; }
    public string?   Status       { get; set; }
    public string?   Priority     { get; set; }
    public string?   Type         { get; set; }
    public Guid?     AssignedToId { get; set; }
    public bool?     OverdueSla   { get; set; }
    public DateTime? DateFrom     { get; set; }
    public DateTime? DateTo       { get; set; }
    public bool?     SlaCompliant { get; set; }
    public int       PageNumber   { get; set; } = 1;
    public int       PageSize     { get; set; } = 20;
}

// ── Sub-DTOs ──────────────────────────────────────────────────────────────────
public record TicketCommentDto(Guid Id, string Type, string Body, string AuthorName, Guid AuthorId, DateTime CreatedAt);
public record TicketWorkLogDto(Guid Id, string UserName, Guid UserId, int TotalMinutes, string? Notes, DateTime LoggedAt);
public record TicketVisitDto(Guid Id, DateTime ScheduledAt, string? TechnicianName, Guid? TechnicianId, string? Observations, DateTime CreatedAt, string Status = "Programada");

// M11 — Historial de actividad de un ticket (audit log proyectado)
public record TicketActivityDto(
    Guid     Id,
    string   Action,
    string   Description,
    string   UserName,
    Guid?    UserId,
    DateTime Timestamp);

// M13 — Plantillas de respuesta rápida
public record CannedResponseDto(
    Guid     Id,
    string   Title,
    string   Body,
    string?  Category,
    bool     IsActive,
    DateTime CreatedAt);

public class CreateCannedResponseDto
{
    public string  Title    { get; set; } = string.Empty;
    public string  Body     { get; set; } = string.Empty;
    public string? Category { get; set; }
}

public class UpdateCannedResponseDto
{
    public string?  Title    { get; set; }
    public string?  Body     { get; set; }
    public string?  Category { get; set; }
    public bool?    IsActive { get; set; }
}

// ── Lista ─────────────────────────────────────────────────────────────────────
public record TicketListItemDto(
    Guid    Id, string ClientName, string ClientTbn, Guid? ClientId,
    string  Subject, string Type, string Priority, string Status, string Origin,
    string  Description, string? SupportGroup,
    string? AssignedToName, Guid? AssignedToId, string CreatedByName,
    DateTime CreatedAt, DateTime? DueDate, DateTime? ResolvedAt,
    DateTime? FirstRespondedAt, bool? SlaCompliant, int? CsatScore, int TotalWorkMinutes,
    // M9
    string?   TicketNumber,   // US-TKT-CORRELATIVO
    DateTime? SlaDeadline,    // US-TKT-SLA
    bool      AutoAssigned    // US-TKT-BALANCEO
);

// ── Detalle ───────────────────────────────────────────────────────────────────
public record TicketDetailDto(
    Guid    Id, string ClientName, string ClientTbn, Guid? ClientId,
    string  Subject, string Type, string Priority, string Status, string Origin,
    string  Description, string? SupportGroup,
    string? AssignedToName, Guid? AssignedToUserId,
    string  CreatedByName, Guid CreatedByUserId,
    DateTime CreatedAt, DateTime? DueDate, DateTime? ResolvedAt, DateTime? ClosedAt,
    DateTime? FirstRespondedAt, bool? SlaCompliant,
    string? ResolutionMessage, string? RootCause,
    int? CsatScore, DateTime? CsatRespondedAt, int TotalWorkMinutes,
    IEnumerable<TicketCommentDto> Comments,
    IEnumerable<TicketWorkLogDto> WorkLogs,
    IEnumerable<TicketVisitDto>   Visits,
    // SLA Pausa
    int       SlaTotalPausedMinutes = 0,
    DateTime? SlaPausedAt           = null,
    string?   WhatsAppWarning       = null,
    string?   TicketNumber          = null,   // correlativo TK-YYYY-NNNN
    string?   ImageUrl              = null,   // imagen adjunta del cliente (WhatsApp bot)
    decimal?  ClientGpsLatitude     = null,
    decimal?  ClientGpsLongitude    = null
);

// ── Commands ──────────────────────────────────────────────────────────────────
public class CreateTicketDto
{
    public Guid?   ClientId         { get; set; }
    // Campos de prospecto — requeridos cuando ClientId es null
    public string? ProspectName     { get; set; }
    public string? ProspectPhone    { get; set; }
    public string  Subject          { get; set; } = string.Empty;
    public string  Type             { get; set; } = string.Empty;
    public string  Priority         { get; set; } = string.Empty;
    public string  Description      { get; set; } = string.Empty;
    public string? SupportGroup     { get; set; }
    public Guid?   AssignedToUserId { get; set; }
    public int?    SlaDurationHours { get; set; }
    // M9
    public bool?   AutoAssign       { get; set; }
    public string? Origin           { get; set; }
    public string? ImageUrl         { get; set; }
}

// M9: US-TKT-BALANCEO — carga por técnico y resumen
public record TecnicoCargaDto(
    Guid   TecnicoId,
    string TecnicoNombre,
    int    TicketsActivos,
    int    TicketsCriticos
);

public record BalanceoResumenDto(List<TecnicoCargaDto> Tecnicos);

// M9: tipos de ticket disponibles
public record TicketTypesDto(string[] Types);


public class UpdateTicketDto
{
    public string? Subject      { get; set; }
    public string? Description  { get; set; }
    public string? Priority     { get; set; }
    public string? SupportGroup { get; set; }
    public string? RootCause    { get; set; }
}

public class ChangeTicketStatusDto
{
    public string  Status            { get; set; } = string.Empty;
    public string? ResolutionMessage { get; set; }
}

public class AssignTicketDto { public Guid TechnicianId { get; set; } }

public class AddCommentDto
{
    public string Type { get; set; } = "NotaInterna";
    public string Body { get; set; } = string.Empty;
}

public class AddWorkLogDto
{
    public int     Hours   { get; set; }
    public int     Minutes { get; set; }
    public string? Notes   { get; set; }
}

public class ScheduleVisitDto
{
    public DateTime ScheduledAt  { get; set; }
    public Guid?    TechnicianId { get; set; }
    public string?  Observations { get; set; }
}

public class SubmitCsatDto { public int Score { get; set; } }

// ── KPI / Métricas (US-21) ────────────────────────────────────────────────────
public record SlaByPriorityDto(string Priority, int Compliant, int Breached);

public record TicketKpiDto(
    int TotalOpen, int TotalInProcess, int TotalResolved, int TotalClosed,
    int OverdueSla, int CreatedToday,
    int SlaCompliantCount, int SlaBreachedCount, double? AvgCsatScore,
    // Fase A — estándar ISP CRM
    IEnumerable<SlaByPriorityDto> SlaByPriority,   // cumplimiento por prioridad
    double? AvgResolutionMinutes,                    // MTTR
    double? AvgFirstResponseMinutes                  // MTTA
);

// ── SLA Alerts (dashboard) ────────────────────────────────────────────────────
public record SlaAlertItemDto(
    Guid      Id,
    string    TicketNumber,
    string    Subject,
    string    Priority,
    string    Status,
    string    ClientName,
    string?   AssignedToName,
    DateTime  CreatedAt,
    DateTime  DueDate,
    int       PctElapsed,   // 0-200+  (>100 = vencido)
    string    Level         // "Breached" | "Warning" | "Attention"
);

public record SlaAlertsDto(
    IEnumerable<SlaAlertItemDto> Breached,   // SLA vencido
    IEnumerable<SlaAlertItemDto> Warning,    // >75% consumido
    IEnumerable<SlaAlertItemDto> Attention   // >50% consumido
);

// ── SLA Plans (US-05, US-07) ─────────────────────────────────────────────────
public class CreateSlaPlanDto
{
    public string Name                 { get; set; } = string.Empty;
    public string Priority             { get; set; } = string.Empty;
    public int    FirstResponseMinutes { get; set; }
    public int    ResolutionMinutes    { get; set; }
    public string Schedule             { get; set; } = "Veinticuatro7";
}

public class UpdateSlaPlanDto
{
    public string? Name                 { get; set; }
    public int?    FirstResponseMinutes { get; set; }
    public int?    ResolutionMinutes    { get; set; }
    public string? Schedule             { get; set; }
    public bool?   IsActive             { get; set; }
}

public record SlaPlanDto(
    Guid Id, string Name, string Priority,
    int FirstResponseMinutes, int ResolutionMinutes, string Schedule, bool IsActive
);
