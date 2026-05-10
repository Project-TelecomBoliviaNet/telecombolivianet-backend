using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Notifications;

/// <summary>
/// US-NOT-02 — Segmento de destinatarios con condiciones AND/OR sobre variables del cliente.
/// </summary>
public class NotifSegment : Entity
{
    public string   Nombre      { get; set; } = string.Empty;
    public string?  Descripcion { get; set; }
    /// <summary>JSON serializado de List&lt;SegmentConditionGroup&gt; (grupos OR de condiciones AND)</summary>
    public string   ReglasJson  { get; set; } = "[]";
    public DateTime CreadoAt    { get; set; } = DateTime.UtcNow;
    public Guid?    CreadoPorId { get; set; }
    public DateTime? ActualizadoAt   { get; set; }
    public Guid?     ActualizadoPorId { get; set; }
}
