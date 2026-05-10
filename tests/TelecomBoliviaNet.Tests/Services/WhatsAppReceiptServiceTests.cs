using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Payments;
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
/// Tests para la lógica de notificación 23h en WhatsAppReceiptService.ApproveAsync:
///   - Ventana abierta  (< 23h) → chatbot directo, sin outbox
///   - Ventana cerrada  (≥ 23h) → outbox → Python notifier
///   - Chatbot falla         → fallback a outbox
///   - ClientId nulo         → rechazado antes de notificar
///   - Comprobante inexistente → Failure
/// </summary>
public class WhatsAppReceiptServiceTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (Client client, Invoice invoice, WhatsAppReceipt receipt)
        MakeTestData(double receivedHoursAgo)
    {
        var clientId  = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        var client = TestEntityFactory.MakeClient(
            id:        clientId,
            fullName:  "Juan Pérez",
            phoneMain: "59178901234",
            tbnCode:   "TBN-0001");

        var invoice = new Invoice
        {
            Id       = invoiceId,
            ClientId = clientId,
            Type     = InvoiceType.Mensualidad,
            Year     = 2025,
            Month    = 1,
            Status   = InvoiceStatus.Pendiente,
            Amount   = 150m,
        };

        var receipt = new WhatsAppReceipt
        {
            Id         = Guid.NewGuid(),
            ClientId   = clientId,
            Client     = client,
            Status     = ReceiptQueueStatus.Pendiente,
            ReceivedAt = DateTime.UtcNow.AddHours(-receivedHoursAgo),
            ImageUrl   = "/chatbot-uploads/test.jpg",
        };

        return (client, invoice, receipt);
    }

    private static ApproveReceiptDto MakeDto(Guid invoiceId) => new(
        Amount: 150m,
        Method: "Efectivo",
        Bank: null,
        PaidAt: DateTime.UtcNow,
        PhysicalReceiptNumber: null,
        InvoiceIds: new List<Guid> { invoiceId });

    private static (WhatsAppReceiptService svc,
                    Mock<INotifPublisher>        notifMock,
                    Mock<IChatbotDirectNotifier> chatbotMock)
        BuildService(WhatsAppReceipt receipt, Invoice invoice)
    {
        var receiptRepo = RepoMock.Of(receipt);
        var clientRepo  = RepoMock.Empty<Client>();
        var invoiceRepo = RepoMock.Of(invoice);
        var paymentRepo = RepoMock.Empty<Payment>();
        var piRepo      = RepoMock.Empty<PaymentInvoice>();
        var auditRepo   = RepoMock.Empty<AuditLog>();

        var notifMock   = new Mock<INotifPublisher>();
        var chatbotMock = new Mock<IChatbotDirectNotifier>();
        var audit       = new AuditService(auditRepo.Object, NullLogger<AuditService>.Instance);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uowMock.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uowMock.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var svc = new WhatsAppReceiptService(
            receiptRepo.Object,
            clientRepo.Object,
            invoiceRepo.Object,
            paymentRepo.Object,
            piRepo.Object,
            notifMock.Object,
            chatbotMock.Object,
            audit,
            uowMock.Object);

        return (svc, notifMock, chatbotMock);
    }

    // ── Ventana abierta (< 23h) → chatbot directo ─────────────────────────────

    [Fact]
    public async Task ApproveAsync_VentanaAbierta_UsaChatbotDirecto_NoPublicaOutbox()
    {
        var (_, invoice, receipt) = MakeTestData(receivedHoursAgo: 2);
        var (svc, notifMock, chatbotMock) = BuildService(receipt, invoice);

        chatbotMock
            .Setup(x => x.SendPaymentConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var result = await svc.ApproveAsync(receipt.Id, MakeDto(invoice.Id), ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        chatbotMock.Verify(x => x.SendPaymentConfirmationAsync(
            "59178901234", "Juan Pérez", 150m, It.IsAny<string>()), Times.Once);
        notifMock.Verify(x => x.PublishAsync(
            It.IsAny<NotifType>(), It.IsAny<Guid>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()), Times.Never);
    }

    // ── Ventana cerrada (≥ 23h) → outbox ──────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_VentanaCerrada_PublicaOutbox_NoChatbot()
    {
        var (_, invoice, receipt) = MakeTestData(receivedHoursAgo: 25);
        var (svc, notifMock, chatbotMock) = BuildService(receipt, invoice);

        var result = await svc.ApproveAsync(receipt.Id, MakeDto(invoice.Id), ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        chatbotMock.Verify(x => x.SendPaymentConfirmationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        notifMock.Verify(x => x.PublishAsync(
            NotifType.CONFIRMACION_PAGO, It.IsAny<Guid>(), "59178901234",
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()), Times.Once);
    }

    // ── Ventana abierta pero chatbot falla → fallback al outbox ───────────────

    [Fact]
    public async Task ApproveAsync_ChatbotFalla_FallbackAlOutbox()
    {
        var (_, invoice, receipt) = MakeTestData(receivedHoursAgo: 1);
        var (svc, notifMock, chatbotMock) = BuildService(receipt, invoice);

        chatbotMock
            .Setup(x => x.SendPaymentConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var result = await svc.ApproveAsync(receipt.Id, MakeDto(invoice.Id), ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeTrue();
        chatbotMock.Verify(x => x.SendPaymentConfirmationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Once);
        notifMock.Verify(x => x.PublishAsync(
            NotifType.CONFIRMACION_PAGO, It.IsAny<Guid>(), "59178901234",
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()), Times.Once);
    }

    // ── Edge cases: límite exacto ──────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_RecibidoHace22h_TodaviaUsaChatbot()
    {
        var (_, invoice, receipt) = MakeTestData(receivedHoursAgo: 22.9);
        var (svc, notifMock, chatbotMock) = BuildService(receipt, invoice);

        chatbotMock
            .Setup(x => x.SendPaymentConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        await svc.ApproveAsync(receipt.Id, MakeDto(invoice.Id), ActorId, "Admin", "127.0.0.1");

        chatbotMock.Verify(x => x.SendPaymentConfirmationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Once);
        notifMock.Verify(x => x.PublishAsync(
            It.IsAny<NotifType>(), It.IsAny<Guid>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_RecibidoHace23h_UsaOutbox()
    {
        var (_, invoice, receipt) = MakeTestData(receivedHoursAgo: 23.1);
        var (svc, notifMock, chatbotMock) = BuildService(receipt, invoice);

        await svc.ApproveAsync(receipt.Id, MakeDto(invoice.Id), ActorId, "Admin", "127.0.0.1");

        chatbotMock.Verify(x => x.SendPaymentConfirmationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        notifMock.Verify(x => x.PublishAsync(
            NotifType.CONFIRMACION_PAGO, It.IsAny<Guid>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()), Times.Once);
    }

    // ── Sin ClientId identificado → rechazado antes de notificar ─────────────

    [Fact]
    public async Task ApproveAsync_SinClienteIdentificado_RetornaFailure_SinNotificar()
    {
        var (_, invoice, receipt) = MakeTestData(receivedHoursAgo: 2);
        receipt = new WhatsAppReceipt
        {
            Id         = receipt.Id,
            ClientId   = null,
            Client     = null,
            Status     = ReceiptQueueStatus.Pendiente,
            ReceivedAt = receipt.ReceivedAt,
            ImageUrl   = receipt.ImageUrl,
        };
        var (svc, notifMock, chatbotMock) = BuildService(receipt, invoice);

        var result = await svc.ApproveAsync(receipt.Id, MakeDto(invoice.Id), ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        chatbotMock.Verify(x => x.SendPaymentConfirmationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        notifMock.Verify(x => x.PublishAsync(
            It.IsAny<NotifType>(), It.IsAny<Guid>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()), Times.Never);
    }

    // ── Comprobante no existe ──────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_ComprobanteNoExiste_RetornaFailure()
    {
        var (_, invoice, _) = MakeTestData(receivedHoursAgo: 2);
        var notifMock   = new Mock<INotifPublisher>();
        var chatbotMock = new Mock<IChatbotDirectNotifier>();
        var audit       = new AuditService(RepoMock.Empty<AuditLog>().Object, NullLogger<AuditService>.Instance);

        var uowMock2 = new Mock<IUnitOfWork>();
        uowMock2.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uowMock2.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uowMock2.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var svc = new WhatsAppReceiptService(
            RepoMock.Empty<WhatsAppReceipt>().Object,
            RepoMock.Empty<Client>().Object,
            RepoMock.Of(invoice).Object,
            RepoMock.Empty<Payment>().Object,
            RepoMock.Empty<PaymentInvoice>().Object,
            notifMock.Object,
            chatbotMock.Object,
            audit,
            uowMock2.Object);

        var result = await svc.ApproveAsync(Guid.NewGuid(), MakeDto(invoice.Id), ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no encontrado");
    }

    // ── Comprobante ya procesado → rechazado sin re-notificar ─────────────────

    [Fact]
    public async Task ApproveAsync_ComprobanteYaProcesado_RetornaFailure()
    {
        var (_, invoice, receipt) = MakeTestData(receivedHoursAgo: 2);
        receipt = new WhatsAppReceipt
        {
            Id         = receipt.Id,
            ClientId   = receipt.ClientId,
            Client     = receipt.Client,
            Status     = ReceiptQueueStatus.Aprobado,  // ya procesado
            ReceivedAt = receipt.ReceivedAt,
            ImageUrl   = receipt.ImageUrl,
        };
        var (svc, notifMock, chatbotMock) = BuildService(receipt, invoice);

        var result = await svc.ApproveAsync(receipt.Id, MakeDto(invoice.Id), ActorId, "Admin", "127.0.0.1");

        result.IsSuccess.Should().BeFalse();
        chatbotMock.Verify(x => x.SendPaymentConfirmationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        notifMock.Verify(x => x.PublishAsync(
            It.IsAny<NotifType>(), It.IsAny<Guid>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid?>()), Times.Never);
    }
}
