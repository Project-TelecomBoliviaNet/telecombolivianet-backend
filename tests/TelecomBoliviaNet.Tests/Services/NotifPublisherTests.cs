using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Notifications;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests para NotifPublisher.PublishAsync:
///   - Config inactiva o inexistente → no inserta en outbox
///   - Sin teléfono → inserta log OMITIDO, no inserta outbox
///   - Exitoso → inserta outbox con ContextoJson correcto
///   - Contexto vacío → auto-construye via NotifContextBuilderService
///   - Delay → EnviarDesde es en el futuro
///   - Sin delay → EnviarDesde es ahora (±1s)
/// </summary>
public class NotifPublisherTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private const string Phone = "59178901234";

    private static NotifConfig MakeConfig(bool activo = true, int delay = 0, bool inmediato = false,
        NotifType tipo = NotifType.RECORDATORIO_R1)
        => new()
        {
            Id            = Guid.NewGuid(),
            Tipo          = tipo,
            Activo        = activo,
            DelaySegundos = delay,
            Inmediato     = inmediato,
            HoraInicio    = new TimeOnly(8, 0),
            HoraFin       = new TimeOnly(20, 0),
        };

    private static Dictionary<string, string> MakeContexto()
        => new()
        {
            ["nombre"]              = "Carlos",
            ["meses_deuda_detalle"] = "• Enero 2026 - Bs. 150.00",
            ["deuda"]               = "150.00",
        };

    private static (NotifPublisher publisher,
                    Mock<IGenericRepository<NotifOutbox>> outboxMock,
                    Mock<IGenericRepository<NotifLog>>    logMock)
        BuildPublisher(NotifConfig? config, Mock<NotifContextBuilderService>? ctxBuilder = null)
    {
        var configRepo = config is not null
            ? RepoMock.Of(config)
            : RepoMock.Empty<NotifConfig>();

        var outboxMock = RepoMock.Empty<NotifOutbox>();
        var logMock    = RepoMock.Empty<NotifLog>();

        // NotifContextBuilderService tiene varias dependencias — mockeamos el servicio completo
        var contextBuilderMock = ctxBuilder
            ?? new Mock<NotifContextBuilderService>(
                RepoMock.Empty<TelecomBoliviaNet.Domain.Entities.Clients.Client>().Object,
                RepoMock.Empty<TelecomBoliviaNet.Domain.Entities.Clients.Invoice>().Object,
                RepoMock.Empty<TelecomBoliviaNet.Domain.Entities.Clients.ClientQr>().Object,
                RepoMock.Empty<SystemConfig>().Object,
                Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());

        var publisher = new NotifPublisher(
            configRepo.Object,
            outboxMock.Object,
            logMock.Object,
            contextBuilderMock.Object,
            NullLogger<NotifPublisher>.Instance);

        return (publisher, outboxMock, logMock);
    }

    // ── Config inactiva / inexistente ────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_ConfigInactiva_NoInsertaEnOutbox()
    {
        var (publisher, outboxMock, _) = BuildPublisher(MakeConfig(activo: false));

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, Phone, MakeContexto());

        outboxMock.Verify(r => r.AddAsync(It.IsAny<NotifOutbox>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_SinConfig_NoInsertaEnOutbox()
    {
        var (publisher, outboxMock, _) = BuildPublisher(null);

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, Phone, MakeContexto());

        outboxMock.Verify(r => r.AddAsync(It.IsAny<NotifOutbox>()), Times.Never);
    }

    // ── Sin teléfono ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_SinTelefono_InsertaLogOmitido_YNoInsertaOutbox()
    {
        var (publisher, outboxMock, logMock) = BuildPublisher(MakeConfig());

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, phoneNumber: null, MakeContexto());

        outboxMock.Verify(r => r.AddAsync(It.IsAny<NotifOutbox>()), Times.Never);
        logMock.Verify(r => r.AddAsync(It.Is<NotifLog>(
            l => l.Estado == NotifLogEstado.OMITIDO
              && l.OutboxId == null
              && l.ClienteId == ClientId)), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_TelefonoVacio_InsertaLogOmitido()
    {
        var (publisher, _, logMock) = BuildPublisher(MakeConfig());

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, phoneNumber: "   ", MakeContexto());

        logMock.Verify(r => r.AddAsync(It.Is<NotifLog>(
            l => l.Estado == NotifLogEstado.OMITIDO)), Times.Once);
    }

    // ── Inserción exitosa en outbox ──────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_Exitoso_InsertaOutboxConTipo()
    {
        var (publisher, outboxMock, _) = BuildPublisher(MakeConfig());

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, Phone, MakeContexto());

        outboxMock.Verify(r => r.AddAsync(It.Is<NotifOutbox>(
            o => o.Tipo      == NotifType.RECORDATORIO_R1
              && o.ClienteId == ClientId
              && o.PhoneNumber == Phone
              && o.Publicado  == false
              && o.Intentos   == 0)), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_Exitoso_ContextoJsonEsCorrecto()
    {
        var (publisher, outboxMock, _) = BuildPublisher(MakeConfig());
        var contexto = MakeContexto();

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, Phone, contexto);

        outboxMock.Verify(r => r.AddAsync(It.Is<NotifOutbox>(o =>
            o.ContextoJson.Contains("\"nombre\"")
         && o.ContextoJson.Contains("Carlos")
         && o.ContextoJson.Contains("\"deuda\"")
         && o.ContextoJson.Contains("150.00"))), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_Exitoso_EstadoFinalEsNull_InicialmentePendiente()
    {
        var (publisher, outboxMock, _) = BuildPublisher(MakeConfig());

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, Phone, MakeContexto());

        outboxMock.Verify(r => r.AddAsync(It.Is<NotifOutbox>(
            o => o.EstadoFinal == null)), Times.Once);
    }

    // ── Delay / ventana horaria ──────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_SinDelay_EnviarDesdeEsAhora()
    {
        var (publisher, outboxMock, _) = BuildPublisher(MakeConfig(delay: 0));
        var antes = DateTime.UtcNow;

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, Phone, MakeContexto());

        outboxMock.Verify(r => r.AddAsync(It.Is<NotifOutbox>(
            o => o.EnviarDesde >= antes
              && o.EnviarDesde <= DateTime.UtcNow.AddSeconds(2))), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ConDelay3600_EnviarDesdeEsEn1Hora()
    {
        var (publisher, outboxMock, _) = BuildPublisher(MakeConfig(delay: 3600));
        var antes = DateTime.UtcNow.AddSeconds(3598);

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, Phone, MakeContexto());

        outboxMock.Verify(r => r.AddAsync(It.Is<NotifOutbox>(
            o => o.EnviarDesde >= antes)), Times.Once);
    }

    // ── ReferenciaId (anti-duplicado) ────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_ConReferenciaId_SeGuardaEnOutbox()
    {
        var (publisher, outboxMock, _) = BuildPublisher(MakeConfig(tipo: NotifType.CONFIRMACION_PAGO));
        var refId = Guid.NewGuid();

        await publisher.PublishAsync(
            NotifType.CONFIRMACION_PAGO, ClientId, Phone, MakeContexto(), referenciaId: refId);

        outboxMock.Verify(r => r.AddAsync(It.Is<NotifOutbox>(
            o => o.ReferenciaId == refId)), Times.Once);
    }

    // ── Contexto vacío: auto-construcción ────────────────────────────────────

    [Fact]
    public async Task PublishAsync_ContextoVacio_LlamaContextBuilder()
    {
        var ctxBuilderMock = new Mock<NotifContextBuilderService>(
            RepoMock.Empty<TelecomBoliviaNet.Domain.Entities.Clients.Client>().Object,
            RepoMock.Empty<TelecomBoliviaNet.Domain.Entities.Clients.Invoice>().Object,
            RepoMock.Empty<TelecomBoliviaNet.Domain.Entities.Clients.ClientQr>().Object,
            RepoMock.Empty<SystemConfig>().Object,
            Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());

        ctxBuilderMock
            .Setup(x => x.BuildAsync(ClientId, null, null, null))
            .ReturnsAsync(MakeContexto());

        var (publisher, outboxMock, _) = BuildPublisher(MakeConfig(), ctxBuilderMock);

        await publisher.PublishAsync(
            NotifType.RECORDATORIO_R1, ClientId, Phone,
            contexto: new Dictionary<string, string>());  // vacío → auto-construye

        ctxBuilderMock.Verify(x => x.BuildAsync(ClientId, null, null, null), Times.Once);
        outboxMock.Verify(r => r.AddAsync(It.IsAny<NotifOutbox>()), Times.Once);
    }
}
