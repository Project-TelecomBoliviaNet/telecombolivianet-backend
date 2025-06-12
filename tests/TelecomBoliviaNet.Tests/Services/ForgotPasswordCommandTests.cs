using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable;
using Moq;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Users.Commands;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

public class ForgotPasswordCommandTests
{
    private readonly Mock<IGenericRepository<UserSystem>>         _userRepo  = new();
    private readonly Mock<IGenericRepository<PasswordResetToken>> _resetRepo = new();
    private readonly Mock<IEmailService>                          _email     = new();
    private readonly IConfiguration                               _config;

    public ForgotPasswordCommandTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:FrontendUrl"] = "http://localhost" })
            .Build();

        _email.Setup(e => e.SendPasswordResetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
              .Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken>().AsQueryable().BuildMock());
        _resetRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.UpdateAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
    }

    private ForgotPasswordHandler MakeHandler() => new(
        _userRepo.Object, _resetRepo.Object, _email.Object, _config,
        NullLogger<ForgotPasswordHandler>.Instance);

    private static UserSystem MakeUser(UserStatus status = UserStatus.Activo, string? phone = null) => new()
    {
        Id = Guid.NewGuid(), FullName = "Test User", Email = "user@bo.com",
        PasswordHash = "hash", Role = UserRole.Operador, Status = status,
        RequiresPasswordChange = false, FailedLoginAttempts = 0,
        CreatedAt = DateTime.UtcNow, Phone = phone,
    };

    // TC-01: Email no registrado → Success genérico, IEmailService no llamado
    [Fact]
    public async Task TC01_EmailNoRegistrado_RetornaSuccessGenerico_SinEnviarEmail()
    {
        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem>().AsQueryable().BuildMock());

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand("noexiste@bo.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Channel.Should().Be("Email");
        _email.Verify(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // TC-02: Usuario inactivo → Success genérico, IEmailService no llamado
    [Fact]
    public async Task TC02_UsuarioInactivo_RetornaSuccessGenerico_SinEnviarEmail()
    {
        var user = MakeUser(status: UserStatus.Inactivo);
        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.AsQueryable().BuildMock());

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand(user.Email), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _email.Verify(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // TC-03: Usuario activo → SendPasswordResetAsync llamado con enlace que contiene el token
    [Fact]
    public async Task TC03_UsuarioActivo_LlamaEmailConEnlaceCorrecto()
    {
        var user = MakeUser();
        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.AsQueryable().BuildMock());

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand(user.Email), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Channel.Should().Be("Email");
        _email.Verify(e => e.SendPasswordResetAsync(
            user.Email,
            user.FullName,
            It.Is<string>(link => link.StartsWith("http://localhost/reset-password?token="))),
            Times.Once);
    }

    // TC-04: Token previo válido → se marca como usado antes de generar el nuevo
    [Fact]
    public async Task TC04_TokenPrevioValido_SeInvalidaAlSolicitarNuevoReset()
    {
        var user = MakeUser();
        var oldToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(), UserId = user.Id, Token = "old_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1), Used = false,
        };

        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.AsQueryable().BuildMock());
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken> { oldToken }.AsQueryable().BuildMock());

        await MakeHandler().Handle(new ForgotPasswordCommand(user.Email), CancellationToken.None);

        oldToken.Used.Should().BeTrue();
        _resetRepo.Verify(r => r.UpdateAsync(It.Is<PasswordResetToken>(t => t.Id == oldToken.Id)), Times.Once);
    }

    // TC-05: Usuario con teléfono → canal siempre es Email (no WhatsApp)
    [Fact]
    public async Task TC05_UsuarioConPhone_CanalSiempreEsEmail()
    {
        var user = MakeUser(phone: "59170000000");
        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.AsQueryable().BuildMock());

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand(user.Email), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Channel.Should().Be("Email");
        _email.Verify(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    // TC-06: IEmailService lanza excepción → comando retorna Success (no expone el error al usuario)
    [Fact]
    public async Task TC06_EmailServiceLanzaExcepcion_ComandoRetornaSuccess()
    {
        var user = MakeUser();
        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.AsQueryable().BuildMock());
        _email.Setup(e => e.SendPasswordResetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
              .ThrowsAsync(new HttpRequestException("Resend API unreachable"));

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand(user.Email), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
