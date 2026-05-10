using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Auth;

/// <summary>
/// Almacena tokens JWT que han sido invalidados por logout manual.
/// Permite implementar logout real en un esquema stateless.
/// </summary>
public class TokenBlacklist : Entity
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public DateTime InvalidatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
