using FluentAssertions;
using MockQueryable;
using Moq;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Users.Commands;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

public class UserSystemServiceResetTests
{
    private readonly Mock<IGenericRepository<UserSystem>>         _userRepo  = new();
    private readonly Mock<IGenericRepository<PasswordResetToken>> _resetRepo = new();
    private readonly Mock<IPasswordHasher>                        _hasher    = new();
    private readonly Mock<IEmailService>                          _email     = new();

    private ResetPasswordHandler MakeHandler() =>
        new(_userRepo.Object, _resetRepo.Object, _hasher.Object, _email.Object);

    // ── TC-RP-01 · Token válido → contraseña actualizada y notificación enviada ─

    [Fact]
    public async Task ResetPassword_TokenValido_DebeActualizarPasswordYEnviarNotificacion()
    {
        var userId = Guid.NewGuid();
        var user   = new UserSystem
        {
            Id = userId, Email = "test@test.com", FullName = "Test User",
            PasswordHash = "old_hash", RequiresPasswordChange = false, FailedLoginAttempts = 2
        };
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(), UserId = userId,
            Token = "abc123", ExpiresAt = DateTime.UtcNow.AddHours(1), Used = false
        };

        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken> { token }.BuildMock());
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _userRepo.Setup(r => r.UpdateAsync(It.IsAny<UserSystem>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.UpdateAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _hasher.Setup(h => h.Hash("NuevaPass123!")).Returns("new_hashed_password");
        _email.Setup(e => e.SendPasswordChangedNotificationAsync(
            It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var result = await MakeHandler().Handle(
            new ResetPasswordCommand("abc123", "NuevaPass123!", "NuevaPass123!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("new_hashed_password");
        user.RequiresPasswordChange.Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
        token.Used.Should().BeTrue();
        _hasher.Verify(h => h.Hash("NuevaPass123!"), Times.Once);
        _email.Verify(e => e.SendPasswordChangedNotificationAsync(
            "test@test.com", "Test User"), Times.Once);
    }

    // ── TC-RP-02 · Contraseñas distintas → fallo antes de tocar BD ───────────

    [Fact]
    public async Task ResetPassword_PasswordsNoCoinciden_DebeRetornarFailure()
    {
        var result = await MakeHandler().Handle(
            new ResetPasswordCommand("token", "Pass1!", "Pass2!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("coinciden");
        _userRepo.Verify(r => r.UpdateAsync(It.IsAny<UserSystem>()), Times.Never);
        _email.Verify(e => e.SendPasswordChangedNotificationAsync(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── TC-RP-03 · Contraseña corta → fallo de validación ────────────────────

    [Fact]
    public async Task ResetPassword_PasswordCorto_DebeRetornarFailure()
    {
        var result = await MakeHandler().Handle(
            new ResetPasswordCommand("token", "123", "123"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("8 caracteres");
    }

    // ── TC-RP-04 · Token expirado → error, sin actualizar contraseña ──────────

    [Fact]
    public async Task ResetPassword_TokenExpirado_DebeRetornarFailure()
    {
        var token = new PasswordResetToken
        {
            Token = "expired", ExpiresAt = DateTime.UtcNow.AddHours(-2), Used = false,
            UserId = Guid.NewGuid()
        };
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken> { token }.BuildMock());

        var result = await MakeHandler().Handle(
            new ResetPasswordCommand("expired", "Password123!", "Password123!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("expirado");
        _email.Verify(e => e.SendPasswordChangedNotificationAsync(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── TC-RP-05 · Token ya usado → error ────────────────────────────────────

    [Fact]
    public async Task ResetPassword_TokenYaUsado_DebeRetornarFailure()
    {
        var token = new PasswordResetToken
        {
            Token = "used_token", ExpiresAt = DateTime.UtcNow.AddHours(1), Used = true,
            UserId = Guid.NewGuid()
        };
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken> { token }.BuildMock());

        var result = await MakeHandler().Handle(
            new ResetPasswordCommand("used_token", "Password123!", "Password123!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("inválido");
    }

    // ── TC-RP-06 · Token inexistente → error genérico ─────────────────────────

    [Fact]
    public async Task ResetPassword_TokenInexistente_DebeRetornarFailure()
    {
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken>().BuildMock());

        var result = await MakeHandler().Handle(
            new ResetPasswordCommand("token_falso", "Password123!", "Password123!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _userRepo.Verify(r => r.UpdateAsync(It.IsAny<UserSystem>()), Times.Never);
    }

    // ── TC-RP-07 · Reset exitoso resetea intentos fallidos a 0 ───────────────

    [Fact]
    public async Task ResetPassword_TokenValido_ResetaIntentosFallidosACero()
    {
        var userId = Guid.NewGuid();
        var user   = new UserSystem
        {
            Id = userId, Email = "bloq@test.com", FullName = "Usuario Bloqueado",
            PasswordHash = "old", FailedLoginAttempts = 5
        };
        var token = new PasswordResetToken
        {
            UserId = userId, Token = "tok", ExpiresAt = DateTime.UtcNow.AddHours(1), Used = false
        };

        _resetRepo.Setup(r => r.GetAll()).Returns(new List<PasswordResetToken> { token }.BuildMock());
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _userRepo.Setup(r => r.UpdateAsync(It.IsAny<UserSystem>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.UpdateAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        _email.Setup(e => e.SendPasswordChangedNotificationAsync(
            It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        await MakeHandler().Handle(
            new ResetPasswordCommand("tok", "Password123!", "Password123!"),
            CancellationToken.None);

        user.FailedLoginAttempts.Should().Be(0);
    }
}
