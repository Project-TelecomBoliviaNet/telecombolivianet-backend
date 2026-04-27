using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Installations;

public enum InstallationStatus
{
    Pendiente,    // agendada, esperando ejecución
    EnProceso,    // técnico en camino / trabajando
    Completada,   // instalación exitosa
    Cancelada,    // cancelada por cliente o admin
    Reprogramada, // se movió a otro slot
}

/// <summary>
/// Instalación de servicio agendada para un cliente.
/// Cada instalación genera un ticket de tipo InstalacionNueva automáticamente.
///
/// Flujo:
///   1. Cliente pide instalación por WhatsApp → bot llama POST /api/instalaciones
///   2. Se crea Installation + SupportTicket (InstalacionNueva)
///   3. Admin asigna técnico en el ticket
///   4. Técnico completa → PATCH /api/instalaciones/{id}/completar
///   5. O cliente/admin cancela → PATCH /api/instalaciones/{id}/cancelar
/// </summary>
public class Installation : Entity
{
    // ── Cliente ──────────────────────────────────────────────────────────────
    public Guid     ClientId    { get; set; }
    public Client?  Client      { get; set; }

    // ── Plan solicitado ───────────────────────────────────────────────────────
    public Guid     PlanId      { get; set; }
    public Plan?    Plan        { get; set; }

    // ── Agenda ────────────────────────────────────────────────────────────────
    /// <summary>Fecha de la instalación (solo la parte de fecha, UTC).</summary>
    public DateTime Fecha       { get; set; }
    /// <summary>Hora de inicio acordada (ej: 09:00).</summary>
    public TimeOnly HoraInicio  { get; set; }
    /// <summary>Duración estimada en minutos (default 120).</summary>
    public int      DuracionMin { get; set; } = 120;

    // ── Ubicación ─────────────────────────────────────────────────────────────
    public string   Direccion   { get; set; } = string.Empty;
    public string?  Notas       { get; set; }

    // ── Estado ────────────────────────────────────────────────────────────────
    public InstallationStatus Status { get; set; } = InstallationStatus.Pendiente;

    // ── Técnico asignado ──────────────────────────────────────────────────────
    public Guid?        TecnicoId { get; set; }
    public UserSystem?  Tecnico   { get; set; }

    // ── Ticket vinculado ──────────────────────────────────────────────────────
    /// <summary>
    /// Ticket InstalacionNueva creado automáticamente al agendar.
    /// El ticket es la unidad de trabajo; la instalación es el recurso de agenda.
    /// </summary>
    public Guid?          TicketId { get; set; }
    public SupportTicket? Ticket   { get; set; }

    // ── Cancelación ───────────────────────────────────────────────────────────
    public string?  MotivoCancelacion { get; set; }
    public string?  CanceladoPor      { get; set; }   // "CLIENTE" | "ADMIN"

    // ── Auditoría ─────────────────────────────────────────────────────────────
    public DateTime  CreadoAt       { get; set; } = DateTime.UtcNow;
    public DateTime? ActualizadoAt  { get; set; }
    public Guid      CreadoPorId    { get; set; }
}
