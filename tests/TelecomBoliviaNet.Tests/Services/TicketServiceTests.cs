using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Tickets;
using TelecomBoliviaNet.Application.Tickets.Commands;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests unitarios para CreateTicketHandler (CQRS).
/// Cubre: creación válida, cliente inexistente, tipo/prioridad inválida,
/// asignación a técnico válido, asignación a no-técnico, SLA manual, balanceo.
/// </summary>
public class TicketServiceTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid TechId   = Guid.NewGuid();
    private static readonly Guid ActorId  = Guid.NewGuid();

    private static Client MakeClient() =>
        TestEntityFactory.MakeClient(
            id: ClientId, tbnCode: "TBN-001",
            fullName: "María López", phoneMain: "59170000002",
            installationDate: DateTime.UtcNow.AddMonths(-6));

    private static UserSystem MakeTecnico(Guid? id = null) => new()
    {
        Id       = id ?? TechId,
        FullName = "Carlos Técnico",
        Email    = "tech@telecom.bo",
        Phone    = "59171111111",
        Role     = UserRole.Tecnico,
        Status   = UserStatus.Activo,
    };

    private static AuditService MakeAudit() =>
        new AuditService(RepoMock.Empty<AuditLog>().Object,
                         NullLogger<AuditService>.Instance);

    private static Mock<INotifPublisher> MakeNotif()
    {
        var m = new Mock<INotifPublisher>();
        m.Setup(n => n.PublishAsync(
                It.IsAny<NotifType>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Guid?>()))
         .Returns(Task.CompletedTask);
        return m;
    }

    private static TicketNumberService MakeTicketNumSvc()
    {
        var seq = new Mock<ISequenceGenerator>();
        seq.Setup(s => s.NextTicketNumberAsync()).ReturnsAsync("TK-2026-0001");
        return new TicketNumberService(seq.Object);
    }

    private (CreateTicketHandler handler,
             Mock<IGenericRepository<SupportTicket>> ticketRepo)
        MakeHandler(
            Client?     client  = null,
            UserSystem? tecnico = null,
            IEnumerable<SupportTicket>? existingTickets = null)
    {
        var resolvedClient  = client  ?? MakeClient();
        var resolvedTecnico = tecnico ?? MakeTecnico();

        var ticketRepo  = RepoMock.Of(existingTickets?.ToArray() ?? Array.Empty<SupportTicket>());
        var clientRepo  = RepoMock.Of(resolvedClient);
        var userRepo    = RepoMock.Of(resolvedTecnico);
        var commentRepo = RepoMock.Empty<TicketComment>();
        var workLogRepo = RepoMock.Empty<TicketWorkLog>();
        var visitRepo   = RepoMock.Empty<TicketVisit>();
        var slaPlanRepo = RepoMock.Empty<SlaPlan>();
        var notifRepo   = RepoMock.Empty<TicketNotification>();
        var scheduleRepo = RepoMock.Empty<BusinessSchedule>();

        var helper = new TicketHelper(
            ticketRepo.Object, commentRepo.Object, workLogRepo.Object,
            visitRepo.Object, slaPlanRepo.Object, notifRepo.Object,
            scheduleRepo.Object, MakeNotif().Object);

        var balanceoSvc = new TicketBalanceoService(ticketRepo.Object, userRepo.Object);

        var handler = new CreateTicketHandler(
            ticketRepo.Object, clientRepo.Object, userRepo.Object,
            helper, MakeTicketNumSvc(), balanceoSvc, MakeAudit(),
            new Mock<IInternalAlertService>().Object);

        return (handler, ticketRepo);
    }

    // ── TC-TKT-01 · Crear ticket válido → número asignado y estado Abierto ───

    [Fact]
    public async Task Create_ValidDto_ReturnsSuccessWithTicketNumber()
    {
        var dto = new CreateTicketDto
        {
            ClientId    = ClientId,
            Subject     = "Sin internet",
            Type        = "SoporteTecnico",
            Priority    = "Alta",
            Description = "El cliente no tiene conexión a internet desde hoy.",
        };

        var (handler, ticketRepo) = MakeHandler();
        var cmd = new CreateTicketCommand(dto, ActorId, "Admin", "127.0.0.1");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TicketNumber.Should().Be("TK-2026-0001");
        result.Value.Status.Should().Be("Abierto");
        ticketRepo.Verify(r => r.AddAsync(
            It.Is<SupportTicket>(t =>
                t.ClientId == ClientId &&
                t.Status   == TicketStatus.Abierto &&
                t.Type     == TicketType.SoporteTecnico)),
            Times.Once);
    }

    // ── TC-TKT-02 · Crear ticket con cliente inexistente → Failure ───────────

    [Fact]
    public async Task Create_ClientNotFound_ReturnsFail()
    {
        var ticketRepo  = RepoMock.Empty<SupportTicket>();
        var clientRepo  = RepoMock.Empty<Client>(); // sin clientes
        var userRepo    = RepoMock.Of(MakeTecnico());
        var commentRepo = RepoMock.Empty<TicketComment>();
        var workLogRepo = RepoMock.Empty<TicketWorkLog>();
        var visitRepo   = RepoMock.Empty<TicketVisit>();
        var slaPlanRepo = RepoMock.Empty<SlaPlan>();
        var notifRepo   = RepoMock.Empty<TicketNotification>();
        var scheduleRepo = RepoMock.Empty<BusinessSchedule>();

        var helper = new TicketHelper(
            ticketRepo.Object, commentRepo.Object, workLogRepo.Object,
            visitRepo.Object, slaPlanRepo.Object, notifRepo.Object,
            scheduleRepo.Object, MakeNotif().Object);

        var handler = new CreateTicketHandler(
            ticketRepo.Object, clientRepo.Object, userRepo.Object,
            helper, MakeTicketNumSvc(),
            new TicketBalanceoService(ticketRepo.Object, userRepo.Object),
            MakeAudit(), new Mock<IInternalAlertService>().Object);

        var dto = new CreateTicketDto
        {
            ClientId    = Guid.NewGuid(), // ID que no existe
            Subject     = "Problema",
            Type        = "SoporteTecnico",
            Priority    = "Media",
            Description = "Test",
        };

        var result = await handler.Handle(
            new CreateTicketCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cliente");
    }

    // ── TC-TKT-03 · Crear ticket con tipo inválido → Failure ─────────────────

    [Fact]
    public async Task Create_InvalidType_ReturnsFail()
    {
        var (handler, _) = MakeHandler();
        var dto = new CreateTicketDto
        {
            ClientId    = ClientId,
            Subject     = "Problema",
            Type        = "TipoQueNoExiste",
            Priority    = "Alta",
            Description = "Test",
        };

        var result = await handler.Handle(
            new CreateTicketCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Tipo");
    }

    // ── TC-TKT-04 · Crear ticket con prioridad inválida → Failure ────────────

    [Fact]
    public async Task Create_InvalidPriority_ReturnsFail()
    {
        var (handler, _) = MakeHandler();
        var dto = new CreateTicketDto
        {
            ClientId    = ClientId,
            Subject     = "Problema",
            Type        = "SoporteTecnico",
            Priority    = "MuyUrgente", // no existe en el enum
            Description = "Test",
        };

        var result = await handler.Handle(
            new CreateTicketCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Prioridad");
    }

    // ── TC-TKT-05 · Crear ticket asignado a técnico válido ───────────────────

    [Fact]
    public async Task Create_WithValidTecnico_AssignsCorrectly()
    {
        var dto = new CreateTicketDto
        {
            ClientId         = ClientId,
            Subject          = "Sin señal",
            Type             = "SoporteTecnico",
            Priority         = "Baja",
            Description      = "Test",
            AssignedToUserId = TechId,
        };

        var (handler, ticketRepo) = MakeHandler();
        var result = await handler.Handle(
            new CreateTicketCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticketRepo.Verify(r => r.AddAsync(
            It.Is<SupportTicket>(t => t.AssignedToUserId == TechId)),
            Times.Once);
    }

    // ── TC-TKT-06 · Crear ticket asignado a usuario que no es Técnico → Failure ─

    [Fact]
    public async Task Create_AssignedToNonTecnico_ReturnsFail()
    {
        var operadorId = Guid.NewGuid();
        var operador = new UserSystem
        {
            Id       = operadorId,
            FullName = "Operador",
            Email    = "operador@telecom.bo",
            Role     = UserRole.Operador,
            Status   = UserStatus.Activo,
        };

        var ticketRepo  = RepoMock.Empty<SupportTicket>();
        var clientRepo  = RepoMock.Of(MakeClient());
        var userRepo    = RepoMock.Of(operador);
        var commentRepo = RepoMock.Empty<TicketComment>();
        var workLogRepo = RepoMock.Empty<TicketWorkLog>();
        var visitRepo   = RepoMock.Empty<TicketVisit>();
        var slaPlanRepo = RepoMock.Empty<SlaPlan>();
        var notifRepo   = RepoMock.Empty<TicketNotification>();
        var scheduleRepo = RepoMock.Empty<BusinessSchedule>();

        var helper = new TicketHelper(
            ticketRepo.Object, commentRepo.Object, workLogRepo.Object,
            visitRepo.Object, slaPlanRepo.Object, notifRepo.Object,
            scheduleRepo.Object, MakeNotif().Object);

        var handler = new CreateTicketHandler(
            ticketRepo.Object, clientRepo.Object, userRepo.Object,
            helper, MakeTicketNumSvc(),
            new TicketBalanceoService(ticketRepo.Object, userRepo.Object),
            MakeAudit(), new Mock<IInternalAlertService>().Object);

        var dto = new CreateTicketDto
        {
            ClientId         = ClientId,
            Subject          = "Problema",
            Type             = "SoporteTecnico",
            Priority         = "Media",
            Description      = "Test",
            AssignedToUserId = operadorId,
        };

        var result = await handler.Handle(
            new CreateTicketCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Técnico");
    }

    // ── TC-TKT-07 · Crear ticket con SLA manual → DueDate calculado ──────────

    [Fact]
    public async Task Create_WithSlaDuration_SetsDueDate()
    {
        var dto = new CreateTicketDto
        {
            ClientId         = ClientId,
            Subject          = "Urgente",
            Type             = "SoporteTecnico",
            Priority         = "Critica",
            Description      = "Falla total de red",
            SlaDurationHours = 4,
        };

        var (handler, ticketRepo) = MakeHandler();
        var before = DateTime.UtcNow;

        var result = await handler.Handle(
            new CreateTicketCommand(dto, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticketRepo.Verify(r => r.AddAsync(
            It.Is<SupportTicket>(t =>
                t.DueDate.HasValue &&
                t.DueDate.Value >= before.AddHours(4).AddMinutes(-1) &&
                t.DueDate.Value <= before.AddHours(4).AddMinutes(1))),
            Times.Once);
    }

    // ── TC-TKT-08 · TicketBalanceoService — técnico sin tickets tiene menor carga ─

    [Fact]
    public async Task Balanceo_TecnicoWithNoTickets_HasZeroLoad()
    {
        var tec1 = MakeTecnico();
        var tec2 = MakeTecnico(Guid.NewGuid());
        tec2.FullName = "Tech2";
        tec2.Email    = "t2@bo";

        // tec1 tiene 2 tickets activos, tec2 tiene 0
        var ticket1 = new SupportTicket
        {
            Id = Guid.NewGuid(), ClientId = ClientId,
            AssignedToUserId = tec1.Id,
            Status = TicketStatus.Abierto,
            Subject = "T1", Description = "D1",
            CreatedAt = DateTime.UtcNow,
        };
        var ticket2 = new SupportTicket
        {
            Id = Guid.NewGuid(), ClientId = ClientId,
            AssignedToUserId = tec1.Id,
            Status = TicketStatus.EnProceso,
            Subject = "T2", Description = "D2",
            CreatedAt = DateTime.UtcNow,
        };

        var ticketRepo = RepoMock.Of(ticket1, ticket2);
        var userRepo   = RepoMock.Of(tec1, tec2);
        var balanceo   = new TicketBalanceoService(ticketRepo.Object, userRepo.Object);

        var selected = await balanceo.GetTecnicoMenorCargaAsync();

        selected.Should().Be(tec2.Id);
    }
}
