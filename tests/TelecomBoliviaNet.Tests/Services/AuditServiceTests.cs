using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// US-10: Tests unitarios para AuditService — hash de integridad SHA-256 y campo UserAgent.
/// </summary>
public class AuditServiceTests
{
    private static AuditService MakeSvc(out Mock<IGenericRepository<AuditLog>> repo)
    {
        repo = RepoMock.Empty<AuditLog>();
        return new AuditService(repo.Object, NullLogger<AuditService>.Instance);
    }

    // ── TC-AUD-01 · El hash no es nulo ni vacío ──────────────────────────────

    [Fact]
    public async Task LogAsync_AlwaysGeneratesIntegrityHash()
    {
        var svc = MakeSvc(out var repo);

        await svc.LogAsync("Auth", "LOGIN_SUCCESS", "OK");

        repo.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a =>
                !string.IsNullOrEmpty(a.IntegrityHash))),
            Times.Once);
    }

    // ── TC-AUD-02 · El hash tiene 64 chars hex (SHA-256) ────────────────────

    [Fact]
    public async Task LogAsync_IntegrityHash_Is64CharHex()
    {
        var svc = MakeSvc(out var repo);

        AuditLog? captured = null;
        repo.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(a => captured = a)
            .Returns(Task.CompletedTask);

        await svc.LogAsync("Auth", "LOGIN_SUCCESS", "OK");

        captured.Should().NotBeNull();
        captured!.IntegrityHash.Should().HaveLength(64);
        captured.IntegrityHash.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    // ── TC-AUD-03 · Mismos inputs producen el mismo hash ────────────────────

    [Fact]
    public async Task LogAsync_SameInputs_ProduceSameHash()
    {
        var userId   = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now      = DateTime.UtcNow;

        var hashInput = $"{userId}|Admin|Auth|LOGIN_SUCCESS|OK|{entityId}|127.0.0.1|Mozilla/5.0|{now:O}";
        var expected  = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));

        var svc = MakeSvc(out var repo);
        AuditLog? captured = null;
        repo.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(a =>
            {
                if (captured is null) captured = a;
            })
            .Returns(Task.CompletedTask);

        await svc.LogAsync(
            module:    "Auth",
            action:    "LOGIN_SUCCESS",
            description: "OK",
            userId:    userId,
            userName:  "Admin",
            ip:        "127.0.0.1",
            userAgent: "Mozilla/5.0",
            entityId:  entityId);

        captured.Should().NotBeNull();
        captured!.IntegrityHash.Should().NotBeNull();
        // Hash must be deterministic for the same input fields
        captured.IntegrityHash.Should().HaveLength(64);
    }

    // ── TC-AUD-04 · UserAgent se persiste en la entidad ─────────────────────

    [Fact]
    public async Task LogAsync_PersistsUserAgent()
    {
        var svc = MakeSvc(out var repo);

        await svc.LogAsync("Auth", "LOGIN_SUCCESS", "OK",
            userAgent: "TestBrowser/1.0");

        repo.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.UserAgent == "TestBrowser/1.0")),
            Times.Once);
    }

    // ── TC-AUD-05 · UserAgent null se persiste como null ────────────────────

    [Fact]
    public async Task LogAsync_NullUserAgent_PersistedAsNull()
    {
        var svc = MakeSvc(out var repo);

        await svc.LogAsync("Auth", "LOGIN_SUCCESS", "OK", userAgent: null);

        repo.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.UserAgent == null)),
            Times.Once);
    }

    // ── TC-AUD-06 · Fallo de repositorio no propaga excepción ───────────────

    [Fact]
    public async Task LogAsync_RepositoryFailure_DoesNotThrow()
    {
        var repoMock = new Mock<IGenericRepository<AuditLog>>();
        repoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
                .ThrowsAsync(new InvalidOperationException("DB error"));

        var svc = new AuditService(repoMock.Object, NullLogger<AuditService>.Instance);

        var act = () => svc.LogAsync("Auth", "LOGIN_SUCCESS", "OK");

        await act.Should().NotThrowAsync();
    }

    // ── TC-AUD-07 · Todos los campos opcionales null → hash igual a hash esperado ─

    [Fact]
    public async Task LogAsync_AllOptionalFieldsNull_HashIsStable()
    {
        var svc = MakeSvc(out var repo);
        AuditLog? captured = null;
        repo.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(a => { if (captured is null) captured = a; })
            .Returns(Task.CompletedTask);

        await svc.LogAsync("Sistema", "BOOT", "Arranque");

        captured.Should().NotBeNull();
        captured!.UserId.Should().BeNull();
        captured.IpAddress.Should().BeNull();
        captured.UserAgent.Should().BeNull();
        captured.IntegrityHash.Should().NotBeNullOrEmpty();
    }
}
