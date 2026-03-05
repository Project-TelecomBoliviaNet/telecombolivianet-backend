using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Users.Commands;
using TelecomBoliviaNet.Application.Users.Queries;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests unitarios para los handlers CQRS del módulo Usuarios.
/// Cubre: GetUsersListHandler, GetUserByIdHandler, GetUserDetailHandler,
///        GetPermissionMatrixHandler, CreateUserHandler, UpdateUserHandler,
///        DeactivateUserHandler, ReactivateUserHandler, UnlockUserHandler,
///        SoftDeleteUserHandler, ForceResetPasswordHandler,
///        ForgotPasswordHandler, ResetPasswordHandler.
/// </summary>
public class UserHandlerTests
{
    private static readonly Guid UserId  = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static UserSystem MakeUser(
        UserStatus status = UserStatus.Activo,
        UserRole   role   = UserRole.Operador,
        Guid?      id     = null,
        bool       deleted = false,
        string?    phone  = null) => new()
    {
        Id                     = id ?? UserId,
        FullName               = "Test User",
        Email                  = "test@bo.com",
        PasswordHash           = "hashed",
        Role                   = role,
        Status                 = status,
        RequiresPasswordChange = false,
        FailedLoginAttempts    = 0,
        CreatedAt              = DateTime.UtcNow,
        IsDeleted              = deleted,
        Phone                  = phone,
    };

    private static AuditService MakeAudit() =>
        new(RepoMock.Empty<AuditLog>().Object, NullLogger<AuditService>.Instance);

    private static IPasswordHasher MakeHasher()
    {
        var mock = new Mock<IPasswordHasher>();
        mock.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed_pw");
        return mock.Object;
    }

    private static IConfiguration MakeConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SISTEMA_BOT_EMAIL"] = "bot@bo.com" })
        .Build();

