using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Auth;

/// <summary>
/// Token de activación de cuenta enviado por email al crear un usuario.
/// UUID opaco de un solo uso con TTL de 48 horas.
/// </summary>
public class EmailActivationToken : Entity
{
    public Guid     UserId    { get; set; }
    public string   Token     { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool     Used      { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}
