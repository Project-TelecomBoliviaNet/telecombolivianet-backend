using FluentAssertions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Clients;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
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

    private static Plan MakePlan(Guid id, string name, decimal price) =>
        new() { Id = id, Name = name, MonthlyPrice = price, IsActive = true };

    private static Client MakeClient(Guid planId, Plan plan) => new()
    {
        Id = ClientId, TbnCode = "TBN-100", FullName = "Cliente Prueba",
        PlanId = planId, Plan = plan, Status = ClientStatus.Activo,
        InstallationDate = DateTime.UtcNow.AddMonths(-3),
    };

    // BUG #3 FIX: AuditService ahora requiere ILogger<AuditService> — usar NullLogger
    private static AuditService MakeAudit() =>
        new AuditService(RepoMock.Empty<AuditLog>().Object,
                         NullLogger<AuditService>.Instance);

    // BUG #2 FIX: PlanChangeService ahora requiere IUnitOfWork — mockear con no-op
    private static IUnitOfWork MakeUow()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(default)).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitAsync(default)).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackAsync(default)).Returns(Task.CompletedTask);
        return uow.Object;
    }

    private (PlanChangeService svc,
             Mock<IGenericRepository<PlanChangeRequest>> changeRepo,
             Mock<IGenericRepository<Invoice>> invoiceRepo)
        MakeService(
            IEnumerable<PlanChangeRequest>? changes  = null,
            IEnumerable<Invoice>?           invoices = null,
            Client? client   = null,
            Plan?   newPlan  = null)
    {
        var oldPlan     = MakePlan(OldPlanId, "Plan Cobre", 100m);
        var resolvedNew = newPlan  ?? MakePlan(NewPlanId, "Plan Plata", 150m);
        var resolvedCli = client   ?? MakeClient(OldPlanId, oldPlan);

        var changeRepo  = RepoMock.Of(changes?.ToArray()  ?? Array.Empty<PlanChangeRequest>());
        var clientRepo  = RepoMock.Of(resolvedCli);
        var planRepo    = RepoMock.Of(oldPlan, resolvedNew);
        var invoiceRepo = RepoMock.Of(invoices?.ToArray() ?? Array.Empty<Invoice>());
        var ticketRepo  = RepoMock.Empty<SupportTicket>();

        var svc = new PlanChangeService(
            changeRepo.Object, clientRepo.Object, planRepo.Object,
            invoiceRepo.Object, ticketRepo.Object, MakeAudit(), MakeUow());

        return (svc, changeRepo, invoiceRepo);
    }

    [Fact]
    public async Task SolicitarCambio_CreaRegistroPendiente()
    {
        var (svc, changeRepo, _) = MakeService();

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
    public async Task SolicitarCambio_ConPendienteExistente_RetornaError()
    {
        var pending = new PlanChangeRequest
        {
            Id = Guid.NewGuid(), ClientId = ClientId,
            Status = PlanChangeStatus.Pendiente,
            OldPlanId = OldPlanId, NewPlanId = NewPlanId,
        };
        var (svc, _, _) = MakeService(changes: [pending]);

        var result = await svc.SolicitarCambioAsync(
            ClientId, NewPlanId, null, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pendiente");
    }

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

        var (svc, _, _) = MakeService(changes: [pending]);
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
            Id = Guid.NewGuid(), ClientId = ClientId, Status = PlanChangeStatus.Pendiente,
            OldPlanId = OldPlanId, NewPlanId = NewPlanId,
            EffectiveDate = DateTime.UtcNow.AddMonths(1), RequestedAt = DateTime.UtcNow,
        };
        var p2 = new PlanChangeRequest
        {
            Id = Guid.NewGuid(), ClientId = otroId, Status = PlanChangeStatus.Pendiente,
            OldPlanId = OldPlanId, NewPlanId = NewPlanId,
            EffectiveDate = DateTime.UtcNow.AddMonths(1), RequestedAt = DateTime.UtcNow,
        };
        var (svc, _, _) = MakeService(changes: [p1, p2]);

        var items = await svc.GetPendientesAsync(clientId: ClientId);
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task RechazarCambio_ActualizaEstado()
    {
        var cambioId = Guid.NewGuid();
        var pending  = new PlanChangeRequest
        {
            Id = cambioId, ClientId = ClientId, Status = PlanChangeStatus.Pendiente,
            OldPlanId = OldPlanId, NewPlanId = NewPlanId,
        };
        var (svc, changeRepo, _) = MakeService(changes: [pending]);

        var result = await svc.RechazarCambioAsync(
            cambioId, "No cumple requisitos", ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        changeRepo.Verify(r => r.UpdateAsync(
            It.Is<PlanChangeRequest>(c =>
                c.Status          == PlanChangeStatus.Rechazado &&
                c.RejectionReason == "No cumple requisitos")),
            Times.Once);
    }

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

        var (svc, changeRepo, invoiceRepo) = MakeService(changes: [pending]);

        var result = await svc.AprobarCambioAsync(
            cambioId, midMonth: false, ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        invoiceRepo.Verify(r => r.AddAsync(It.IsAny<Invoice>()), Times.Never);
        changeRepo.Verify(r => r.UpdateAsync(
            It.Is<PlanChangeRequest>(c => c.Status == PlanChangeStatus.Aprobado)),
            Times.Once);
    }
}
