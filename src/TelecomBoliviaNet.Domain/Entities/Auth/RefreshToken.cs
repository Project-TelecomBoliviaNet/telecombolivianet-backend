using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Auth;

/// <summary>
/// Token de renovación JWT. Ciclo de vida:
///   1. Se genera al hacer login (opaco, 256 bits, almacenado hasheado en BD).
///   2. El cliente lo envía a POST /api/auth/refresh para obtener un nuevo access token.
///   3. Cada uso rota el refresh token (revoca el anterior, genera uno nuevo).
///   4. Expira a los RefreshTokenExpirationDays días (configurable, default 30).
///   5. Si se detecta reutilización de un token ya revocado, se invalida toda la familia.
/// </summary>
public class RefreshToken : Entity
{
    /// <summary>Hash SHA-256 del token opaco. Nunca almacenar el valor en claro.</summary>
    public string TokenHash   { get; set; } = string.Empty;

    public Guid   UserId      { get; set; }

    /// <summary>Fecha de expiración absoluta.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Fecha en que fue usado/revocado. Null = activo.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Token que lo reemplazó (para detección de reutilización).</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>IP del cliente que lo generó (trazabilidad).</summary>
    public string? CreatedByIp { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Propiedades computadas ────────────────────────────────────────────────

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive  => !IsRevoked && !IsExpired;
}
