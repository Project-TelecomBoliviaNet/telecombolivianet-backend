using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable;
using Moq;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Users.Commands;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

public class ForgotPasswordHandlerTests
{
    // ── Constantes de fixtures ────────────────────────────────────────────────
    private const string ValidEmail    = "tecnico@telecom.bo";
    private const string ValidFullName = "Juan Pérez";
    private const string FrontendUrl   = "http://localhost:5173";

    // ── Mocks compartidos ─────────────────────────────────────────────────────
    private readonly Mock<IGenericRepository<UserSystem>>         _userRepo  = new();
    private readonly Mock<IGenericRepository<PasswordResetToken>> _resetRepo = new();
    private readonly Mock<IEmailService>                          _email     = new();

    private ForgotPasswordHandler MakeHandler(string? frontendUrl = FrontendUrl)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:FrontendUrl"] = frontendUrl,
            })
            .Build();

        return new ForgotPasswordHandler(
            _userRepo.Object,
            _resetRepo.Object,
            _email.Object,
            config,
            NullLogger<ForgotPasswordHandler>.Instance);
    }

    private static UserSystem MakeUser(
        string email      = ValidEmail,
        string name       = ValidFullName,
        UserStatus status = UserStatus.Activo,
        string? phone     = null) => new()
    {
        Id       = Guid.NewGuid(),
        Email    = email,
        FullName = name,
        Status   = status,
        Phone    = phone,
    };

    private static PasswordResetToken MakeToken(Guid userId, bool used = false, int hoursOffset = 1) => new()
    {
        Id        = Guid.NewGuid(),
        UserId    = userId,
        Token     = Guid.NewGuid().ToString("N"),
        ExpiresAt = DateTime.UtcNow.AddHours(hoursOffset),
        Used      = used,
    };

    // ── TC-FP-01 · Email existente → token generado y email enviado ───────────

    [Fact]
    public async Task Handle_UsuarioActivoSinPhone_DebeGenerarTokenYEnviarEmail()
    {
        var user  = MakeUser();
        var token = MakeToken(user.Id);

        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.BuildMock());
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken>().BuildMock());
        _resetRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>()))
            .Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);
        _email.Setup(e => e.SendPasswordResetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand(ValidEmail), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Channel.Should().Be("Email");
        result.Value.SentTo.Should().NotBeNullOrEmpty();

        // El email siempre se envía por email (canal único para reset)
        _email.Verify(e => e.SendPasswordResetAsync(
            ValidEmail,
            ValidFullName,
            It.Is<string>(url => url.Contains("/reset-password?token="))),
            Times.Once);
    }

    // ── TC-FP-02 · Email existente CON phone → canal sigue siendo Email ───────

    [Fact]
    public async Task Handle_UsuarioActivoConPhone_CanalSigueEmailNoWhatsApp()
    {
        var user = MakeUser(phone: "59170123456");

        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.BuildMock());
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken>().BuildMock());
        _resetRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        _email.Setup(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand(ValidEmail), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Channel.Should().Be("Email");
        _email.Verify(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    // ── TC-FP-03 · Email no registrado → respuesta genérica, sin email enviado ─

    [Fact]
    public async Task Handle_EmailNoRegistrado_RetornaRespuestaGenericaSinEnviarEmail()
    {
        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem>().BuildMock());

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand("noexiste@x.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Message.Should().Contain("Si el correo existe");

        // Anti-enumeración: no se llama al servicio de email
        _email.Verify(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _resetRepo.Verify(r => r.AddAsync(It.IsAny<PasswordResetToken>()), Times.Never);
    }

    // ── TC-FP-04 · Usuario inactivo → respuesta genérica, sin email enviado ───

    [Fact]
    public async Task Handle_UsuarioInactivo_RetornaRespuestaGenericaSinEnviarEmail()
    {
        var inactivo = MakeUser(status: UserStatus.Inactivo);

        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { inactivo }.BuildMock());

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand(ValidEmail), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _email.Verify(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── TC-FP-05 · Token previo válido se invalida antes de generar uno nuevo ─

    [Fact]
    public async Task Handle_TokenPrevioValido_DebeInvalidarloAntesDeGenerarNuevo()
    {
        var user       = MakeUser();
        var tokenViejo = MakeToken(user.Id, used: false, hoursOffset: 1);

        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.BuildMock());
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken> { tokenViejo }.BuildMock());
        _resetRepo.Setup(r => r.UpdateAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        _email.Setup(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await MakeHandler().Handle(new ForgotPasswordCommand(ValidEmail), CancellationToken.None);

        // El token viejo debe haberse marcado como usado
        tokenViejo.Used.Should().BeTrue();
        // Se genera uno nuevo (AddAsync llamado)
        _resetRepo.Verify(r => r.AddAsync(It.IsAny<PasswordResetToken>()), Times.Once);
    }

    // ── TC-FP-06 · El enlace de reset incluye la URL del frontend configurada ─

    [Fact]
    public async Task Handle_UsuarioActivo_EnlaceResetContieneUrlFrontend()
    {
        var user = MakeUser();
        string? capturedLink = null;

        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.BuildMock());
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken>().BuildMock());
        _resetRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        _email.Setup(e => e.SendPasswordResetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, link) => capturedLink = link)
            .Returns(Task.CompletedTask);

        await MakeHandler("https://midominio.com").Handle(
            new ForgotPasswordCommand(ValidEmail), CancellationToken.None);

        capturedLink.Should().StartWith("https://midominio.com/reset-password?token=");
    }

    // ── TC-FP-07 · Fallo de email no rompe el flujo (try/catch interno) ───────

    [Fact]
    public async Task Handle_EmailServiceLanzaExcepcion_RetornaSuccessIgual()
    {
        var user = MakeUser();

        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.BuildMock());
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken>().BuildMock());
        _resetRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        // El email falla con excepción
        _email.Setup(e => e.SendPasswordResetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Resend no disponible"));

        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand(ValidEmail), CancellationToken.None);

        // El token fue guardado y el comando retorna éxito aunque el email fallara
        result.IsSuccess.Should().BeTrue();
        _resetRepo.Verify(r => r.AddAsync(It.IsAny<PasswordResetToken>()), Times.Once);
    }

    // ── TC-FP-08 · Búsqueda de email es case-insensitive ─────────────────────

    [Fact]
    public async Task Handle_EmailEnMayusculas_EncuentraUsuarioCaseInsensitive()
    {
        var user = MakeUser(email: "tecnico@telecom.bo");

        _userRepo.Setup(r => r.GetAll())
            .Returns(new List<UserSystem> { user }.BuildMock());
        _resetRepo.Setup(r => r.GetAll())
            .Returns(new List<PasswordResetToken>().BuildMock());
        _resetRepo.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>())).Returns(Task.CompletedTask);
        _resetRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        _email.Setup(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Email en mayúsculas
        var result = await MakeHandler().Handle(
            new ForgotPasswordCommand("TECNICO@TELECOM.BO"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _email.Verify(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }
}
