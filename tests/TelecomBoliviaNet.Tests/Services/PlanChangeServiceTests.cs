using FluentAssertions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Clients;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

public class PlanChangeServiceTests
{
    private static readonly Guid ActorId   = Guid.NewGuid();
    private static readonly Guid ClientId  = Guid.NewGuid();
    private static readonly Guid OldPlanId = Guid.NewGuid();
    private static readonly Guid NewPlanId = Guid.NewGuid();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Plan MakePlan(Guid id, string name, decimal price, bool active = true) =>
        new() { Id = id, Name = name, MonthlyPrice = price, IsActive = active };

    private static Client MakeClient(Guid planId, Plan plan)
    {
        var client = TestEntityFactory.MakeClient(
            id:               ClientId,
            tbnCode:          "TBN-100",
            fullName:         "Cliente Prueba",
            planId:           planId,
            installationDate: DateTime.UtcNow.AddMonths(-3));
        client.Plan = plan;
        return client;
    }

    private static AuditService MakeAudit() =>
        new(RepoMock.Empty<AuditLog>().Object, NullLogger<AuditService>.Instance);

    private static IUnitOfWork MakeUow()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(default)).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitAsync(default)).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackAsync(default)).Returns(Task.CompletedTask);
        return uow.Object;
    }

    private static INotifPublisher MakeNotif()
    {
        var notif = new Mock<INotifPublisher>();
        notif.Setup(n => n.PublishAsync(
            It.IsAny<NotifType>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()))
            .Returns(Task.CompletedTask);
        return notif.Object;
    }

    private (PlanChangeService svc,
             Mock<IGenericRepository<PlanChangeRequest>> changeRepo,
             Mock<IGenericRepository<Invoice>> invoiceRepo,
             Mock<INotifPublisher> notif)
        MakeService(
            IEnumerable<PlanChangeRequest>? changes  = null,
            IEnumerable<Invoice>?           invoices = null,
            Client? client   = null,
            Plan?   newPlan  = null)
    {
        var oldPlan     = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var resolvedNew = newPlan  ?? MakePlan(NewPlanId, "Plan Plata", 150m);
        var resolvedCli = client   ?? MakeClient(OldPlanId, oldPlan);

        var changeRepo  = RepoMock.Of(changes?.ToArray()  ?? []);
        var clientRepo  = RepoMock.Of(resolvedCli);
        var planRepo    = RepoMock.Of(oldPlan, resolvedNew);
        var invoiceRepo = RepoMock.Of(invoices?.ToArray() ?? []);
        var ticketRepo  = RepoMock.Empty<SupportTicket>();
        var notifMock   = new Mock<INotifPublisher>();
        notifMock.Setup(n => n.PublishAsync(
            It.IsAny<NotifType>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()))
            .Returns(Task.CompletedTask);

        var svc = new PlanChangeService(
            changeRepo.Object, clientRepo.Object, planRepo.Object,
            invoiceRepo.Object, ticketRepo.Object,
            MakeAudit(), MakeUow(), notifMock.Object,
            new Mock<IInternalAlertService>().Object);

        return (svc, changeRepo, invoiceRepo, notifMock);
    }

    // ════════════════════════════════════════════════════════════════════════
    // SolicitarCambioAsync
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SolicitarCambio_CreaRegistroPendiente()
    {
        var (svc, changeRepo, _, _) = MakeService();

        var result = await svc.SolicitarCambioAsync(
            ClientId, NewPlanId, null, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        changeRepo.Verify(r => r.AddAsync(
            It.Is<PlanChangeRequest>(c =>
                c.ClientId  == ClientId &&
                c.NewPlanId == NewPlanId &&
                c.Status    == PlanChangeStatus.Pendiente)),
            Times.Once);
    }

    [Fact]
    public async Task SolicitarCambio_FechaEfectivaEs1roDeMesSiguiente()
    {
        var (svc, changeRepo, _, _) = MakeService();

        await svc.SolicitarCambioAsync(ClientId, NewPlanId, null, ActorId, "Admin", "127.0.0.1");

        var hoy      = DateTime.UtcNow;
        var expected = new DateTime(hoy.Year, hoy.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

        changeRepo.Verify(r => r.AddAsync(
            It.Is<PlanChangeRequest>(c => c.EffectiveDate == expected)),
            Times.Once);
    }

    [Fact]
    public async Task SolicitarCambio_ConPendienteExistente_RetornaError()
    {
        var pending = new PlanChangeRequest
        {
            ClientId = ClientId, Status = PlanChangeStatus.Pendiente,
            OldPlanId = OldPlanId, NewPlanId = NewPlanId,
        };
        var (svc, _, _, _) = MakeService(changes: [pending]);

        var result = await svc.SolicitarCambioAsync(
            ClientId, NewPlanId, null, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pendiente");
    }

    [Fact]
    public async Task SolicitarCambio_MismoPlan_RetornaError()
    {
        var (svc, _, _, _) = MakeService();

        var result = await svc.SolicitarCambioAsync(
            ClientId, OldPlanId, null, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ya tiene ese plan");
    }

    [Fact]
    public async Task SolicitarCambio_PlanInactivo_RetornaError()
    {
        var planInactivo = MakePlan(NewPlanId, "Plan Inactivo", 200m, active: false);
        var (svc, _, _, _) = MakeService(newPlan: planInactivo);

        var result = await svc.SolicitarCambioAsync(
            ClientId, NewPlanId, null, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("inactivo");
    }

    [Fact]
    public async Task SolicitarCambio_LlamaSaveChangesAntesDeCommit()
    {
        // FIX-A: verifica que AddAsync + SaveChangesAsync se llaman (no solo AddAsync + CommitAsync vacío)
        var (svc, changeRepo, _, _) = MakeService();

        await svc.SolicitarCambioAsync(ClientId, NewPlanId, null, ActorId, "Admin", "127.0.0.1");

        changeRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    // ════════════════════════════════════════════════════════════════════════
    // GetPendientesAsync
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPendientes_RetornaListaTipada()
    {
        var oldPlan = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var newPlan = MakePlan(NewPlanId, "Plan Plata", 150m);
        var cli     = MakeClient(OldPlanId, oldPlan);

        var pending = new PlanChangeRequest
        {
            Id = Guid.NewGuid(), ClientId = ClientId, Client = cli,
            OldPlanId = OldPlanId, OldPlan = oldPlan,
            NewPlanId = NewPlanId, NewPlan = newPlan,
            Status = PlanChangeStatus.Pendiente,
            EffectiveDate = DateTime.UtcNow.AddMonths(1),
            RequestedAt = DateTime.UtcNow,
        };

        var (svc, _, _, _) = MakeService(changes: [pending]);
        var items = await svc.GetPendientesAsync();

        items.Should().BeOfType<List<PlanChangeItemDto>>();
        items.Should().HaveCount(1);
        items[0].PlanAnterior.Should().Be("Plan Cobre");
        items[0].PlanNuevo.Should().Be("Plan Plata");
    }

    [Fact]
    public async Task GetPendientes_FiltradoPorClientId()
    {
        var otroId = Guid.NewGuid();
        var p1 = new PlanChangeRequest
        {
            ClientId = ClientId, Status = PlanChangeStatus.Pendiente,
            OldPlanId = OldPlanId, NewPlanId = NewPlanId,
            EffectiveDate = DateTime.UtcNow.AddMonths(1), RequestedAt = DateTime.UtcNow,
        };
        var p2 = new PlanChangeRequest
        {
            ClientId = otroId, Status = PlanChangeStatus.Pendiente,
            OldPlanId = OldPlanId, NewPlanId = NewPlanId,
            EffectiveDate = DateTime.UtcNow.AddMonths(1), RequestedAt = DateTime.UtcNow,
        };
        var (svc, _, _, _) = MakeService(changes: [p1, p2]);

        var items = await svc.GetPendientesAsync(clientId: ClientId);
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPendientes_ExcluyeAprobadosYRechazados()
    {
        var aprobado  = new PlanChangeRequest { ClientId = ClientId, Status = PlanChangeStatus.Aprobado,  OldPlanId = OldPlanId, NewPlanId = NewPlanId, EffectiveDate = DateTime.UtcNow, RequestedAt = DateTime.UtcNow };
        var rechazado = new PlanChangeRequest { ClientId = ClientId, Status = PlanChangeStatus.Rechazado, OldPlanId = OldPlanId, NewPlanId = NewPlanId, EffectiveDate = DateTime.UtcNow, RequestedAt = DateTime.UtcNow };
        var (svc, _, _, _) = MakeService(changes: [aprobado, rechazado]);

        var items = await svc.GetPendientesAsync();
        items.Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════
    // GetHistorialAsync (FIX-F)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetHistorial_RetornaTodosLosEstados()
    {
        var oldPlan = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var newPlan = MakePlan(NewPlanId, "Plan Plata", 150m);

        var aprobado  = new PlanChangeRequest { ClientId = ClientId, Status = PlanChangeStatus.Aprobado,  OldPlan = oldPlan, NewPlan = newPlan, OldPlanId = OldPlanId, NewPlanId = NewPlanId, EffectiveDate = DateTime.UtcNow, RequestedAt = DateTime.UtcNow.AddDays(-10) };
        var rechazado = new PlanChangeRequest { ClientId = ClientId, Status = PlanChangeStatus.Rechazado, OldPlan = oldPlan, NewPlan = newPlan, OldPlanId = OldPlanId, NewPlanId = NewPlanId, EffectiveDate = DateTime.UtcNow, RequestedAt = DateTime.UtcNow.AddDays(-5), RejectionReason = "No disponible" };
        var pendiente = new PlanChangeRequest { ClientId = ClientId, Status = PlanChangeStatus.Pendiente, OldPlan = oldPlan, NewPlan = newPlan, OldPlanId = OldPlanId, NewPlanId = NewPlanId, EffectiveDate = DateTime.UtcNow.AddMonths(1), RequestedAt = DateTime.UtcNow };

        var (svc, _, _, _) = MakeService(changes: [aprobado, rechazado, pendiente]);

        var historial = await svc.GetHistorialAsync(ClientId);

        historial.Should().HaveCount(3);
        historial.Should().Contain(h => h.Status == "Aprobado");
        historial.Should().Contain(h => h.Status == "Rechazado" && h.MotivoRechazo == "No disponible");
        historial.Should().Contain(h => h.Status == "Pendiente");
    }

    [Fact]
    public async Task GetHistorial_SoloDevuelveDelClienteIndicado()
    {
        var otroId = Guid.NewGuid();
        var mio   = new PlanChangeRequest { ClientId = ClientId, Status = PlanChangeStatus.Aprobado, OldPlanId = OldPlanId, NewPlanId = NewPlanId, EffectiveDate = DateTime.UtcNow, RequestedAt = DateTime.UtcNow };
        var otro  = new PlanChangeRequest { ClientId = otroId,   Status = PlanChangeStatus.Aprobado, OldPlanId = OldPlanId, NewPlanId = NewPlanId, EffectiveDate = DateTime.UtcNow, RequestedAt = DateTime.UtcNow };
        var (svc, _, _, _) = MakeService(changes: [mio, otro]);

        var historial = await svc.GetHistorialAsync(ClientId);
        historial.Should().HaveCount(1);
    }

    // ════════════════════════════════════════════════════════════════════════
    // AprobarCambioAsync — Fin de mes
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AprobarCambio_FinDeMes_NoCreaFacturas()
    {
        var cambioId = Guid.NewGuid();
        var oldPlan  = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var newPlan  = MakePlan(NewPlanId, "Plan Plata", 150m);
        var cli      = MakeClient(OldPlanId, oldPlan);

        var pending = new PlanChangeRequest
        {
            Id = cambioId, ClientId = ClientId, Client = cli,
            NewPlanId = NewPlanId, NewPlan = newPlan,
            Status = PlanChangeStatus.Pendiente,
            EffectiveDate = DateTime.UtcNow.AddMonths(1),
        };

        var (svc, changeRepo, invoiceRepo, _) = MakeService(changes: [pending]);

        var result = await svc.AprobarCambioAsync(
            cambioId, midMonth: false, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        invoiceRepo.Verify(r => r.AddAsync(It.IsAny<Invoice>()), Times.Never);
        // FIX-A: debe llamar MarkModifiedAsync, NO UpdateAsync directamente
        changeRepo.Verify(r => r.MarkModifiedAsync(
            It.Is<PlanChangeRequest>(c => c.Status == PlanChangeStatus.Aprobado)),
            Times.Once);
    }

    [Fact]
    public async Task AprobarCambio_FinDeMes_LlamaSaveChangesUnaVez()
    {
        // FIX-A: un único SaveChangesAsync antes del CommitAsync (no múltiples intermedios)
        var cambioId = Guid.NewGuid();
        var oldPlan  = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var newPlan  = MakePlan(NewPlanId, "Plan Plata", 150m);
        var cli      = MakeClient(OldPlanId, oldPlan);
        var pending  = new PlanChangeRequest { Id = cambioId, ClientId = ClientId, Client = cli, NewPlanId = NewPlanId, NewPlan = newPlan, Status = PlanChangeStatus.Pendiente, EffectiveDate = DateTime.UtcNow.AddMonths(1) };

        var (svc, changeRepo, _, _) = MakeService(changes: [pending]);

        await svc.AprobarCambioAsync(cambioId, midMonth: false, ActorId, "Admin", "127.0.0.1");

        changeRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task AprobarCambio_FinDeMes_EnviaNotificacionAlCliente()
    {
        // FIX-D: notificación CAMBIO_PLAN enviada tras aprobar
        var cambioId = Guid.NewGuid();
        var oldPlan  = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var newPlan  = MakePlan(NewPlanId, "Plan Plata", 150m);
        var cli      = MakeClient(OldPlanId, oldPlan);
        var pending  = new PlanChangeRequest { Id = cambioId, ClientId = ClientId, Client = cli, NewPlanId = NewPlanId, NewPlan = newPlan, Status = PlanChangeStatus.Pendiente, EffectiveDate = DateTime.UtcNow.AddMonths(1) };

        var (svc, _, _, notifMock) = MakeService(changes: [pending]);

        await svc.AprobarCambioAsync(cambioId, midMonth: false, ActorId, "Admin", "127.0.0.1");

        notifMock.Verify(n => n.PublishAsync(
            NotifType.CAMBIO_PLAN, ClientId,
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()),
            Times.Once);
    }

    // ════════════════════════════════════════════════════════════════════════
    // AprobarCambioAsync — Mitad de mes (FIX-A crítico)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AprobarCambio_MidMonth_SinFacturaPendiente_CreaDosProporcionales()
    {
        // Escenario: no hay factura mensual activa → crea proporcional viejo plan + nuevo plan
        var cambioId = Guid.NewGuid();
        var oldPlan  = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var newPlan  = MakePlan(NewPlanId, "Plan Plata", 150m);
        var cli      = MakeClient(OldPlanId, oldPlan);
        var pending  = new PlanChangeRequest { Id = cambioId, ClientId = ClientId, Client = cli, NewPlanId = NewPlanId, NewPlan = newPlan, Status = PlanChangeStatus.Pendiente, EffectiveDate = DateTime.UtcNow.AddMonths(1) };

        var (svc, changeRepo, invoiceRepo, _) = MakeService(changes: [pending]);

        var result = await svc.AprobarCambioAsync(cambioId, midMonth: true, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        changeRepo.Verify(r => r.MarkModifiedAsync(
            It.Is<PlanChangeRequest>(c => c.Status == PlanChangeStatus.Aprobado && c.MidMonthChange)),
            Times.Once);

        // Si daysPlanOld > 0 se crean 2 facturas (viejo plan + nuevo plan)
        // Si daysPlanOld == 0 (día 1 del mes) solo se crea 1 (nuevo plan)
        var hoy = DateTime.UtcNow;
        var expectedInvoices = hoy.Day > 1 ? 2 : 1;
        invoiceRepo.Verify(r => r.AddAsync(It.IsAny<Invoice>()), Times.Exactly(expectedInvoices));
    }

    [Fact]
    public async Task AprobarCambio_MidMonth_ConFacturaPendiente_AnulaYCreaProporcionales()
    {
        // FIX-A: el escenario que causaba el bug — hay factura mensual que anular
        var cambioId = Guid.NewGuid();
        var oldPlan  = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var newPlan  = MakePlan(NewPlanId, "Plan Plata", 150m);
        var cli      = MakeClient(OldPlanId, oldPlan);

        var hoy = DateTime.UtcNow;
        var facturaActual = new Invoice
        {
            Id       = Guid.NewGuid(),
            ClientId = ClientId,
            Type     = InvoiceType.Mensualidad,
            Status   = InvoiceStatus.Pendiente,
            Year     = hoy.Year,
            Month    = hoy.Month,
            Amount   = 100m,
            IssuedAt = hoy.AddDays(-5),
            DueDate  = hoy.AddDays(5),
        };

        var pending = new PlanChangeRequest
        {
            Id = cambioId, ClientId = ClientId, Client = cli,
            NewPlanId = NewPlanId, NewPlan = newPlan,
            Status = PlanChangeStatus.Pendiente,
            EffectiveDate = hoy.AddMonths(1),
        };

        var (svc, changeRepo, invoiceRepo, _) = MakeService(changes: [pending], invoices: [facturaActual]);

        var result = await svc.AprobarCambioAsync(cambioId, midMonth: true, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();

        // Factura actual debe quedar Anulada (via MarkModifiedAsync, no UpdateAsync)
        invoiceRepo.Verify(r => r.MarkModifiedAsync(
            It.Is<Invoice>(i => i.Id == facturaActual.Id && i.Status == InvoiceStatus.Anulada)),
            Times.Once);

        // No debe llamar UpdateAsync en lugar de MarkModifiedAsync (FIX-A)
        invoiceRepo.Verify(r => r.UpdateAsync(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task AprobarCambio_MidMonth_LlamaSaveChangesUnaVez()
    {
        // FIX-A: aun con factura que anular, solo un SaveChangesAsync al final
        var cambioId = Guid.NewGuid();
        var oldPlan  = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var newPlan  = MakePlan(NewPlanId, "Plan Plata", 150m);
        var cli      = MakeClient(OldPlanId, oldPlan);
        var hoy      = DateTime.UtcNow;
        var factura  = new Invoice { Id = Guid.NewGuid(), ClientId = ClientId, Type = InvoiceType.Mensualidad, Status = InvoiceStatus.Pendiente, Year = hoy.Year, Month = hoy.Month, Amount = 100m, IssuedAt = hoy, DueDate = hoy.AddDays(5) };
        var pending  = new PlanChangeRequest { Id = cambioId, ClientId = ClientId, Client = cli, NewPlanId = NewPlanId, NewPlan = newPlan, Status = PlanChangeStatus.Pendiente, EffectiveDate = hoy.AddMonths(1) };

        var (svc, changeRepo, _, _) = MakeService(changes: [pending], invoices: [factura]);

        await svc.AprobarCambioAsync(cambioId, midMonth: true, ActorId, "Admin", "127.0.0.1");

        // FIX-A: exactamente 1 SaveChangesAsync, no uno por cada UpdateAsync
        changeRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task AprobarCambio_YaProcesado_RetornaError()
    {
        var cambioId = Guid.NewGuid();
        var cambio   = new PlanChangeRequest { Id = cambioId, ClientId = ClientId, Status = PlanChangeStatus.Aprobado, OldPlanId = OldPlanId, NewPlanId = NewPlanId };
        var (svc, _, _, _) = MakeService(changes: [cambio]);

        var result = await svc.AprobarCambioAsync(cambioId, false, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("procesada");
    }

    // ════════════════════════════════════════════════════════════════════════
    // RechazarCambioAsync
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RechazarCambio_ActualizaEstadoYMotivo()
    {
        var cambioId = Guid.NewGuid();
        var pending  = new PlanChangeRequest { Id = cambioId, ClientId = ClientId, Status = PlanChangeStatus.Pendiente, OldPlanId = OldPlanId, NewPlanId = NewPlanId };
        var (svc, changeRepo, _, _) = MakeService(changes: [pending]);

        var result = await svc.RechazarCambioAsync(
            cambioId, "No cumple requisitos", ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        // FIX-A: MarkModifiedAsync en lugar de UpdateAsync
        changeRepo.Verify(r => r.MarkModifiedAsync(
            It.Is<PlanChangeRequest>(c =>
                c.Status          == PlanChangeStatus.Rechazado &&
                c.RejectionReason == "No cumple requisitos")),
            Times.Once);
    }

    [Fact]
    public async Task RechazarCambio_LlamaSaveChangesUnaVez()
    {
        var cambioId = Guid.NewGuid();
        var pending  = new PlanChangeRequest { Id = cambioId, ClientId = ClientId, Status = PlanChangeStatus.Pendiente, OldPlanId = OldPlanId, NewPlanId = NewPlanId };
        var (svc, changeRepo, _, _) = MakeService(changes: [pending]);

        await svc.RechazarCambioAsync(cambioId, "Motivo", ActorId, "Admin", "127.0.0.1");

        changeRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task RechazarCambio_EnviaNotificacionAlCliente()
    {
        // FIX-D: notificación cuando el cliente tiene PhoneMain cargado
        var cambioId = Guid.NewGuid();
        var oldPlan  = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var cli      = MakeClient(OldPlanId, oldPlan);
        var pending  = new PlanChangeRequest { Id = cambioId, ClientId = ClientId, Client = cli, Status = PlanChangeStatus.Pendiente, OldPlanId = OldPlanId, NewPlanId = NewPlanId };
        var (svc, _, _, notifMock) = MakeService(changes: [pending]);

        await svc.RechazarCambioAsync(cambioId, "Motivo de prueba", ActorId, "Admin", "127.0.0.1");

        notifMock.Verify(n => n.PublishAsync(
            NotifType.CAMBIO_PLAN, ClientId,
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()),
            Times.Once);
    }

    [Fact]
    public async Task RechazarCambio_YaProcesado_RetornaError()
    {
        var cambioId = Guid.NewGuid();
        var cambio   = new PlanChangeRequest { Id = cambioId, ClientId = ClientId, Status = PlanChangeStatus.Rechazado, OldPlanId = OldPlanId, NewPlanId = NewPlanId };
        var (svc, _, _, _) = MakeService(changes: [cambio]);

        var result = await svc.RechazarCambioAsync(cambioId, "Motivo", ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
    }
}
