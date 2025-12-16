using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Notifications;

/// <summary>
/// Versiones archivadas de cada plantilla. Permite restaurar a cualquier versión pasada (US-37).
/// </summary>
public class NotifPlantillaHistorial : Entity
{
    public Guid      PlantillaId   { get; set; }
    public NotifType Tipo          { get; set; }
    public string    Texto         { get; set; } = string.Empty;
    public DateTime  ArchivadoAt   { get; set; } = DateTime.UtcNow;
    public Guid?     ArchivadoPorId { get; set; }
}
