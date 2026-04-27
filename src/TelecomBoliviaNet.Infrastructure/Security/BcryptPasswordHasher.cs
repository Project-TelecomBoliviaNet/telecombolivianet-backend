using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Infrastructure.Security;

/// <summary>
/// CORRECCIÓN Problema #9: implementación de IPasswordHasher con BCrypt.
/// Work factor 12: balance recomendado en 2026.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string plainPassword)
        => BCrypt.Net.BCrypt.HashPassword(plainPassword, WorkFactor);

    public bool Verify(string plainPassword, string hash)
    {
        try   { return BCrypt.Net.BCrypt.Verify(plainPassword, hash); }
        catch { return false; }
    }
}
