using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Notifications;

public enum NotifLogEstado
{
    ENVIADO,
    FALLIDO,
    CANCELADO,
    OMITIDO,
    STUB
}

/// <summary>
/// Historial permanente de resultados de notificaciones. Nunca se borra. Visible en US-36.
/// </summary>
public class NotifLog : Entity
{
    public Guid             OutboxId      { get; set; }
    public Guid             ClienteId     { get; set; }
    public Client?          Cliente       { get; set; }
    public NotifType        Tipo          { get; set; }
    public string           PhoneNumber   { get; set; } = string.Empty;
    public string           Mensaje       { get; set; } = string.Empty;
    public NotifLogEstado   Estado        { get; set; }
    public int              IntentoNum    { get; set; }
    public string?          ErrorDetalle  { get; set; }
    public DateTime         RegistradoAt  { get; set; } = DateTime.UtcNow;
}
