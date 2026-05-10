using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests unitarios para AuthService.
/// Cubre: Login (exitoso, credenciales incorrectas, cuenta bloqueada, bloqueo tras intentos),
/// Refresh, Logout, ChangePassword, IsTokenBlacklisted.
/// </summary>
public class AuthServiceTests
{
    private static readonly Guid UserId   = Guid.NewGuid();
    private const string ValidEmail       = "admin@telecombolivianet.bo";
    private const string ValidPassword    = "Segura123!";
    private const string HashedPassword   = "HASH_SIMULADO";

    // ── Helpers de construcción ───────────────────────────────────────────────

    private static IConfiguration MakeConfig(int maxAttempts = 5) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]                        = "SuperSecretKey1234567890_AtLeast32Chars!!",
                ["Jwt:Issuer"]                     = "TelecomBoliviaNet",
                ["Jwt:Audience"]                   = "TelecomBoliviaNet",
                ["Jwt:ExpirationHours"]            = "8",
                ["Jwt:RefreshTokenExpirationDays"] = "30",
                ["Security:MaxFailedLoginAttempts"]= maxAttempts.ToString()
            })
            .Build();

    private static JwtTokenService MakeJwt(IConfiguration? cfg = null) =>
        new JwtTokenService(cfg ?? MakeConfig(), NullLogger<JwtTokenService>.Instance);

    private static RefreshTokenService MakeRefresh(
        IConfiguration? cfg = null,
        params RefreshToken[] tokens)
    {
        var repo = RepoMock.Of(tokens);
        return new RefreshTokenService(repo.Object, cfg ?? MakeConfig(),
                                       NullLogger<RefreshTokenService>.Instance);
    }

    private static AuditService MakeAudit() =>
        new AuditService(RepoMock.Empty<AuditLog>().Object,
                         NullLogger<AuditService>.Instance);

    private static Mock<IPasswordHasher> MakeHasher(bool verifyResult = true)
    {
        var mock = new Mock<IPasswordHasher>();
        mock.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(verifyResult);
        mock.Setup(h => h.Hash(It.IsAny<string>()))
            .Returns("NUEVO_HASH");
        return mock;
    }

    private static UserSystem MakeUser(
        UserStatus status = UserStatus.Activo,
        int failedAttempts = 0,
        bool requiresChange = false) => new()
    {
        Id                    = UserId,
        Email                 = ValidEmail,
        FullName              = "Administrador",
        PasswordHash          = HashedPassword,
        Role                  = UserRole.Admin,
        Status                = status,
        FailedLoginAttempts   = failedAttempts,
        RequiresPasswordChange = requiresChange,
    };

    private AuthService MakeService(
        UserSystem? user             = null,
        IPasswordHasher? hasher      = null,
        int maxAttempts              = 5,
        params RefreshToken[] tokens)
    {
        var cfg        = MakeConfig(maxAttempts);
        var userRepo   = user is null ? RepoMock.Empty<UserSystem>() : RepoMock.Of(user);
        var tokenRepo  = RepoMock.Empty<TokenBlacklist>();
        var jwtSvc     = MakeJwt(cfg);
        var refreshSvc = MakeRefresh(cfg, tokens);
        var audit      = MakeAudit();
        var pwd        = hasher ?? MakeHasher().Object;

        return new AuthService(userRepo.Object, tokenRepo.Object,
                               jwtSvc, refreshSvc, audit, cfg, pwd);
    }

    // ── TC-AUTH-01 · Login exitoso devuelve token y datos de sesión ──────────

    [Fact]
    public async Task Login_ValidCredentials_ReturnsSuccessWithToken()
    {
        var user    = MakeUser();
        var service = MakeService(user: user, hasher: MakeHasher(true).Object);

        var result = await service.LoginAsync(
            new LoginDto(ValidEmail, ValidPassword), "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Token.Should().NotBeNullOrEmpty();
        result.Value.UserId.Should().Be(UserId.ToString());
        result.Value.Role.Should().Be("Admin");
    }

    // ── TC-AUTH-02 · Login con email desconocido → Failure ──────────────────

    [Fact]
    public async Task Login_UnknownEmail_ReturnsFail()
    {
        var service = MakeService(); // repo vacío — sin usuarios

        var result = await service.LoginAsync(
            new LoginDto("noexiste@x.bo", "cualquiera"), "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Credenciales");
    }

    // ── TC-AUTH-03 · Login con contraseña incorrecta → Failure ──────────────

    [Fact]
    public async Task Login_WrongPassword_ReturnsFail()
    {
        var user    = MakeUser();
        var service = MakeService(user: user, hasher: MakeHasher(false).Object);

        var result = await service.LoginAsync(
            new LoginDto(ValidEmail, "Incorrecta!"), "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Credenciales");
    }

    // ── TC-AUTH-04 · Cuenta bloqueada → mensaje específico ──────────────────

    [Fact]
    public async Task Login_BlockedAccount_ReturnsBloqueadoMessage()
    {
        var user    = MakeUser(status: UserStatus.Bloqueado);
        var service = MakeService(user: user);

        var result = await service.LoginAsync(
            new LoginDto(ValidEmail, ValidPassword), "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("bloqueada");
    }

    // ── TC-AUTH-05 · Cuenta inactiva → mensaje específico ───────────────────

    [Fact]
    public async Task Login_InactiveAccount_ReturnsDesactivadaMessage()
    {
        var user    = MakeUser(status: UserStatus.Inactivo);
        var service = MakeService(user: user);

        var result = await service.LoginAsync(
            new LoginDto(ValidEmail, ValidPassword), "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("desactivada");
    }

    // ── TC-AUTH-06 · Intento máximo de contraseña incorrecta bloquea cuenta ──

    [Fact]
    public async Task Login_MaxFailedAttempts_BlocksAccount()
    {
        // Usuario con 4 intentos fallidos — el 5to debe bloquear
        var user    = MakeUser(failedAttempts: 4);
        var hasher  = MakeHasher(false); // contraseña siempre incorrecta
        var userRepo = RepoMock.Of(user);

        var cfg        = MakeConfig(maxAttempts: 5);
        var tokenRepo  = RepoMock.Empty<TokenBlacklist>();
        var jwtSvc     = MakeJwt(cfg);
        var refreshSvc = MakeRefresh(cfg);
        var audit      = MakeAudit();

        var service = new AuthService(userRepo.Object, tokenRepo.Object,
                                      jwtSvc, refreshSvc, audit, cfg, hasher.Object);

        var result = await service.LoginAsync(
            new LoginDto(ValidEmail, "Mal!"), "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        // El usuario debe haberse actualizado con estado Bloqueado
        userRepo.Verify(r => r.UpdateAsync(
            It.Is<UserSystem>(u => u.Status == UserStatus.Bloqueado)),
            Times.Once);
    }

    // ── TC-AUTH-07 · Redirección por rol ─────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Admin,        "/dashboard")]
    [InlineData(UserRole.Tecnico,      "/tickets")]
    [InlineData(UserRole.SocioLectura, "/dashboard")]
    public async Task Login_ByRole_ReturnsCorrectRedirectUrl(UserRole role, string expectedUrl)
    {
        var user = new UserSystem
        {
            Id = Guid.NewGuid(), Email = ValidEmail,
            FullName = "Test", PasswordHash = HashedPassword,
            Role = role, Status = UserStatus.Activo,
        };

        var service = MakeService(user: user, hasher: MakeHasher(true).Object);

        var result = await service.LoginAsync(
            new LoginDto(ValidEmail, ValidPassword), "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        result.Value!.RedirectUrl.Should().Be(expectedUrl);
    }

    // ── TC-AUTH-08 · Login exitoso resetea intentos fallidos a cero ──────────

    [Fact]
    public async Task Login_AfterPreviousFailures_ResetsFailedAttempts()
    {
        var user     = MakeUser(failedAttempts: 3);
        var userRepo = RepoMock.Of(user);
        var hasher   = MakeHasher(true);

        var cfg       = MakeConfig();
        var tokenRepo = RepoMock.Empty<TokenBlacklist>();
        var jwtSvc    = MakeJwt(cfg);
        var refresh   = MakeRefresh(cfg);
        var audit     = MakeAudit();

        var service = new AuthService(userRepo.Object, tokenRepo.Object,
                                      jwtSvc, refresh, audit, cfg, hasher.Object);

        var result = await service.LoginAsync(
            new LoginDto(ValidEmail, ValidPassword), "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        userRepo.Verify(r => r.UpdateAsync(
            It.Is<UserSystem>(u => u.FailedLoginAttempts == 0)),
            Times.Once);
    }

    // ── TC-AUTH-09 · IsTokenBlacklisted devuelve true si el token está en lista ─

    [Fact]
    public async Task IsTokenBlacklisted_KnownToken_ReturnsTrue()
    {
        var rawToken   = "mi.jwt.token";
        var blacklisted = new TokenBlacklist
        {
            Id     = Guid.NewGuid(),
            Token  = rawToken,
            UserId = UserId,
            ExpiresAt     = DateTime.UtcNow.AddHours(8),
            InvalidatedAt = DateTime.UtcNow
        };

        var tokenRepo = RepoMock.Of(blacklisted);
        var userRepo  = RepoMock.Empty<UserSystem>();
        var cfg       = MakeConfig();

        var service = new AuthService(userRepo.Object, tokenRepo.Object,
                                      MakeJwt(cfg), MakeRefresh(cfg), MakeAudit(),
                                      cfg, MakeHasher().Object);

        var result = await service.IsTokenBlacklistedAsync(rawToken);

        result.Should().BeTrue();
    }

    // ── TC-AUTH-10 · IsTokenBlacklisted devuelve false si token no está en lista ─

    [Fact]
    public async Task IsTokenBlacklisted_UnknownToken_ReturnsFalse()
    {
        var service = MakeService(); // repo vacío

        var result = await service.IsTokenBlacklistedAsync("token.no.registrado");

        result.Should().BeFalse();
    }

    // ── TC-AUTH-11 · ChangePassword correcto actualiza hash ─────────────────

    [Fact]
    public async Task ChangePassword_ValidData_UpdatesHash()
    {
        var user     = MakeUser();
        var userRepo = RepoMock.Of(user);
        var hasher   = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify("Actual123!", HashedPassword)).Returns(true);
        hasher.Setup(h => h.Verify("Nueva456!",  HashedPassword)).Returns(false);
        hasher.Setup(h => h.Hash("Nueva456!")).Returns("NUEVO_HASH");

        var cfg     = MakeConfig();
        var service = new AuthService(userRepo.Object, RepoMock.Empty<TokenBlacklist>().Object,
                                      MakeJwt(cfg), MakeRefresh(cfg), MakeAudit(),
                                      cfg, hasher.Object);

        var result = await service.ChangePasswordAsync(UserId,
            new ChangePasswordDto("Actual123!", "Nueva456!", "Nueva456!"), "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        userRepo.Verify(r => r.UpdateAsync(
            It.Is<UserSystem>(u =>
                u.PasswordHash             == "NUEVO_HASH" &&
                u.RequiresPasswordChange   == false)),
            Times.Once);
    }

    // ── TC-AUTH-12 · ChangePassword rechaza si la contraseña actual es incorrecta ─

    [Fact]
    public async Task ChangePassword_WrongCurrent_ReturnsFail()
    {
        var user    = MakeUser();
        var hasher  = MakeHasher(false); // Verify siempre false
        var service = MakeService(user: user, hasher: hasher.Object);

        var result = await service.ChangePasswordAsync(UserId,
            new ChangePasswordDto("Incorrecta", "Nueva456!", "Nueva456!"), "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("actual");
    }

    // ── TC-AUTH-13 · ChangePassword rechaza si nueva == actual ──────────────

    [Fact]
    public async Task ChangePassword_SamePassword_ReturnsFail()
    {
        var user     = MakeUser();
        var userRepo = RepoMock.Of(user);

        // Verify devuelve true para ambas contraseñas (son iguales al hash)
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify(It.IsAny<string>(), HashedPassword)).Returns(true);

        var cfg     = MakeConfig();
        var service = new AuthService(userRepo.Object, RepoMock.Empty<TokenBlacklist>().Object,
                                      MakeJwt(cfg), MakeRefresh(cfg), MakeAudit(),
                                      cfg, hasher.Object);

        var result = await service.ChangePasswordAsync(UserId,
            new ChangePasswordDto(ValidPassword, ValidPassword, ValidPassword), "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("igual");
    }
}
