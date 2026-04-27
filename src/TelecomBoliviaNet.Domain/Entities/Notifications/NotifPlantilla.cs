using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Notifications;

/// <summary>
/// US-NOT-03 — Categorías de plantilla WhatsApp.
/// </summary>
public enum PlantillaCategoria
{
    Cobro,
    Bienvenida,
    Tecnico,
    Ticket,
    General
}

/// <summary>
/// US-NOT-03 — Estado de aprobación HSM de Meta.
/// </summary>
public enum HsmStatus
{
    Aprobada,
    Pendiente,
    Rechazada
}

/// <summary>
/// Plantilla de mensaje activa por tipo. El Worker lee la activa en tiempo de ejecución (US-37).
/// Solo una fila activa por tipo en cada momento.
/// US-NOT-03: agrega Categoria y HsmStatus.
/// </summary>
public class NotifPlantilla : Entity
{
    public NotifType          Tipo         { get; set; }
    public string             Texto        { get; set; } = string.Empty;
    public bool               Activa       { get; set; } = true;
    public PlantillaCategoria Categoria    { get; set; } = PlantillaCategoria.General;
    public HsmStatus          HsmStatus    { get; set; } = HsmStatus.Aprobada;
    public DateTime           CreadoAt     { get; set; } = DateTime.UtcNow;
    public Guid?              CreadoPorId  { get; set; }
}
