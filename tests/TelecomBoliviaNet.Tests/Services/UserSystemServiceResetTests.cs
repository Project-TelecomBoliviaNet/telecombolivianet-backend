using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests de UserSystemService.ResetPasswordAsync.
/// Verifica la CORRECCIÓN Problema #9 (IPasswordHasher) y el flujo de recuperación.
/// </summary>
public class UserSystemServiceResetTests
{
    private readonly Mock<IGenericRepository<UserSystem>>         _userRepo     = new();
    private readonly Mock<IGenericRepository<PasswordResetToken>> _resetRepo    = new();
    private readonly Mock<AuditService>                           _audit        = new();
    private readonly Mock<IPasswordHasher>                        _hasher       = new();
    private readonly UserSystemService                            _svc;

    public UserSystemServiceResetTests()
    {
        _svc = new UserSystemService(
            _userRepo.Object,
            _resetRepo.Object,
            _audit.Object,
            NullLogger<UserSystemService>.Instance,
            _hasher.Object);
    }

    [Fact]
    public async Task ResetPassword_TokenValido_DebeActualizarPasswordYMarcarTokenUsado()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user   = new UserSystem
        {
            Id = userId, Email = "test@test.com", PasswordHash = "old_hash",
            RequiresPasswordChange = false, FailedLoginAttempts = 2
        };
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(), UserId = userId,
            Token = "abc123", ExpiresAt = DateTime.UtcNow.AddHours(1), Used = false
        };

        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken> { token }.AsQueryable());
        _userRepo.Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Hash("NuevaPass123!"))
            .Returns("new_hashed_password");

        // Act
        var result = await _svc.ResetPasswordAsync("abc123", "NuevaPass123!", "NuevaPass123!");

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("new_hashed_password");
        user.RequiresPasswordChange.Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
        token.Used.Should().BeTrue();
        _hasher.Verify(h => h.Hash("NuevaPass123!"), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_PasswordsNoCoinciden_DebeRetornarFailure()
    {
        var result = await _svc.ResetPasswordAsync("token", "Pass1!", "Pass2!");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("coinciden");
        _userRepo.Verify(r => r.UpdateAsync(It.IsAny<UserSystem>()), Times.Never);
    }

    [Fact]
    public async Task ResetPassword_PasswordCorto_DebeRetornarFailure()
    {
        var result = await _svc.ResetPasswordAsync("token", "123", "123");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("8 caracteres");
    }

    [Fact]
    public async Task ResetPassword_TokenExpirado_DebeRetornarFailure()
    {
        var token = new PasswordResetToken
        {
            Token = "expired", ExpiresAt = DateTime.UtcNow.AddHours(-2), Used = false, UserId = Guid.NewGuid()
        };

        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken> { token }.AsQueryable());

        var result = await _svc.ResetPasswordAsync("expired", "Password123!", "Password123!");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("expirado");
    }

    [Fact]
    public async Task ResetPassword_TokenYaUsado_DebeRetornarFailure()
    {
        var token = new PasswordResetToken
        {
            Token = "used_token", ExpiresAt = DateTime.UtcNow.AddHours(1), Used = true, UserId = Guid.NewGuid()
        };

        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken> { token }.AsQueryable());

        var result = await _svc.ResetPasswordAsync("used_token", "Password123!", "Password123!");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("inválido");
    }
}
