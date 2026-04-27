using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Auth;

/// <summary>
/// Gestiona el ciclo de vida de los refresh tokens.
///
/// Estrategia de seguridad:
///  - Tokens opacos de 256 bits (32 bytes random) — nunca predecibles.
///  - Solo el hash SHA-256 se almacena en BD — si la BD se compromete,
///    los tokens en texto claro no son recuperables.
///  - Rotación en cada uso: el token anterior se revoca, se genera uno nuevo.
///  - Detección de reutilización: si se usa un token ya revocado, se invalida
///    toda la sesión (indica posible robo de token).
///  - Limpieza periódica de tokens expirados para evitar crecimiento de tabla.
/// </summary>
public class RefreshTokenService
{
    private readonly IGenericRepository<RefreshToken> _tokenRepo;
    private readonly IConfiguration                   _config;
    private readonly ILogger<RefreshTokenService>     _logger;

    private int ExpirationDays =>
        int.Parse(_config["Jwt:RefreshTokenExpirationDays"] ?? "30");

    public RefreshTokenService(
        IGenericRepository<RefreshToken> tokenRepo,
        IConfiguration                   config,
        ILogger<RefreshTokenService>     logger)
    {
        _tokenRepo = tokenRepo;
        _config    = config;
        _logger    = logger;
    }

    // ── Generar nuevo refresh token ───────────────────────────────────────────

    /// <summary>
    /// Genera un nuevo refresh token opaco para el usuario.
    /// Devuelve el valor en texto claro (solo se envía una vez al cliente).
    /// </summary>
    public async Task<string> GenerateAsync(Guid userId, string? clientIp)
    {
        var rawToken  = GenerateOpaqueToken();
        var tokenHash = ComputeHash(rawToken);

        var entity = new RefreshToken
        {
            TokenHash    = tokenHash,
            UserId       = userId,
            ExpiresAt    = DateTime.UtcNow.AddDays(ExpirationDays),
            CreatedByIp  = clientIp,
            CreatedAt    = DateTime.UtcNow,
        };

        await _tokenRepo.AddAsync(entity);
        await _tokenRepo.SaveChangesAsync();

        _logger.LogDebug("RefreshToken generado para usuario {UserId}, expira {Expiry}",
            userId, entity.ExpiresAt);

        return rawToken;
    }

    // ── Validar y rotar ───────────────────────────────────────────────────────

    /// <summary>
    /// Valida el token recibido del cliente y lo rota:
    ///  - Si es válido: revoca el anterior, genera uno nuevo.
    ///  - Si fue ya revocado (reutilización): revoca TODOS los tokens del usuario.
    ///  - Si expiró o no existe: retorna null.
    /// </summary>
    public async Task<(RefreshToken? entity, string? newRawToken)> RotateAsync(
        string rawToken, string? clientIp)
    {
        var hash   = ComputeHash(rawToken);
        var stored = await _tokenRepo.GetAll()
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (stored is null)
        {
            _logger.LogWarning("RefreshToken no encontrado. Hash: {Hash}", hash[..8] + "...");
            return (null, null);
        }

        // ── Detección de reutilización ────────────────────────────────────────
        if (stored.IsRevoked)
        {
            _logger.LogWarning(
                "RefreshToken reutilizado detectado para usuario {UserId}. " +
                "Revocando toda la sesión.", stored.UserId);

            await RevokeAllForUserAsync(stored.UserId,
                "Reutilización de refresh token detectada — posible robo de token.");
            return (null, null);
        }

        if (stored.IsExpired)
        {
            _logger.LogInformation("RefreshToken expirado para usuario {UserId}", stored.UserId);
            return (null, null);
        }

        // ── Rotación: revocar el actual, generar uno nuevo ────────────────────
        var newRawToken  = GenerateOpaqueToken();
        var newTokenHash = ComputeHash(newRawToken);

        stored.RevokedAt           = DateTime.UtcNow;
        stored.ReplacedByTokenHash = newTokenHash;
        await _tokenRepo.UpdateAsync(stored);

        var newEntity = new RefreshToken
        {
            TokenHash   = newTokenHash,
            UserId      = stored.UserId,
            ExpiresAt   = DateTime.UtcNow.AddDays(ExpirationDays),
            CreatedByIp = clientIp,
            CreatedAt   = DateTime.UtcNow,
        };
        await _tokenRepo.AddAsync(newEntity);
        await _tokenRepo.SaveChangesAsync();

        _logger.LogDebug("RefreshToken rotado para usuario {UserId}", stored.UserId);
        return (stored, newRawToken);
    }

    // ── Revocar todos los tokens del usuario (logout) ─────────────────────────

    // CORRECCIÓN (Fix #9): Reemplaza N UpdateAsync individuales por UpdateRangeAsync.
    // Con 10 tokens activos, antes eran 30 queries (FindAsync + SetValues + SaveChanges × 10).
    // Ahora son 2: 1 SELECT + 1 UPDATE batch.
    public async Task RevokeAllForUserAsync(Guid userId, string reason = "Logout")
    {
        var activeTokens = await _tokenRepo.GetAll()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync();

        if (activeTokens.Count == 0) return;

        foreach (var token in activeTokens)
            token.RevokedAt = DateTime.UtcNow;

        await _tokenRepo.UpdateRangeAsync(activeTokens);

        _logger.LogInformation(
            "RefreshTokens revocados para usuario {UserId}: {Count} tokens. Razón: {Reason}",
            userId, activeTokens.Count, reason);
    }

    // ── Limpieza de tokens expirados (llamar desde job periódico) ─────────────

    public async Task<int> PurgeExpiredAsync()
    {
        var cutoff  = DateTime.UtcNow.AddDays(-1); // conservar 1 día extra para logs
        var expired = await _tokenRepo.GetAll()
            .Where(t => t.ExpiresAt < cutoff)
            .ToListAsync();

        // BUG FIX: un único DeleteRangeAsync en lugar de N queries DELETE individuales
        if (expired.Count > 0)
            await _tokenRepo.DeleteRangeAsync(expired);

        if (expired.Count > 0)
            _logger.LogInformation("RefreshTokens expirados eliminados: {Count}", expired.Count);

        return expired.Count;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Genera 32 bytes aleatorios criptográficamente seguros en Base64-URL.</summary>
    private static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>Hash SHA-256 del token en hexadecimal (64 chars).</summary>
    public static string ComputeHash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
