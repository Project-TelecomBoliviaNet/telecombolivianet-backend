using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Plans;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Plans;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Pruebas unitarias para PlanService.
/// Cubre: CreateAsync (duplicado), UpdateAsync (FIX-C bloqueo desactivación, cambio de precio).
/// </summary>
public class PlanServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private const string AdminName = "admin";
    private const string Ip        = "127.0.0.1";

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Plan MakePlan(
        Guid?   id       = null,
        string  name     = "Plan Plata",
        int     speed    = 50,
        decimal price    = 150m,
        bool    isActive = true) =>
        new()
        {
            Id           = id ?? Guid.NewGuid(),
            Name         = name,
            SpeedMb      = speed,
            MonthlyPrice = price,
            IsActive     = isActive,
            CreatedAt    = DateTime.UtcNow
        };

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

    private static (PlanService svc,
                    Mock<IGenericRepository<Plan>>        planRepo,
                    Mock<IGenericRepository<Client>>      clientRepo,
                    Mock<IGenericRepository<PlanHistory>> histRepo,
                    Mock<INotifPublisher>                 notif)
        MakeService(Plan[]? planes = null, Client[]? clientes = null, PlanHistory[]? historial = null)
    {
        var planRepo   = RepoMock.Of(planes    ?? []);
        var clientRepo = RepoMock.Of(clientes  ?? []);
        var histRepo   = RepoMock.Of(historial ?? []);
        var notif      = MakeNotif();
        var svc        = new PlanService(
            planRepo.Object, clientRepo.Object, histRepo.Object, MakeAudit(), notif.Object);
        return (svc, planRepo, clientRepo, histRepo, notif);
    }

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PlanNuevo_RetornaSuccess()
    {
        var (svc, _, _, _, _) = MakeService();
        var dto = new CreatePlanDto("Plan Oro", 80, 200m);

        var result = await svc.CreateAsync(dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Plan Oro");
        result.Value.SpeedMb.Should().Be(80);
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_NombreYVelocidadDuplicados_RetornaFailure()
    {
        var planExistente = MakePlan(name: "Plan Plata", speed: 50);
        var (svc, _, _, _, _) = MakeService(planes: [planExistente]);
        var dto = new CreatePlanDto("Plan Plata", 50, 160m);

        var result = await svc.CreateAsync(dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Plan Plata");
    }

    [Fact]
    public async Task CreateAsync_MismoNombreDistintaVelocidad_Permite()
    {
        var planExistente = MakePlan(name: "Plan Plata", speed: 50);
        var (svc, _, _, _, _) = MakeService(planes: [planExistente]);
        var dto = new CreatePlanDto("Plan Plata", 100, 250m);

        var result = await svc.CreateAsync(dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeTrue();
    }

    // ── UpdateAsync — FIX-C bloqueo desactivación ────────────────────────────

    [Fact]
    public async Task UpdateAsync_PlanNoEncontrado_RetornaFailure()
    {
        var (svc, _, _, _, _) = MakeService();
        var dto = new UpdatePlanDto("Plan X", 50, 150m, false);

        var result = await svc.UpdateAsync(Guid.NewGuid(), dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no encontrado");
    }

    /// <summary>
    /// FIX-C: No se puede desactivar un plan que tiene clientes activos.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_DesactivarPlanConClientesActivos_RetornaFailure()
    {
        var planId  = Guid.NewGuid();
        var plan    = MakePlan(id: planId, isActive: true);
        var cliente = TestEntityFactory.MakeClient(planId: planId, status: ClientStatus.Activo);
        var (svc, _, _, _, _) = MakeService(planes: [plan], clientes: [cliente]);

        var dto = new UpdatePlanDto(plan.Name, plan.SpeedMb, plan.MonthlyPrice, IsActive: false);
        var result = await svc.UpdateAsync(planId, dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("1 cliente(s)");
        result.ErrorMessage.Should().Contain(plan.Name);
        result.ErrorMessage.Should().Contain("Migre los clientes");
    }

    /// <summary>
    /// FIX-C: Clientes suspendidos también bloquean la desactivación del plan.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_DesactivarPlanConClientesSuspendidos_RetornaFailure()
    {
        var planId     = Guid.NewGuid();
        var plan       = MakePlan(id: planId, isActive: true);
        var suspendido = TestEntityFactory.MakeClient(planId: planId, status: ClientStatus.Suspendido);
        var (svc, _, _, _, _) = MakeService(planes: [plan], clientes: [suspendido]);

        var dto    = new UpdatePlanDto(plan.Name, plan.SpeedMb, plan.MonthlyPrice, IsActive: false);
        var result = await svc.UpdateAsync(planId, dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("1 cliente(s)");
    }

    /// <summary>
    /// FIX-C: Clientes dados de baja NO bloquean la desactivación.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_DesactivarPlanSoloClientesDadosDeBaja_Permite()
    {
        var planId   = Guid.NewGuid();
        var plan     = MakePlan(id: planId, isActive: true);
        var dado     = TestEntityFactory.MakeClient(planId: planId, status: ClientStatus.DadoDeBaja);
        var (svc, _, _, _, _) = MakeService(planes: [plan], clientes: [dado]);

        var dto    = new UpdatePlanDto(plan.Name, plan.SpeedMb, plan.MonthlyPrice, IsActive: false);
        var result = await svc.UpdateAsync(planId, dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// FIX-C: Plan ya desactivado — no se chequea conteo de clientes (idempotente).
    /// </summary>
    [Fact]
    public async Task UpdateAsync_PlanYaInactivo_NoVerificaClientes_Permite()
    {
        var planId  = Guid.NewGuid();
        var plan    = MakePlan(id: planId, isActive: false);
        // hay un cliente activo, pero el plan ya estaba inactivo — no debe bloquearse
        var cliente = TestEntityFactory.MakeClient(planId: planId, status: ClientStatus.Activo);
        var (svc, _, _, _, _) = MakeService(planes: [plan], clientes: [cliente]);

        var dto    = new UpdatePlanDto(plan.Name, plan.SpeedMb, plan.MonthlyPrice, IsActive: false);
        var result = await svc.UpdateAsync(planId, dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_ActivarPlanInactivo_Permite()
    {
        var plan = MakePlan(isActive: false);
        var (svc, _, _, _, _) = MakeService(planes: [plan]);

        var dto    = new UpdatePlanDto(plan.Name, plan.SpeedMb, plan.MonthlyPrice, IsActive: true);
        var result = await svc.UpdateAsync(plan.Id, dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_SinCambios_Permite()
    {
        var plan = MakePlan();
        var (svc, _, _, _, _) = MakeService(planes: [plan]);

        var dto    = new UpdatePlanDto(plan.Name, plan.SpeedMb, plan.MonthlyPrice, plan.IsActive);
        var result = await svc.UpdateAsync(plan.Id, dto, AdminId, AdminName, Ip);

        result.IsSuccess.Should().BeTrue();
    }

    // ── UpdateAsync — notificación cambio de precio ──────────────────────────

    [Fact]
    public async Task UpdateAsync_CambioPrecio_NotificaClientesActivos()
    {
        var planId  = Guid.NewGuid();
        var plan    = MakePlan(id: planId, price: 150m);
        var cliente = TestEntityFactory.MakeClient(planId: planId, status: ClientStatus.Activo);
        var (svc, _, _, _, notif) = MakeService(planes: [plan], clientes: [cliente]);

        var dto = new UpdatePlanDto(plan.Name, plan.SpeedMb, 200m, plan.IsActive);
        await svc.UpdateAsync(planId, dto, AdminId, AdminName, Ip);

        notif.Verify(n => n.PublishAsync(
            NotifType.CAMBIO_PRECIO,
            cliente.Id,
            cliente.PhoneMain,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Guid?>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_CambioPrecio_NoNotificaClientesDadosDeBaja()
    {
        var planId  = Guid.NewGuid();
        var plan    = MakePlan(id: planId, price: 150m);
        var dado    = TestEntityFactory.MakeClient(planId: planId, status: ClientStatus.DadoDeBaja);
        var (svc, _, _, _, notif) = MakeService(planes: [plan], clientes: [dado]);

        var dto = new UpdatePlanDto(plan.Name, plan.SpeedMb, 200m, plan.IsActive);
        await svc.UpdateAsync(planId, dto, AdminId, AdminName, Ip);

        notif.Verify(n => n.PublishAsync(
            It.IsAny<NotifType>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_SinCambioPrecio_NoNotifica()
    {
        var planId  = Guid.NewGuid();
        var plan    = MakePlan(id: planId, price: 150m);
        var cliente = TestEntityFactory.MakeClient(planId: planId, status: ClientStatus.Activo);
        var (svc, _, _, _, notif) = MakeService(planes: [plan], clientes: [cliente]);

        var dto = new UpdatePlanDto(plan.Name, plan.SpeedMb, 150m, plan.IsActive);
        await svc.UpdateAsync(planId, dto, AdminId, AdminName, Ip);

        notif.Verify(n => n.PublishAsync(
            It.IsAny<NotifType>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Guid?>()), Times.Never);
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_SinFiltro_RetornaTodos()
    {
        var activo   = MakePlan(name: "A", isActive: true);
        var inactivo = MakePlan(name: "B", isActive: false);
        var (svc, _, _, _, _) = MakeService(planes: [activo, inactivo]);

        var result = await svc.GetAllAsync(onlyActive: false);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAsync_OnlyActive_RetornaSoloActivos()
    {
        var activo   = MakePlan(name: "A", speed: 50, isActive: true);
        var inactivo = MakePlan(name: "B", speed: 80, isActive: false);
        var (svc, _, _, _, _) = MakeService(planes: [activo, inactivo]);

        var result = await svc.GetAllAsync(onlyActive: true);

        result.Should().HaveCount(1);
        result.First().Name.Should().Be("A");
    }

    [Fact]
    public async Task GetByIdAsync_Existente_RetornaDto()
    {
        var plan = MakePlan(name: "Plan Cobre", speed: 30, price: 100m);
        var (svc, _, _, _, _) = MakeService(planes: [plan]);

        var result = await svc.GetByIdAsync(plan.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Plan Cobre");
        result.DisplayLabel.Should().Contain("30 Mb");
    }

    [Fact]
    public async Task GetByIdAsync_NoExistente_RetornaNull()
    {
        var (svc, _, _, _, _) = MakeService();

        var result = await svc.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── FIX-H: Historial de versiones ────────────────────────────────────────

    /// <summary>
    /// FIX-H: Al actualizar un plan, debe guardarse un snapshot con los valores ANTERIORES.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_GuardaSnapshotConValoresAnteriores()
    {
        var plan = MakePlan(name: "Plan Plata", speed: 50, price: 149m, isActive: true);
        var (svc, _, _, histRepo, _) = MakeService(planes: [plan]);

        var dto = new UpdatePlanDto("Plan Plata Plus", 60, 169m, true, "Mejora de velocidad");
        await svc.UpdateAsync(plan.Id, dto, AdminId, AdminName, Ip);

        histRepo.Verify(r => r.AddAsync(It.Is<PlanHistory>(h =>
            h.PlanId       == plan.Id   &&
            h.Name         == "Plan Plata" &&
            h.SpeedMb      == 50        &&
            h.MonthlyPrice == 149m      &&
            h.IsActive     == true      &&
            h.ChangedByName == AdminName &&
            h.ChangeReason == "Mejora de velocidad"
        )), Times.Once);
    }

    /// <summary>
    /// FIX-H: El snapshot se guarda incluso cuando solo cambia el IsActive.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_DesactivacionSinClientes_GuardaSnapshot()
    {
        var plan = MakePlan(isActive: true);
        var (svc, _, _, histRepo, _) = MakeService(planes: [plan]);

        var dto = new UpdatePlanDto(plan.Name, plan.SpeedMb, plan.MonthlyPrice, IsActive: false);
        await svc.UpdateAsync(plan.Id, dto, AdminId, AdminName, Ip);

        histRepo.Verify(r => r.AddAsync(It.Is<PlanHistory>(h =>
            h.PlanId   == plan.Id &&
            h.IsActive == true      // valor anterior: activo
        )), Times.Once);
    }

    /// <summary>
    /// FIX-H: Si el plan no existe, NO se crea ningún snapshot.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_PlanInexistente_NoGuardaSnapshot()
    {
        var (svc, _, _, histRepo, _) = MakeService();

        var dto = new UpdatePlanDto("X", 50, 100m, true);
        await svc.UpdateAsync(Guid.NewGuid(), dto, AdminId, AdminName, Ip);

        histRepo.Verify(r => r.AddAsync(It.IsAny<PlanHistory>()), Times.Never);
    }

    /// <summary>
    /// FIX-H: GetHistoryAsync devuelve entradas del repo para el plan indicado.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_RetornaEntradasDelPlan()
    {
        var planId = Guid.NewGuid();
        var h1 = new PlanHistory
        {
            Id = Guid.NewGuid(), PlanId = planId,
            Name = "Plan Plata", SpeedMb = 50, MonthlyPrice = 149m, IsActive = true,
            ChangedAt = DateTime.UtcNow.AddDays(-10),
            ChangedById = AdminId, ChangedByName = AdminName,
        };
        var h2 = new PlanHistory
        {
            Id = Guid.NewGuid(), PlanId = planId,
            Name = "Plan Plata", SpeedMb = 50, MonthlyPrice = 130m, IsActive = true,
            ChangedAt = DateTime.UtcNow.AddDays(-30),
            ChangedById = AdminId, ChangedByName = AdminName,
        };
        var (svc, _, _, _, _) = MakeService(historial: [h1, h2]);

        var result = await svc.GetHistoryAsync(planId);

        result.Should().HaveCount(2);
        result[0].MonthlyPrice.Should().Be(149m);  // más reciente primero
        result[1].MonthlyPrice.Should().Be(130m);
    }

    /// <summary>
    /// FIX-H: GetHistoryAsync no devuelve entradas de otros planes.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_NoMezclaEntrasDeOtrosPlanes()
    {
        var planId      = Guid.NewGuid();
        var otroPlanId  = Guid.NewGuid();
        var h1 = new PlanHistory
        {
            Id = Guid.NewGuid(), PlanId = planId,
            Name = "A", SpeedMb = 50, MonthlyPrice = 149m, IsActive = true,
            ChangedAt = DateTime.UtcNow, ChangedById = AdminId, ChangedByName = AdminName,
        };
        var h2 = new PlanHistory
        {
            Id = Guid.NewGuid(), PlanId = otroPlanId,
            Name = "B", SpeedMb = 80, MonthlyPrice = 200m, IsActive = true,
            ChangedAt = DateTime.UtcNow, ChangedById = AdminId, ChangedByName = AdminName,
        };
        var (svc, _, _, _, _) = MakeService(historial: [h1, h2]);

        var result = await svc.GetHistoryAsync(planId);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("A");
    }

    /// <summary>
    /// FIX-H: GetHistoryAsync retorna lista vacía para plan sin cambios previos.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_PlanSinHistorial_RetornaListaVacia()
    {
        var (svc, _, _, _, _) = MakeService();

        var result = await svc.GetHistoryAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }
}
