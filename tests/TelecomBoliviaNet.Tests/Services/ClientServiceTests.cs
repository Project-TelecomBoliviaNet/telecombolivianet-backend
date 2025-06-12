using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.Clients.Commands;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests unitarios para handlers de ciclo de vida de cliente (CQRS).
/// Cubre: SuspendClientHandler, ReactivateClientHandler, CancelClientHandler.
/// </summary>
public class ClientServiceTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid PlanId  = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static Plan MakePlan() =>
        new() { Id = PlanId, Name = "Plan Plata", MonthlyPrice = 150m, IsActive = true };

    private static Client MakeClient(
        ClientStatus status = ClientStatus.Activo,
        IEnumerable<Invoice>? invoices = null,
        DateTime? suspendedAt = null)
    {
        var client = TestEntityFactory.MakeClient(
            id: ClientId,
            tbnCode: "TBN-001", fullName: "Juan Mamani", phoneMain: "59170000001",
            planId: PlanId, status: status,
            installationDate: DateTime.UtcNow.AddMonths(-3),
            suspendedAt: suspendedAt,
            invoices: invoices);
        client.Plan = MakePlan();
        return client;
    }

    private static AuditService MakeAudit() =>
        new(RepoMock.Empty<AuditLog>().Object, NullLogger<AuditService>.Instance);

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

    private static (SuspendClientHandler handler,
                    Mock<IGenericRepository<Client>> clientRepo,
                    Mock<INotifPublisher> notif)
        MakeSuspend(Client? client = null)
    {
        var notif      = MakeNotif();
        var clientRepo = RepoMock.Of(client ?? MakeClient());
        var handler    = new SuspendClientHandler(clientRepo.Object, MakeAudit(), notif.Object);
        return (handler, clientRepo, notif);
    }

    private static (ReactivateClientHandler handler,
                    Mock<IGenericRepository<Client>> clientRepo,
                    Mock<INotifPublisher> notif)
        MakeReactivate(Client? client = null)
    {
        var notif      = MakeNotif();
        var clientRepo = RepoMock.Of(client ?? MakeClient());
        var handler    = new ReactivateClientHandler(clientRepo.Object, MakeAudit(), notif.Object);
        return (handler, clientRepo, notif);
    }

    private static (CancelClientHandler handler,
                    Mock<IGenericRepository<Client>> clientRepo)
        MakeCancel(Client? client = null)
    {
        var clientRepo = RepoMock.Of(client ?? MakeClient());
        var ticketRepo = RepoMock.Empty<SupportTicket>();
        var handler    = new CancelClientHandler(clientRepo.Object, ticketRepo.Object, MakeAudit());
        return (handler, clientRepo);
    }

    // ── TC-CLI-01 · Suspender cliente activo → OK + notificación ────────────

    [Fact]
    public async Task Suspend_ActiveClient_SetsStatusAndPublishesNotif()
    {
        var client = MakeClient(ClientStatus.Activo);
        var (handler, clientRepo, notif) = MakeSuspend(client);

        var result = await handler.Handle(
            new SuspendClientCommand(ClientId, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        clientRepo.Verify(r => r.UpdateAsync(
            It.Is<Client>(c =>
                c.Status      == ClientStatus.Suspendido &&
                c.SuspendedAt != null)),
            Times.Once);
        notif.Verify(n => n.PublishAsync(
            NotifType.SUSPENSION, ClientId, It.IsAny<string?>(),
            It.IsAny<Dictionary<string, string>>(), null),
            Times.Once);
    }

    // ── TC-CLI-02 · Suspender cliente ya suspendido → Failure ────────────────

    [Fact]
    public async Task Suspend_AlreadySuspended_ReturnsFail()
    {
        var client = MakeClient(ClientStatus.Suspendido);
        var (handler, clientRepo, _) = MakeSuspend(client);

        var result = await handler.Handle(
            new SuspendClientCommand(ClientId, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("suspendido");
        clientRepo.Verify(r => r.UpdateAsync(It.IsAny<Client>()), Times.Never);
    }

    // ── TC-CLI-03 · Suspender cliente dado de baja → Failure ─────────────────

    [Fact]
    public async Task Suspend_CancelledClient_ReturnsFail()
    {
        var client = MakeClient(ClientStatus.DadoDeBaja);
        var (handler, _, _) = MakeSuspend(client);

        var result = await handler.Handle(
            new SuspendClientCommand(ClientId, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("baja");
    }

    // ── TC-CLI-04 · Reactivar cliente suspendido → OK + limpia SuspendedAt ──

    [Fact]
    public async Task Reactivate_SuspendedClient_SetsActiveAndClearsSuspendedAt()
    {
        var client = MakeClient(ClientStatus.Suspendido, suspendedAt: DateTime.UtcNow.AddDays(-10));
        var (handler, clientRepo, notif) = MakeReactivate(client);

        var result = await handler.Handle(
            new ReactivateClientCommand(ClientId, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        clientRepo.Verify(r => r.UpdateAsync(
            It.Is<Client>(c =>
                c.Status      == ClientStatus.Activo &&
                c.SuspendedAt == null)),
            Times.Once);
        notif.Verify(n => n.PublishAsync(
            NotifType.REACTIVACION, ClientId, It.IsAny<string?>(),
            It.IsAny<Dictionary<string, string>>(), null),
            Times.Once);
    }

    // ── TC-CLI-05 · Reactivar cliente ya activo → Failure ────────────────────

    [Fact]
    public async Task Reactivate_AlreadyActive_ReturnsFail()
    {
        var client = MakeClient(ClientStatus.Activo);
        var (handler, clientRepo, _) = MakeReactivate(client);

        var result = await handler.Handle(
            new ReactivateClientCommand(ClientId, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("activo");
        clientRepo.Verify(r => r.UpdateAsync(It.IsAny<Client>()), Times.Never);
    }

    // ── TC-CLI-06 · Dar de baja sin deuda → marca DadoDeBaja ─────────────────

    [Fact]
    public async Task Cancel_NoDebt_MarksAsDadoDeBaja()
    {
        var client = MakeClient(ClientStatus.Activo); // sin facturas pendientes
        var (handler, clientRepo) = MakeCancel(client);

        var result = await handler.Handle(
            new CancelClientCommand(ClientId, Confirmed: false, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        clientRepo.Verify(r => r.UpdateAsync(
            It.Is<Client>(c =>
                c.Status    == ClientStatus.DadoDeBaja &&
                c.IsDeleted == true)),
            Times.Once);
    }

    // ── TC-CLI-07 · Dar de baja con deuda sin confirmar → pide confirmación ──

    [Fact]
    public async Task Cancel_WithDebt_WithoutConfirmation_RequiresConfirmation()
    {
        var invoice = new Invoice
        {
            Id       = Guid.NewGuid(),
            ClientId = ClientId,
            Status   = InvoiceStatus.Pendiente,
            Amount   = 150m,
            Year     = DateTime.UtcNow.Year,
            Month    = DateTime.UtcNow.Month,
            Type     = InvoiceType.Mensualidad,
            IssuedAt = DateTime.UtcNow,
            DueDate  = DateTime.UtcNow.AddDays(5),
        };
        var client = MakeClient(ClientStatus.Activo, invoices: [invoice]);
        var (handler, clientRepo) = MakeCancel(client);

        var result = await handler.Handle(
            new CancelClientCommand(ClientId, Confirmed: false, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("needs_confirmation");
        clientRepo.Verify(r => r.UpdateAsync(It.IsAny<Client>()), Times.Never);
    }

    // ── TC-CLI-08 · Dar de baja con deuda y confirmed=true → procede ─────────

    [Fact]
    public async Task Cancel_WithDebt_Confirmed_MarksAsDadoDeBaja()
    {
        var invoice = new Invoice
        {
            Id       = Guid.NewGuid(),
            ClientId = ClientId,
            Status   = InvoiceStatus.Pendiente,
            Amount   = 150m,
            Year     = DateTime.UtcNow.Year,
            Month    = DateTime.UtcNow.Month,
            Type     = InvoiceType.Mensualidad,
            IssuedAt = DateTime.UtcNow,
            DueDate  = DateTime.UtcNow.AddDays(5),
        };
        var client = MakeClient(ClientStatus.Activo, invoices: [invoice]);
        var (handler, clientRepo) = MakeCancel(client);

        var result = await handler.Handle(
            new CancelClientCommand(ClientId, Confirmed: true, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe("needs_confirmation");
        clientRepo.Verify(r => r.UpdateAsync(
            It.Is<Client>(c => c.Status == ClientStatus.DadoDeBaja)),
            Times.Once);
    }

    // ── TC-CLI-09 · Dar de baja cliente ya dado de baja → Failure ────────────

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsFail()
    {
        var client = MakeClient(ClientStatus.DadoDeBaja);
        var (handler, _) = MakeCancel(client);

        var result = await handler.Handle(
            new CancelClientCommand(ClientId, Confirmed: true, ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("baja");
    }

    // ── TC-CLI-10 · Suspender cliente inexistente → Failure ──────────────────

    [Fact]
    public async Task Suspend_ClientNotFound_ReturnsFail()
    {
        var emptyClientRepo = RepoMock.Empty<Client>();
        var handler = new SuspendClientHandler(
            emptyClientRepo.Object, MakeAudit(), MakeNotif().Object);

        var result = await handler.Handle(
            new SuspendClientCommand(Guid.NewGuid(), ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no encontrado");
    }
}
