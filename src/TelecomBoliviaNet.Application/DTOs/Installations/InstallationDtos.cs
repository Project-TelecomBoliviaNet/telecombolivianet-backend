namespace TelecomBoliviaNet.Application.DTOs.Installations;

// ══════════════════════════════════════════════════════════════════════════════
// DTOs del Módulo de Instalaciones
// ══════════════════════════════════════════════════════════════════════════════

// ── Slot de disponibilidad ────────────────────────────────────────────────────

/// <summary>
/// Slot de tiempo disponible para agendar instalación.
/// Consumido por el chatbot vía GET /api/instalaciones/slots-disponibles.
/// </summary>
public record SlotDisponibleDto(
    string   Fecha,          // "YYYY-MM-DD"
    string   HoraInicio,     // "HH:mm"
    string   HoraFin,        // "HH:mm"
    int      Disponibles,    // cuántos técnicos libres hay en ese slot
    bool     Disponible      // true si Disponibles > 0
);

// ── Crear instalación ─────────────────────────────────────────────────────────

/// <summary>
/// Payload del bot para agendar instalación.
/// PascalCase — el chatbot envía en PascalCase.
/// </summary>
public class CrearInstalacionDto
{
    /// <summary>ID del cliente. Null si es cliente nuevo aún sin registrar.</summary>
    public Guid?   ClienteId  { get; set; }
    public Guid    PlanId     { get; set; }
    public string  Fecha      { get; set; } = string.Empty;    // "YYYY-MM-DD"
    public string  HoraInicio { get; set; } = string.Empty;    // "HH:mm"
    public string  Direccion  { get; set; } = string.Empty;
    public string? Notas      { get; set; }
}

/// <summary>
/// Payload desde el panel admin para agendar instalación con más detalle.
/// </summary>
public class CrearInstalacionAdminDto
{
    public Guid    ClienteId   { get; set; }
    public Guid    PlanId      { get; set; }
    public string  Fecha       { get; set; } = string.Empty;
    public string  HoraInicio  { get; set; } = string.Empty;
    public int     DuracionMin { get; set; } = 120;
    public string  Direccion   { get; set; } = string.Empty;
    public string? Notas       { get; set; }
    public Guid?   TecnicoId   { get; set; }
}

// ── Cancelar instalación ──────────────────────────────────────────────────────

public class CancelarInstalacionDto
{
    public string  MotivoCancelacion { get; set; } = string.Empty;
    public string  CanceladoPor      { get; set; } = "CLIENTE";   // "CLIENTE" | "ADMIN"
}

// ── Reprogramar instalación ───────────────────────────────────────────────────

public class ReprogramarInstalacionDto
{
    public string  Fecha      { get; set; } = string.Empty;
    public string  HoraInicio { get; set; } = string.Empty;
    public string? Motivo     { get; set; }
}

// ── Completar instalación ─────────────────────────────────────────────────────

public class CompletarInstalacionDto
{
    public string? NotasTecnico { get; set; }
}

// ── Asignar técnico ───────────────────────────────────────────────────────────

public class AsignarTecnicoDto
{
    public Guid TecnicoId { get; set; }
}

// ── Respuestas ────────────────────────────────────────────────────────────────

/// <summary>
/// Respuesta al chatbot al crear instalación.
/// PascalCase para compatibilidad con NestJS.
/// </summary>
public record InstalacionCreadaDto(
    string InstalacionId,
    string TicketId,
    string Fecha,
    string HoraInicio,
    string Status
);

/// <summary>Detalle completo de instalación para el panel admin.</summary>
public record InstalacionDetalleDto(
    Guid     Id,
    string   ClienteTbn,
    string   ClienteNombre,
    string   ClientePhone,
    string   PlanNombre,
    string   Fecha,
    string   HoraInicio,
    string   HoraFin,
    int      DuracionMin,
    string   Direccion,
    string?  Notas,
    string   Status,
    string?  TecnicoNombre,
    Guid?    TecnicoId,
    Guid?    TicketId,
    string?  MotivoCancelacion,
    string?  CanceladoPor,
    DateTime CreadoAt
);

/// <summary>Item de lista para la grilla de instalaciones.</summary>
public record InstalacionListItemDto(
    Guid    Id,
    string  ClienteTbn,
    string  ClienteNombre,
    string  PlanNombre,
    string  Fecha,
    string  HoraInicio,
    string  Status,
    string? TecnicoNombre,
    Guid?   TicketId,
    DateTime CreadoAt
);

// ── Filtros ───────────────────────────────────────────────────────────────────

public class InstalacionFilterDto
{
    public string?   Status     { get; set; }
    public string?   Fecha      { get; set; }    // "YYYY-MM-DD" filtro por fecha exacta
    public Guid?     TecnicoId  { get; set; }
    public Guid?     ClienteId  { get; set; }
    public int       Page       { get; set; } = 1;
    public int       PageSize   { get; set; } = 20;
}
