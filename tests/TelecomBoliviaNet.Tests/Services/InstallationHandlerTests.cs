using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Installations.Commands;
using TelecomBoliviaNet.Application.Installations.Queries;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests unitarios para los handlers CQRS del módulo Instalaciones.
/// Cubre: GetInstalacionSlotsHandler, CrearInstalacionHandler,
///        CancelarInstalacionHandler, CompletarInstalacionHandler,
///        AsignarTecnicoHandler, ReprogramarInstalacionHandler.
/// </summary>
public class InstallationHandlerTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid PlanId   = Guid.NewGuid();
    private static readonly Guid TecId    = Guid.NewGuid();
    private static readonly Guid ActorId  = Guid.NewGuid();

    private static Plan MakePlan(bool active = true) =>
        new() { Id = PlanId, Name = "Plan Oro", MonthlyPrice = 200m, IsActive = active };

    private static Client MakeClient() =>
        TestEntityFactory.MakeClient(id: ClientId, tbnCode: "TBN-042",
            fullName: "Ana Pérez", phoneMain: "59170000099");

    private static UserSystem MakeTecnico(Guid? id = null) => new()
    {
        Id = id ?? TecId, FullName = "Carlos Tech", Email = "t@bo",
        Role = UserRole.Tecnico, Status = UserStatus.Activo,
    };

    private static Installation MakeInstallation(
        InstallationStatus status = InstallationStatus.Pendiente,
        SupportTicket?     ticket = null)
    {
        var inst = new Installation
        {
            Id         = Guid.NewGuid(),
            ClientId   = ClientId,
            PlanId     = PlanId,
            Fecha      = DateTime.UtcNow.Date.AddDays(2),
            HoraInicio = new TimeOnly(10, 0),
            DuracionMin = 120,
            Direccion  = "Calle Falsa 123",
            CreadoAt   = DateTime.UtcNow,
        };
        // Set status via reflection (same pattern as Client)
        typeof(Installation)
            .GetProperty(nameof(Installation.Status))!
            .SetValue(inst, status);
        if (ticket is not null) inst.Ticket = ticket;
        return inst;
    }

    private static AuditService MakeAudit() =>
        new(RepoMock.Empty<AuditLog>().Object, NullLogger<AuditService>.Instance);

    // ── TC-INST-01 · Slots sin instalaciones activas → todos disponibles ───────

    [Fact]
    public async Task GetSlots_NoActiveInstallations_AllSlotsAvailable()
    {
        var tecnico    = MakeTecnico();
        var instRepo   = RepoMock.Empty<Installation>();
        var userRepo   = RepoMock.Of(tecnico);
        var handler    = new GetInstalacionSlotsHandler(instRepo.Object, userRepo.Object);

        var slots = await handler.Handle(new GetInstalacionSlotsQuery(2), CancellationToken.None);

        slots.Should().NotBeEmpty();
        slots.Should().OnlyContain(s => s.Disponible);
    }

    // ── TC-INST-02 · Crear instalación válida → éxito + ticket creado ──────────

    [Fact]
    public async Task Crear_ValidDto_ReturnsSuccessAndCreatesTicket()
    {
        var instRepo   = RepoMock.Empty<Installation>();
        var clientRepo = RepoMock.Of(MakeClient());
        var planRepo   = RepoMock.Of<Plan>(MakePlan());
        var userRepo   = RepoMock.Of(MakeTecnico());
        var ticketRepo = RepoMock.Empty<SupportTicket>();
        var handler    = new CrearInstalacionHandler(
            instRepo.Object, clientRepo.Object, planRepo.Object,
            userRepo.Object, ticketRepo.Object, MakeAudit());

        var dto = new CrearInstalacionDto
        {
            ClienteId  = ClientId,
            PlanId     = PlanId,
            Fecha      = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),
            HoraInicio = "10:00",
            Direccion  = "Av. Principal 456",
        };

        var result = await handler.Handle(
            new CrearInstalacionCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Pendiente");
        ticketRepo.Verify(r => r.AddAsync(
            It.Is<SupportTicket>(t =>
                t.Type   == TicketType.InstalacionNueva &&
                t.Status == TicketStatus.Abierto)),
            Times.Once);
    }

    // ── TC-INST-03 · Crear con cliente inexistente → Failure ───────────────────

    [Fact]
    public async Task Crear_ClientNotFound_ReturnsFail()
    {
        var handler = new CrearInstalacionHandler(
            RepoMock.Empty<Installation>().Object,
            RepoMock.Empty<Client>().Object,
            RepoMock.Of<Plan>(MakePlan()).Object,
            RepoMock.Of(MakeTecnico()).Object,
            RepoMock.Empty<SupportTicket>().Object,
            MakeAudit());

        var dto = new CrearInstalacionDto
        {
            ClienteId  = Guid.NewGuid(),
            PlanId     = PlanId,
            Fecha      = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),
            HoraInicio = "10:00",
            Direccion  = "Test",
        };

        var result = await handler.Handle(
            new CrearInstalacionCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cliente");
    }

    // ── TC-INST-04 · Crear con plan inactivo → Failure ─────────────────────────

    [Fact]
    public async Task Crear_InactivePlan_ReturnsFail()
    {
        var handler = new CrearInstalacionHandler(
            RepoMock.Empty<Installation>().Object,
            RepoMock.Of(MakeClient()).Object,
            RepoMock.Of<Plan>(MakePlan(active: false)).Object,
            RepoMock.Of(MakeTecnico()).Object,
            RepoMock.Empty<SupportTicket>().Object,
            MakeAudit());

        var dto = new CrearInstalacionDto
        {
            ClienteId  = ClientId,
            PlanId     = PlanId,
            Fecha      = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),
            HoraInicio = "10:00",
            Direccion  = "Test",
        };

        var result = await handler.Handle(
            new CrearInstalacionCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("plan");
    }

    // ── TC-INST-05 · Cancelar instalación pendiente → OK + ticket cerrado ──────

    [Fact]
    public async Task Cancelar_PendingInstallation_ClosesTicket()
    {
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(), ClientId = ClientId,
            Status = TicketStatus.Abierto, Subject = "Test",
            Description = "Test", CreatedAt = DateTime.UtcNow,
        };
        var inst       = MakeInstallation(ticket: ticket);
        var instRepo   = RepoMock.Of(inst);
        var ticketRepo = RepoMock.Of(ticket);
        var handler    = new CancelarInstalacionHandler(
            instRepo.Object, ticketRepo.Object, MakeAudit());

        var result = await handler.Handle(
            new CancelarInstalacionCommand(
                inst.Id,
                new CancelarInstalacionDto { MotivoCancelacion = "Cambio de planes", CanceladoPor = "Admin" },
                ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        instRepo.Verify(r => r.UpdateAsync(
            It.Is<Installation>(i => i.Status == InstallationStatus.Cancelada)),
            Times.Once);
        ticketRepo.Verify(r => r.UpdateAsync(
            It.Is<SupportTicket>(t => t.Status == TicketStatus.Cerrado)),
            Times.Once);
    }

    // ── TC-INST-06 · Cancelar instalación ya completada → Failure ──────────────

    [Fact]
    public async Task Cancelar_CompletedInstallation_ReturnsFail()
    {
        var inst    = MakeInstallation(InstallationStatus.Completada);
        var handler = new CancelarInstalacionHandler(
            RepoMock.Of(inst).Object,
            RepoMock.Empty<SupportTicket>().Object,
            MakeAudit());

        var result = await handler.Handle(
            new CancelarInstalacionCommand(
                inst.Id,
                new CancelarInstalacionDto { MotivoCancelacion = "Motivo", CanceladoPor = "Admin" },
                ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // ── TC-INST-07 · Completar → ticket pasa a Resuelto ────────────────────────

    [Fact]
    public async Task Completar_PendingInstallation_ResolvesTicket()
    {
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(), ClientId = ClientId,
            Status = TicketStatus.Abierto, Subject = "Test",
            Description = "Test", CreatedAt = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddHours(4),
        };
        var inst       = MakeInstallation(InstallationStatus.EnProceso, ticket);
        var instRepo   = RepoMock.Of(inst);
        var ticketRepo = RepoMock.Of(ticket);
        var handler    = new CompletarInstalacionHandler(
            instRepo.Object, ticketRepo.Object, MakeAudit());

        var result = await handler.Handle(
            new CompletarInstalacionCommand(
                inst.Id,
                new CompletarInstalacionDto { NotasTecnico = "Todo perfecto" },
                ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticketRepo.Verify(r => r.UpdateAsync(
            It.Is<SupportTicket>(t => t.Status == TicketStatus.Resuelto)),
            Times.Once);
    }

    // ── TC-INST-08 · Asignar técnico sin rol → Failure ─────────────────────────

    [Fact]
    public async Task AsignarTecnico_NonTecnicoRole_ReturnsFail()
    {
        var operador = new UserSystem
        {
            Id = Guid.NewGuid(), FullName = "Op", Email = "op@bo",
            Role = UserRole.Operador, Status = UserStatus.Activo,
        };
        var inst    = MakeInstallation();
        var handler = new AsignarTecnicoHandler(
            RepoMock.Of(inst).Object,
            RepoMock.Of(operador).Object,
            MakeAudit());

        var result = await handler.Handle(
            new AsignarTecnicoCommand(
                inst.Id,
                new AsignarTecnicoDto { TecnicoId = operador.Id },
                ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Técnico");
    }
}
