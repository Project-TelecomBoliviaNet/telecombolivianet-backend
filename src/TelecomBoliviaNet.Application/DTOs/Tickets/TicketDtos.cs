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
public record TicketVisitDto(Guid Id, DateTime ScheduledAt, string? TechnicianName, Guid? TechnicianId, string? Observations, DateTime CreatedAt);

// ── Lista ─────────────────────────────────────────────────────────────────────
public record TicketListItemDto(
    Guid    Id, string ClientName, string ClientTbn, Guid ClientId,
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
    Guid    Id, string ClientName, string ClientTbn, Guid ClientId,
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
    string? WhatsAppWarning = null
);

// ── Commands ──────────────────────────────────────────────────────────────────
public class CreateTicketDto
{
    public Guid    ClientId         { get; set; }
    public string  Subject          { get; set; } = string.Empty;
    public string  Type             { get; set; } = string.Empty;
    public string  Priority         { get; set; } = string.Empty;
    public string  Description      { get; set; } = string.Empty;
    public string? SupportGroup     { get; set; }
    public Guid?   AssignedToUserId { get; set; }
    public int?    SlaDurationHours { get; set; }
    // M9
    public bool?   AutoAssign       { get; set; }   // US-TKT-BALANCEO: auto-asignar al técnico con menor carga
    public string? Origin           { get; set; }   // "Bot" | "Manual" | "Automatico" — null→Manual
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
public record TicketKpiDto(
    int TotalOpen, int TotalInProcess, int TotalResolved, int TotalClosed,
    int OverdueSla, int CreatedToday,
    int SlaCompliantCount, int SlaBreachedCount, double? AvgCsatScore
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
