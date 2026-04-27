using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Invoices;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

public class BillingServiceTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid PlanId   = Guid.NewGuid();
    private const decimal MonthlyPrice    = 150m;

    private static Client MakeClient(DateTime installDate) => new()
    {
        Id               = ClientId,
        TbnCode          = "TBN-001",
        FullName         = "Test Cliente",
        PlanId           = PlanId,
        Plan             = new Plan { Id = PlanId, Name = "Plan Test", MonthlyPrice = MonthlyPrice },
        Status           = ClientStatus.Activo,
        InstallationDate = installDate,
    };

    // BUG #3 FIX: AuditService ahora requiere ILogger<AuditService> — usar NullLogger
    private static AuditService MakeAudit() =>
        new AuditService(RepoMock.Empty<AuditLog>().Object,
                         NullLogger<AuditService>.Instance);

    private static Mock<IInvoiceNumberService> MakeInvNumSvc()
    {
        var mock = new Mock<IInvoiceNumberService>();
        // Devuelve números correlativos secuenciales — suficiente para tests unitarios
        var counter = 0;
        mock.Setup(s => s.NextInvoiceNumberAsync(It.IsAny<bool>()))
            .ReturnsAsync(() => $"F-TEST-{++counter:D4}");
        return mock;
    }

    private static BillingService MakeService(IEnumerable<Invoice>? existing = null)
    {
        var invoiceRepo = RepoMock.Of(existing?.ToArray() ?? Array.Empty<Invoice>());
        return new BillingService(
            RepoMock.Empty<Client>().Object,
            invoiceRepo.Object,
            MakeAudit(),
            NullLogger<BillingService>.Instance,
            MakeInvNumSvc().Object);
    }

    // Test 1: calculo proporcional correcto para distintos dias
    [Theory]
    [InlineData(1,  31, 150.00)]
    [InlineData(15, 31, 82.26)]
    [InlineData(28, 28, 14.46)]
    [InlineData(31, 31, 4.84)]
    public void ProportionalAmount_IsCalculatedCorrectly(
        int installDay, int daysInMonth, decimal expected)
    {
        var remaining = daysInMonth - installDay + 1;
        var actual    = Math.Round(MonthlyPrice * remaining / daysInMonth, 2);
        actual.Should().Be(expected, $"dia {installDay} de mes de {daysInMonth} dias");
    }

    // Test 2: backfill no duplica meses ya existentes
    [Fact]
    public async Task GenerateBackfill_SkipsExistingMonths()
    {
        var installDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1,
                                       0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        var client = MakeClient(installDate);

        var existing = new Invoice
        {
            Id = Guid.NewGuid(), ClientId = ClientId,
            Year = installDate.Year, Month = installDate.Month,
            Type = InvoiceType.Mensualidad, Amount = MonthlyPrice,
            Status = InvoiceStatus.Pagada, IssuedAt = DateTime.UtcNow,
            DueDate = DateTime.UtcNow,
        };

        var invoiceRepo = RepoMock.Of(existing);
        var svc = new BillingService(
            RepoMock.Empty<Client>().Object, invoiceRepo.Object,
            MakeAudit(), NullLogger<BillingService>.Instance,
            MakeInvNumSvc().Object);

        await svc.GenerateBackfillInvoicesAsync(client, MonthlyPrice);

        // No debe duplicar el mes existente
        invoiceRepo.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<Invoice>>(inv =>
                !inv.Any(i => i.Year == installDate.Year && i.Month == installDate.Month))),
            Times.Once);
    }

    // Test 3: usa bulk insert (AddRangeAsync, nunca AddAsync individual).
    // GenerateBackfillInvoicesAsync NO llama SaveChangesAsync propio — el llamador
    // (ClientService.RegisterAsync via IUnitOfWork) maneja el commit atómico.
    [Fact]
    public async Task GenerateBackfill_UsesBulkInsert()
    {
        var installDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1,
                                       0, 0, 0, DateTimeKind.Utc).AddMonths(-2);
        var client      = MakeClient(installDate);
        var invoiceRepo = RepoMock.Empty<Invoice>();
        var svc = new BillingService(
            RepoMock.Empty<Client>().Object, invoiceRepo.Object,
            MakeAudit(), NullLogger<BillingService>.Instance,
            MakeInvNumSvc().Object);

        await svc.GenerateBackfillInvoicesAsync(client, MonthlyPrice);

        invoiceRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Invoice>>()), Times.Once);
        // SaveChangesAsync NO debe llamarse aquí — responsabilidad del llamador vía IUnitOfWork
        invoiceRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
        invoiceRepo.Verify(r => r.AddAsync(It.IsAny<Invoice>()), Times.Never);
    }

    // Test 4: cliente instalado este mes crea exactamente 1 factura proporcional
    [Fact]
    public async Task GenerateBackfill_NewClient_CreatesOneInvoice()
    {
        var today  = DateTime.UtcNow;
        var client = MakeClient(new DateTime(today.Year, today.Month, today.Day,
                                             0, 0, 0, DateTimeKind.Utc));
        var invoiceRepo = RepoMock.Empty<Invoice>();
        var svc = new BillingService(
            RepoMock.Empty<Client>().Object, invoiceRepo.Object,
            MakeAudit(), NullLogger<BillingService>.Instance,
            MakeInvNumSvc().Object);

        await svc.GenerateBackfillInvoicesAsync(client, MonthlyPrice);

        invoiceRepo.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<Invoice>>(inv => inv.Count() == 1)), Times.Once);
    }
}