    private static IEmailService MakeEmail()
    {
        var mock = new Mock<IEmailService>();
        mock.Setup(e => e.SendAccountActivationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }

    private static CreateUserHandler MakeCreateHandler(params UserSystem[] existing) =>
        new(RepoMock.Of(existing).Object,
            RepoMock.Empty<EmailActivationToken>().Object,
            MakeHasher(),
            MakeEmail(),
            MakeAudit(),
            MakeConfig(),
            NullLogger<CreateUserHandler>.Instance);

    // ── TC-USR-01 · Listar usuarios excluye bot ────────────────────────────────

    [Fact]
    public async Task GetUsersList_ExcludesBotEmail()
    {
        var bot     = MakeUser(id: Guid.NewGuid());
        bot.Email   = "bot@bo.com";  // set email to match bot filter
        var normal  = MakeUser(id: Guid.NewGuid());
        var repo    = RepoMock.Of(bot, normal);
        var handler = new GetUsersListHandler(repo.Object, MakeConfig());

        var result = await handler.Handle(new GetUsersListQuery(), CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items.First().Email.Should().Be(normal.Email);
    }

    // ── TC-USR-02 · Obtener usuario por ID existente → DTO correcto ────────────

    [Fact]
    public async Task GetUserById_Exists_ReturnsDto()
    {
        var user    = MakeUser();
        var handler = new GetUserByIdHandler(RepoMock.Of(user).Object);

        var dto = await handler.Handle(new GetUserByIdQuery(UserId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(UserId);
        dto.Role.Should().Be("Operador");
    }

    // ── TC-USR-03 · Obtener usuario inexistente → null ─────────────────────────

    [Fact]
    public async Task GetUserById_NotFound_ReturnsNull()
    {
        var handler = new GetUserByIdHandler(RepoMock.Empty<UserSystem>().Object);

        var dto = await handler.Handle(new GetUserByIdQuery(Guid.NewGuid()), CancellationToken.None);

        dto.Should().BeNull();
    }

    // ── TC-USR-04 · Matriz de permisos → 4 roles ──────────────────────────────

    [Fact]
    public async Task GetPermissionMatrix_ReturnsFourRoles()
    {
        var handler = new GetPermissionMatrixHandler();

        var matrix = await handler.Handle(new GetPermissionMatrixQuery(), CancellationToken.None);

        matrix.Should().HaveCount(4);
        matrix.Select(r => r.Role).Should().Contain(["Admin", "Operador", "Tecnico", "SocioLectura"]);
    }

    // ── TC-USR-05 · Crear usuario válido → éxito ──────────────────────────────

    [Fact]
    public async Task CreateUser_Valid_ReturnsSuccess()
    {
        var handler = MakeCreateHandler();

        var dto = new CreateUserDto("Ana López", "ana@bo.com", "Password1!", "Operador");
        var result = await handler.Handle(
            new CreateUserCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().Be("ana@bo.com");
        result.Value.Role.Should().Be("Operador");
    }

    // ── TC-USR-06 · Crear con email duplicado → Failure ───────────────────────

    [Fact]
    public async Task CreateUser_DuplicateEmail_ReturnsFail()
    {
        var existing = MakeUser();
        var handler  = MakeCreateHandler(existing);

        var dto = new CreateUserDto("Otro", existing.Email, "Password1!", "Operador");
        var result = await handler.Handle(
            new CreateUserCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("correo");
    }

    // ── TC-USR-07 · Crear con rol inválido → Failure ──────────────────────────

    [Fact]
    public async Task CreateUser_InvalidRole_ReturnsFail()
    {
        var handler = MakeCreateHandler();

        var dto = new CreateUserDto("X", "x@bo.com", "Pass1234!", "SuperAdmin");
        var result = await handler.Handle(
            new CreateUserCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Rol");
    }

    // ── TC-USR-08 · Desactivar cuenta activa → OK ─────────────────────────────

    [Fact]
    public async Task DeactivateUser_Active_ReturnsSuccess()
    {
        var user    = MakeUser(UserStatus.Activo);
        var repo    = RepoMock.Of(user);
        var handler = new DeactivateUserHandler(repo.Object, MakeAudit());

        var result = await handler.Handle(
            new DeactivateUserCommand(UserId, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(
            It.Is<UserSystem>(u => u.Status == UserStatus.Inactivo)), Times.Once);
    }

    // ── TC-USR-09 · Desactivar ya inactiva → Failure ──────────────────────────

    [Fact]
    public async Task DeactivateUser_AlreadyInactive_ReturnsFail()
    {
        var user    = MakeUser(UserStatus.Inactivo);
        var handler = new DeactivateUserHandler(RepoMock.Of(user).Object, MakeAudit());

        var result = await handler.Handle(
            new DeactivateUserCommand(UserId, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // ── TC-USR-10 · Desbloquear cuenta bloqueada → OK ─────────────────────────

    [Fact]
    public async Task UnlockUser_Blocked_ResetsAttempts()
    {
        var user = MakeUser(UserStatus.Bloqueado);
        user.FailedLoginAttempts = 5;
        var repo    = RepoMock.Of(user);
        var handler = new UnlockUserHandler(repo.Object, MakeAudit());

        var result = await handler.Handle(
            new UnlockUserCommand(UserId, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(
            It.Is<UserSystem>(u =>
                u.Status == UserStatus.Activo &&
                u.FailedLoginAttempts == 0)), Times.Once);
    }

    // ── TC-USR-11 · Desbloquear cuenta no bloqueada → Failure ─────────────────

    [Fact]
    public async Task UnlockUser_NotBlocked_ReturnsFail()
    {
        var user    = MakeUser(UserStatus.Activo);
        var handler = new UnlockUserHandler(RepoMock.Of(user).Object, MakeAudit());

        var result = await handler.Handle(
            new UnlockUserCommand(UserId, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("bloqueada");
    }

    // ── TC-USR-12 · Baja lógica → IsDeleted + Inactivo ────────────────────────

    [Fact]
    public async Task SoftDeleteUser_Active_SetsDeletedAndInactive()
    {
        var user    = MakeUser(UserStatus.Activo);
        var repo    = RepoMock.Of(user);
        var handler = new SoftDeleteUserHandler(repo.Object, MakeAudit());

        var result = await handler.Handle(
            new SoftDeleteUserCommand(UserId, "Cese laboral", ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(
            It.Is<UserSystem>(u => u.IsDeleted && u.Status == UserStatus.Inactivo)), Times.Once);
    }

    // ── TC-USR-13 · Baja lógica ya eliminado → Failure ────────────────────────

    [Fact]
    public async Task SoftDeleteUser_AlreadyDeleted_ReturnsFail()
    {
        var user    = MakeUser(deleted: true);
        var handler = new SoftDeleteUserHandler(RepoMock.Of(user).Object, MakeAudit());

        var result = await handler.Handle(
            new SoftDeleteUserCommand(UserId, "Motivo", ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // ── TC-USR-14 · Force reset password → RequiresPasswordChange = true ──────

    [Fact]
    public async Task ForceResetPassword_ValidUser_SetsRequiresChange()
    {
        var user    = MakeUser();
        var repo    = RepoMock.Of(user);
        var handler = new ForceResetPasswordHandler(repo.Object, MakeHasher(), MakeAudit());

        var result = await handler.Handle(
            new ForceResetPasswordCommand(UserId, "Temp1234!", ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(
            It.Is<UserSystem>(u =>
                u.RequiresPasswordChange &&
                u.FailedLoginAttempts == 0)), Times.Once);
    }

    // ── TC-USR-15 · Reset password con contraseñas distintas → Failure ─────────

    [Fact]
    public async Task ResetPassword_PasswordMismatch_ReturnsFail()
    {
        var handler = new ResetPasswordHandler(
            RepoMock.Empty<UserSystem>().Object,
            RepoMock.Empty<PasswordResetToken>().Object,
            MakeHasher(),
            new Mock<IEmailService>().Object);

        var result = await handler.Handle(
            new ResetPasswordCommand("sometoken", "Pass1234!", "Different!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("coinciden");
    }

    // ── TC-USR-16 · Reset password con token inválido → Failure ───────────────

    [Fact]
    public async Task ResetPassword_InvalidToken_ReturnsFail()
    {
        var handler = new ResetPasswordHandler(
            RepoMock.Empty<UserSystem>().Object,
            RepoMock.Empty<PasswordResetToken>().Object,
            MakeHasher(),
            new Mock<IEmailService>().Object);

        var result = await handler.Handle(
            new ResetPasswordCommand("invalid_token", "Pass1234!", "Pass1234!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("inválido");
    }

    // ── TC-USR-17 · ForgotPassword usuario inexistente → generic OK ───────────

    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsGenericSuccess()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:FrontendUrl"] = "http://localhost" })
            .Build();

        var handler = new ForgotPasswordHandler(
            RepoMock.Empty<UserSystem>().Object,
            RepoMock.Empty<PasswordResetToken>().Object,
            new Mock<IEmailService>().Object,
            config,
            NullLogger<ForgotPasswordHandler>.Instance);

        var result = await handler.Handle(
            new ForgotPasswordCommand("noexiste@bo.com"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Channel.Should().Be("Email");
    }
}
