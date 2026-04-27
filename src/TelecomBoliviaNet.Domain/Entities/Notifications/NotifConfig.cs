using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Notifications;

/// <summary>
/// Tipos de notificación predefinidos en código (US-35).
/// US-NOT-04: agrega TICKET_CREADO, TICKET_RESUELTO, CAMBIO_PLAN.
/// </summary>
public enum NotifType
{
    SUSPENSION,
    REACTIVACION,
    RECORDATORIO_R1,
    RECORDATORIO_R2,
    RECORDATORIO_R3,
    FACTURA_VENCIDA,
    CONFIRMACION_PAGO,
    /// <summary>Notificación al técnico cuando se le asigna o reasigna un ticket.</summary>
    TICKET_ASIGNADO,
    /// <summary>US-NOT-04 — Notificación al cliente cuando se crea un ticket.</summary>
    TICKET_CREADO,
    /// <summary>US-NOT-04 — Notificación al cliente cuando su ticket es resuelto.</summary>
    TICKET_RESUELTO,
    /// <summary>US-NOT-04 — Notificación al cliente cuando su cambio de plan es efectivo.</summary>
    CAMBIO_PLAN
}

/// <summary>
/// Configuración por tipo de notificación. Una fila por tipo (US-35, US-38).
/// El Worker y el módulo central leen y escriben esta tabla.
/// US-NOT-04: agrega PlantillaId (FK editable desde la UI de triggers).
/// </summary>
public class NotifConfig : Entity
{
    public NotifType  Tipo            { get; set; }
    public bool       Activo          { get; set; } = true;
    public int        DelaySegundos   { get; set; } = 0;
    public TimeOnly   HoraInicio      { get; set; } = new TimeOnly(8, 0);
    public TimeOnly   HoraFin         { get; set; } = new TimeOnly(20, 0);
    /// <summary>Si true, ignora la ventana horaria (solo CONFIRMACION_PAGO por defecto).</summary>
    public bool       Inmediato       { get; set; } = false;
    /// <summary>Solo para RECORDATORIO_Rx: días antes del vencimiento.</summary>
    public int?       DiasAntes       { get; set; }
    /// <summary>US-NOT-04 — ID de plantilla asociada a este trigger (editable desde UI).</summary>
    public Guid?      PlantillaId     { get; set; }
    public DateTime   ActualizadoAt   { get; set; } = DateTime.UtcNow;
    public Guid?      ActualizadoPorId { get; set; }
}
