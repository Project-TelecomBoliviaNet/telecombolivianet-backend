using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Auth;

/// <summary>
/// US-USR-RECOVERY — Token de recuperación de contraseña.
/// El token es un UUID opaco de un solo uso con TTL de 1 hora.
/// Se envía por WhatsApp (si el usuario tiene Phone) o email (si tiene Email en Client).
/// </summary>
public class PasswordResetToken : Entity
{
    public Guid      UserId     { get; set; }
    public string    Token      { get; set; } = string.Empty;   // UUID opaco
    public DateTime  ExpiresAt  { get; set; }
    public bool      Used       { get; set; } = false;
    public DateTime  CreatedAt  { get; set; } = DateTime.UtcNow;
    public string?   SentTo     { get; set; }  // email o teléfono donde se envió
    public string    Channel    { get; set; } = "WhatsApp"; // WhatsApp | Email

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}
