using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Payments;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Payments;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests para WhatsAppReceiptService.AssignClientAsync (BUG-09):
///   - Comprobante no existe
///   - Comprobante en estado distinto a Pendiente
///   - Comprobante ya tiene ClientId asignado
///   - Cliente destino no existe
///   - Asignación exitosa — retorna Success
///   - Asignación exitosa — el ClientId queda persistido en el repositorio
/// </summary>
public class WhatsAppReceiptAssignClientTests
{
    private static readonly Guid ActorId   = Guid.NewGuid();
    private const           string ActorName = "Admin Test";
    private const           string Ip        = "127.0.0.1";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WhatsAppReceipt MakeOrphanReceipt(ReceiptQueueStatus status = ReceiptQueueStatus.Pendiente, Guid? clientId = null)
        => new()
        {
            Id         = Guid.NewGuid(),
            ClientId   = clientId,
            Status     = status,
            ReceivedAt = DateTime.UtcNow.AddMinutes(-10),
            ImageUrl   = "/chatbot-uploads/test.jpg",
        };

    private static WhatsAppReceiptService BuildService(
        WhatsAppReceipt? receipt = null,
        Client?          client  = null)
    {
        var receiptRepo = receipt is null ? RepoMock.Empty<WhatsAppReceipt>() : RepoMock.Of(receipt);
        var clientRepo  = client  is null ? RepoMock.Empty<Client>()          : RepoMock.Of(client);
        var auditRepo   = RepoMock.Empty<AuditLog>();
        var audit       = new AuditService(auditRepo.Object, NullLogger<AuditService>.Instance);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return new WhatsAppReceiptService(
            receiptRepo.Object,
            clientRepo.Object,
            RepoMock.Empty<Invoice>().Object,
            RepoMock.Empty<Payment>().Object,
            RepoMock.Empty<PaymentInvoice>().Object,
            new Mock<INotifPublisher>().Object,
            new Mock<IChatbotDirectNotifier>().Object,
            audit,
            uow.Object);
    }

    // ── TC-WA-ASN-01: comprobante no existe ──────────────────────────────────

    [Fact]
    public async Task AssignClientAsync_ComprobanteNoExiste_RetornaFailure()
    {
        var client = TestEntityFactory.MakeClient(fullName: "María López", tbnCode: "TBN-0010");
        var svc    = BuildService(receipt: null, client: client);

        var result = await svc.AssignClientAsync(Guid.NewGuid(), client.Id, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no encontrado");
    }

    // ── TC-WA-ASN-02: comprobante ya procesado (Aprobado) ────────────────────

    [Fact]
    public async Task AssignClientAsync_ComprobanteYaProcesado_RetornaFailure()
    {
        var receipt = MakeOrphanReceipt(status: ReceiptQueueStatus.Aprobado);
        var client  = TestEntityFactory.MakeClient(tbnCode: "TBN-0010");
        var svc     = BuildService(receipt, client);

        var result = await svc.AssignClientAsync(receipt.Id, client.Id, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pendientes");
    }

    // ── TC-WA-ASN-03: comprobante ya tiene cliente asignado ──────────────────

    [Fact]
    public async Task AssignClientAsync_ComprobanteYaTieneCliente_RetornaFailure()
    {
        var existingClientId = Guid.NewGuid();
        var receipt          = MakeOrphanReceipt(clientId: existingClientId);
        var newClient        = TestEntityFactory.MakeClient(tbnCode: "TBN-0020");
        var svc              = BuildService(receipt, newClient);

        var result = await svc.AssignClientAsync(receipt.Id, newClient.Id, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ya tiene un cliente");
    }

    // ── TC-WA-ASN-04: cliente destino no existe en la BD ─────────────────────

    [Fact]
    public async Task AssignClientAsync_ClienteNoExiste_RetornaFailure()
    {
        var receipt         = MakeOrphanReceipt();
        var inexistentId    = Guid.NewGuid();
        var svc             = BuildService(receipt, client: null);

        var result = await svc.AssignClientAsync(receipt.Id, inexistentId, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cliente no encontrado");
    }

    // ── TC-WA-ASN-05: asignación exitosa — retorna Success ───────────────────

    [Fact]
    public async Task AssignClientAsync_ClienteExistenteYComprobanteHuerfano_RetornaSuccess()
    {
        var receipt = MakeOrphanReceipt();
        var client  = TestEntityFactory.MakeClient(fullName: "Carlos Mamani", tbnCode: "TBN-0030");
        var svc     = BuildService(receipt, client);

        var result = await svc.AssignClientAsync(receipt.Id, client.Id, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeTrue();
    }

    // ── TC-WA-ASN-06: asignación exitosa — ClientId persiste ─────────────────

    [Fact]
    public async Task AssignClientAsync_AsignacionExitosa_ClientIdActualizadoEnRepositorio()
    {
        var receipt     = MakeOrphanReceipt();
        var client      = TestEntityFactory.MakeClient(fullName: "Ana Flores", tbnCode: "TBN-0031");
        var receiptRepo = RepoMock.Of(receipt);
        var auditRepo   = RepoMock.Empty<AuditLog>();
        var audit       = new AuditService(auditRepo.Object, NullLogger<AuditService>.Instance);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var svc = new WhatsAppReceiptService(
            receiptRepo.Object,
            RepoMock.Of(client).Object,
            RepoMock.Empty<Invoice>().Object,
            RepoMock.Empty<Payment>().Object,
            RepoMock.Empty<PaymentInvoice>().Object,
            new Mock<INotifPublisher>().Object,
            new Mock<IChatbotDirectNotifier>().Object,
            audit,
            uow.Object);

        await svc.AssignClientAsync(receipt.Id, client.Id, ActorId, ActorName, Ip);

        receiptRepo.Verify(r => r.UpdateAsync(
            It.Is<WhatsAppReceipt>(r => r.Id == receipt.Id && r.ClientId == client.Id)),
            Times.Once);
    }
}
