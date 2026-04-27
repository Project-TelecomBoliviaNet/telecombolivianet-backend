using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests unitarios para RefreshTokenService.
/// Cubre: generación, rotación, detección de reutilización y expiración.
/// </summary>
public class RefreshTokenServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (RefreshTokenService svc, Mock<IGenericRepository<RefreshToken>> repo)
        MakeService(params RefreshToken[] tokens)
    {
        var repo   = RepoMock.Of(tokens);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenExpirationDays"] = "30"
            })
            .Build();

        var svc = new RefreshTokenService(
            repo.Object, config, NullLogger<RefreshTokenService>.Instance);

        return (svc, repo);
    }

    // ── Test 1: GenerateAsync crea token con hash y TTL correcto ─────────────

    [Fact]
    public async Task Generate_CreatesTokenWithHashAndExpiry()
    {
        var (svc, repo) = MakeService();

        var rawToken = await svc.GenerateAsync(UserId, "127.0.0.1");

        rawToken.Should().NotBeNullOrEmpty();
        rawToken.Length.Should().BeGreaterThan(20); // token opaco 256 bits en base64

        repo.Verify(r => r.AddAsync(It.Is<RefreshToken>(t =>
            t.UserId     == UserId &&
            t.TokenHash  == RefreshTokenService.ComputeHash(rawToken) &&
            t.RevokedAt  == null &&
            t.ExpiresAt  > DateTime.UtcNow.AddDays(29) &&
            t.ExpiresAt  < DateTime.UtcNow.AddDays(31)
        )), Times.Once);
    }

    // ── Test 2: tokens distintos producen hashes distintos ───────────────────

    [Fact]
    public async Task Generate_TwoTokens_HaveDifferentHashes()
    {
        var (svc, _) = MakeService();

        var token1 = await svc.GenerateAsync(UserId, "127.0.0.1");
        var token2 = await svc.GenerateAsync(UserId, "127.0.0.1");

        token1.Should().NotBe(token2);
        RefreshTokenService.ComputeHash(token1)
            .Should().NotBe(RefreshTokenService.ComputeHash(token2));
    }

    // ── Test 3: Rotate rota el token activo y genera uno nuevo ───────────────

    [Fact]
    public async Task Rotate_ActiveToken_RevokesOldAndReturnsNew()
    {
        var rawToken  = "valid_test_token_abc";
        var tokenHash = RefreshTokenService.ComputeHash(rawToken);

        var stored = new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = UserId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(20),
            RevokedAt = null,
        };

        var (svc, repo) = MakeService(stored);

        var (entity, newRaw) = await svc.RotateAsync(rawToken, "127.0.0.1");

        entity.Should().NotBeNull();
        newRaw.Should().NotBeNullOrEmpty();
        newRaw.Should().NotBe(rawToken);

        // El token anterior debe haber sido revocado
        repo.Verify(r => r.UpdateAsync(It.Is<RefreshToken>(t =>
            t.Id         == stored.Id &&
            t.RevokedAt  != null
        )), Times.Once);

        // Un nuevo token debe haberse creado
        repo.Verify(r => r.AddAsync(It.Is<RefreshToken>(t =>
            t.UserId    == UserId &&
            t.TokenHash == RefreshTokenService.ComputeHash(newRaw!)
        )), Times.Once);
    }

    // ── Test 4: token no encontrado devuelve null ─────────────────────────────

    [Fact]
    public async Task Rotate_UnknownToken_ReturnsNull()
    {
        var (svc, _) = MakeService(); // repo vacío

        var (entity, newRaw) = await svc.RotateAsync("token_inexistente", "127.0.0.1");

        entity.Should().BeNull();
        newRaw.Should().BeNull();
    }

    // ── Test 5: token ya revocado → revoca toda la sesión ────────────────────

    [Fact]
    public async Task Rotate_RevokedToken_RevokesAllUserTokens()
    {
        var rawToken  = "reused_revoked_token";
        var tokenHash = RefreshTokenService.ComputeHash(rawToken);

        var revokedToken = new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = UserId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(20),
            RevokedAt = DateTime.UtcNow.AddHours(-1), // ya fue revocado
        };

        var activeToken = new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = UserId,
            TokenHash = RefreshTokenService.ComputeHash("otro_token_activo"),
            ExpiresAt = DateTime.UtcNow.AddDays(25),
            RevokedAt = null, // activo
        };

        var (svc, repo) = MakeService(revokedToken, activeToken);

        var (entity, newRaw) = await svc.RotateAsync(rawToken, "127.0.0.1");

        // Debe devolver null — sesión invalidada
        entity.Should().BeNull();
        newRaw.Should().BeNull();

        // El token activo debe haber sido revocado también (toda la sesión)
        repo.Verify(r => r.UpdateAsync(It.Is<RefreshToken>(t =>
            t.Id == activeToken.Id && t.RevokedAt != null
        )), Times.Once);
    }

    // ── Test 6: token expirado devuelve null ─────────────────────────────────

    [Fact]
    public async Task Rotate_ExpiredToken_ReturnsNull()
    {
        var rawToken  = "expired_token";
        var tokenHash = RefreshTokenService.ComputeHash(rawToken);

        var expired = new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = UserId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // expirado ayer
            RevokedAt = null,
        };

        var (svc, _) = MakeService(expired);

        var (entity, newRaw) = await svc.RotateAsync(rawToken, "127.0.0.1");

        entity.Should().BeNull();
        newRaw.Should().BeNull();
    }

    // ── Test 7: ComputeHash produce el mismo hash para el mismo input ─────────

    [Fact]
    public void ComputeHash_SameInput_ProducesSameHash()
    {
        var token = "deterministic_token_input";

        var hash1 = RefreshTokenService.ComputeHash(token);
        var hash2 = RefreshTokenService.ComputeHash(token);

        hash1.Should().Be(hash2);
        hash1.Length.Should().Be(64); // SHA-256 = 64 hex chars
    }
}
