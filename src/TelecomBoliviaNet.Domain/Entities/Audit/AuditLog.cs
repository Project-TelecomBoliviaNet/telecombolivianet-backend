using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Audit;

/// <summary>
/// Registro inmutable de todas las acciones del sistema.
/// Nunca se elimina ni modifica desde la aplicación.
/// </summary>
public class AuditLog : Entity
{
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "Sistema";
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>
    /// Identificador de la entidad afectada (cliente, ticket, etc.).
    /// BUG FIX: campo indexado para reemplazar búsqueda con Description.Contains.
    /// </summary>
    public Guid? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public string? PreviousData { get; set; }
    public string? NewData { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
