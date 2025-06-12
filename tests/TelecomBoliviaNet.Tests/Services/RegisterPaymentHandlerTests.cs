using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.Clients.Commands;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// FIX-17: Verifica que RegisterPaymentHandler valida que todas las
/// facturas del DTO pertenecen al ClientId del pago (ownership cross-check).
/// </summary>
public class RegisterPaymentHandlerTests
{
    private static readonly Guid ActorId   = Guid.NewGuid();
    private const string ActorName = "Admin";
    private const string Ip        = "127.0.0.1";

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static RegisterPaymentHandler BuildHandler(
        Client client, params Invoice[] invoices)
    {
        var clientRepo  = RepoMock.Of(client);
        var invoiceRepo = RepoMock.Of(invoices);
        var paymentRepo = RepoMock.Empty<Payment>();
        var piRepo      = RepoMock.Empty<PaymentInvoice>();

        var auditRepo  = RepoMock.Empty<AuditLog>();
        var auditSvc   = new AuditService(auditRepo.Object,
                             NullLogger<AuditService>.Instance);
        var notifMock  = new Mock<INotifPublisher>();
        notifMock.Setup(n => n.PublishAsync(
            It.IsAny<NotifType>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Guid?>()))
            .Returns(Task.CompletedTask);

        return new RegisterPaymentHandler(
            clientRepo.Object, invoiceRepo.Object,
            paymentRepo.Object, piRepo.Object,
            auditSvc, notifMock.Object);
    }

    private static RegisterPaymentDto MakeDto(
        Guid clientId, params Guid[] invoiceIds)
        => new(
            ClientId:              clientId,
            Amount:                150m,
            Method:                "Efectivo",
            Bank:                  null,
            PaidAt:                DateTime.UtcNow,
            PhysicalReceiptNumber: null,
            InvoiceIds:            invoiceIds.ToList(),
            ConfirmedDuplicate:    null,
            BankReference:         null);

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_RejectsInvoicesBelongingToOtherClient()
    {
        var clientId      = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();

        var client  = TestEntityFactory.MakeClient(id: clientId);
        var invoice = new Invoice
        {
            Id       = Guid.NewGuid(),
            ClientId = otherClientId,   // pertenece a OTRO cliente
            Status   = InvoiceStatus.Pendiente,
            Amount   = 150m,
            Year     = 2025,
            Month    = 1,
            Type     = InvoiceType.Mensualidad,
        };

        var handler = BuildHandler(client, invoice);
        var dto     = MakeDto(clientId, invoice.Id);
        var cmd     = new RegisterPaymentCommand(dto, ActorId, ActorName, Ip);

        var result  = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pertenec");
    }

    [Fact]
    public async Task Handle_RejectsWhenInvoiceListContainsMixOfOwnAndForeign()
    {
        var clientId      = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();

        var client = TestEntityFactory.MakeClient(id: clientId);
        var ownInvoice = new Invoice
        {
            Id       = Guid.NewGuid(),
            ClientId = clientId,
            Status   = InvoiceStatus.Pendiente,
            Amount   = 150m,
            Year     = 2025,
            Month    = 1,
            Type     = InvoiceType.Mensualidad,
        };
        var foreignInvoice = new Invoice
        {
            Id       = Guid.NewGuid(),
            ClientId = otherClientId,
            Status   = InvoiceStatus.Pendiente,
            Amount   = 100m,
            Year     = 2025,
            Month    = 2,
            Type     = InvoiceType.Mensualidad,
        };

        var handler = BuildHandler(client, ownInvoice, foreignInvoice);
        var dto     = MakeDto(clientId, ownInvoice.Id, foreignInvoice.Id);
        var cmd     = new RegisterPaymentCommand(dto, ActorId, ActorName, Ip);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_RejectsInvoiceIdsNotFoundInDb()
    {
        var clientId = Guid.NewGuid();
        var client   = TestEntityFactory.MakeClient(id: clientId);
        var ghostId  = Guid.NewGuid(); // ID que no existe

        var handler = BuildHandler(client); // sin facturas en repo
        var dto     = MakeDto(clientId, ghostId);
        var cmd     = new RegisterPaymentCommand(dto, ActorId, ActorName, Ip);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_RejectsAlreadyPaidInvoice()
    {
        var clientId = Guid.NewGuid();
        var client   = TestEntityFactory.MakeClient(id: clientId);
        var invoice  = new Invoice
        {
            Id       = Guid.NewGuid(),
            ClientId = clientId,
            Status   = InvoiceStatus.Pagada,   // ya pagada
            Amount   = 150m,
            Year     = 2025,
            Month    = 1,
            Type     = InvoiceType.Mensualidad,
        };

        var handler = BuildHandler(client, invoice);
        var dto     = MakeDto(clientId, invoice.Id);
        var cmd     = new RegisterPaymentCommand(dto, ActorId, ActorName, Ip);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pagad");
    }

    [Fact]
    public async Task Handle_SucceedsWithValidOwnershipAndPendingInvoice()
    {
        var clientId = Guid.NewGuid();
        var client   = TestEntityFactory.MakeClient(id: clientId);
        var invoice  = new Invoice
        {
            Id       = Guid.NewGuid(),
            ClientId = clientId,            // mismo cliente
            Status   = InvoiceStatus.Pendiente,
            Amount   = 150m,
            Year     = 2025,
            Month    = 1,
            Type     = InvoiceType.Mensualidad,
        };

        var handler = BuildHandler(client, invoice);
        var dto     = MakeDto(clientId, invoice.Id);
        var cmd     = new RegisterPaymentCommand(dto, ActorId, ActorName, Ip);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
