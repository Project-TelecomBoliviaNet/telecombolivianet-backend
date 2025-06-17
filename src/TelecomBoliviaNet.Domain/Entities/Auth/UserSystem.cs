using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Auth;

public class UserSystem : Entity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>Número WhatsApp del técnico/admin para notificaciones internas (opcional).</summary>
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Activo;
    public bool RequiresPasswordChange { get; set; } = true;
    // US-USR-01: baja lógica de usuario
    public bool      IsDeleted  { get; set; } = false;
    public DateTime? DeletedAt  { get; set; }
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
