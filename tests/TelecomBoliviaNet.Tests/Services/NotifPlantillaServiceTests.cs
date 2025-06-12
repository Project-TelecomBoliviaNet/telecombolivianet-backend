using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Notifications;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests para NotifPlantillaService:
///   - UpdatePlantillaAsync persiste campos Meta (MetaTemplateName, MetaLanguageCode, MetaParamOrder)
///   - UpdatePlantillaAsync falla si plantilla no existe
///   - UpdateHsmStatusAsync cambia el estado a Aprobada / Rechazada
///   - RestoreDefaultAsync restaura texto de R1 desde DefaultTextos
///   - RestoreDefaultAsync falla para tipos sin default (R2/R3 eliminados)
///   - GetVariablesDisponibles retorna las variables definidas en NotifShared
///   - GetPlantillasAsync filtra inactivas y por categoría
/// </summary>
public class NotifPlantillaServiceTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private const string ActorName = "admin";
    private const string Ip        = "127.0.0.1";

    private static AuditService MakeAudit() =>
        new AuditService(RepoMock.Empty<AuditLog>().Object,
                         NullLogger<AuditService>.Instance);

    private static NotifPlantilla MakePlantilla(
        NotifType tipo              = NotifType.RECORDATORIO_R1,
        bool      activa            = true,
        HsmStatus hsmStatus         = HsmStatus.Pendiente,
        string?   metaTemplateName  = null,
        string?   metaLanguageCode  = null,
        string?   metaParamOrder    = null) => new()
    {
        Id               = Guid.NewGuid(),
        Tipo             = tipo,
        Texto            = "Texto original {{nombre}}",
        Activa           = activa,
        Categoria        = PlantillaCategoria.Cobro,
        HsmStatus        = hsmStatus,
        CreadoAt         = DateTime.UtcNow,
        CreadoPorId      = ActorId,
        MetaTemplateName = metaTemplateName,
        MetaLanguageCode = metaLanguageCode,
        MetaParamOrder   = metaParamOrder,
    };

    private static NotifPlantillaService MakeService(
        NotifPlantilla[]? plantillas = null)
    {
        var repo          = plantillas is not null
            ? RepoMock.Of(plantillas)
            : RepoMock.Empty<NotifPlantilla>();
        var historialRepo = RepoMock.Empty<NotifPlantillaHistorial>();
        var clientRepo    = RepoMock.Empty<Client>();
        var invoiceRepo   = RepoMock.Empty<Invoice>();
        var sysConfigRepo = RepoMock.Empty<SystemConfig>();

        return new NotifPlantillaService(
            repo.Object,
            historialRepo.Object,
            clientRepo.Object,
            invoiceRepo.Object,
            sysConfigRepo.Object,
            MakeAudit());
    }

    // ── UpdatePlantillaAsync — plantilla no encontrada ────────────────────────

    [Fact]
    public async Task UpdatePlantillaAsync_PlantillaNoExiste_RetornaFailure()
    {
        var svc = MakeService(plantillas: []);

        var result = await svc.UpdatePlantillaAsync(
            NotifType.RECORDATORIO_R1,
            new UpdateNotifPlantillaDto("Nuevo texto"),
            ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RECORDATORIO_R1");
    }

    [Fact]
    public async Task UpdatePlantillaAsync_PlantillaInactiva_RetornaFailure()
    {
        var svc = MakeService(plantillas:
        [
            MakePlantilla(NotifType.RECORDATORIO_R1, activa: false),
        ]);

        var result = await svc.UpdatePlantillaAsync(
            NotifType.RECORDATORIO_R1,
            new UpdateNotifPlantillaDto("Nuevo texto"),
            ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeFalse();
    }

    // ── UpdatePlantillaAsync — campos Meta ────────────────────────────────────

    [Fact]
    public async Task UpdatePlantillaAsync_ActualizaMetaTemplateName()
    {
        var plantilla = MakePlantilla();
        var repo      = RepoMock.Of(plantilla);
        var svc = new NotifPlantillaService(
            repo.Object,
            RepoMock.Empty<NotifPlantillaHistorial>().Object,
            RepoMock.Empty<Client>().Object,
            RepoMock.Empty<Invoice>().Object,
            RepoMock.Empty<SystemConfig>().Object,
            MakeAudit());

        var dto = new UpdateNotifPlantillaDto(
            Texto:           "Texto actualizado {{nombre}}",
            MetaTemplateName: "aviso_pago_pendiente",
            MetaLanguageCode: "es",
            MetaParamOrder:   @"[""nombre"",""meses_deuda_detalle"",""deuda""]");

        var result = await svc.UpdatePlantillaAsync(
            NotifType.RECORDATORIO_R1, dto, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(It.Is<NotifPlantilla>(p =>
            p.MetaTemplateName == "aviso_pago_pendiente"
         && p.MetaLanguageCode == "es"
         && p.MetaParamOrder   == @"[""nombre"",""meses_deuda_detalle"",""deuda""]"
        )), Times.Once);
    }

    [Fact]
    public async Task UpdatePlantillaAsync_MetaLanguageCode_DefaultEsSiNoSeEspecifica()
    {
        var plantilla = MakePlantilla();
        var repo      = RepoMock.Of(plantilla);
        var svc = new NotifPlantillaService(
            repo.Object,
            RepoMock.Empty<NotifPlantillaHistorial>().Object,
            RepoMock.Empty<Client>().Object,
            RepoMock.Empty<Invoice>().Object,
            RepoMock.Empty<SystemConfig>().Object,
            MakeAudit());

        // MetaLanguageCode null → debe quedar "es"
        var dto = new UpdateNotifPlantillaDto(
            "Texto", MetaLanguageCode: null);

        await svc.UpdatePlantillaAsync(
            NotifType.RECORDATORIO_R1, dto, ActorId, ActorName, Ip);

        repo.Verify(r => r.UpdateAsync(It.Is<NotifPlantilla>(
            p => p.MetaLanguageCode == "es")), Times.Once);
    }

    [Fact]
    public async Task UpdatePlantillaAsync_GuardaTextoNuevo()
    {
        var plantilla = MakePlantilla();
        var repo      = RepoMock.Of(plantilla);
        var svc = new NotifPlantillaService(
            repo.Object,
            RepoMock.Empty<NotifPlantillaHistorial>().Object,
            RepoMock.Empty<Client>().Object,
            RepoMock.Empty<Invoice>().Object,
            RepoMock.Empty<SystemConfig>().Object,
            MakeAudit());

        await svc.UpdatePlantillaAsync(
            NotifType.RECORDATORIO_R1,
            new UpdateNotifPlantillaDto("Texto completamente nuevo"),
            ActorId, ActorName, Ip);

        repo.Verify(r => r.UpdateAsync(It.Is<NotifPlantilla>(
            p => p.Texto == "Texto completamente nuevo")), Times.Once);
    }

    // ── UpdateHsmStatusAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHsmStatusAsync_CambiaStatusAAprobada()
    {
        var plantilla = MakePlantilla(hsmStatus: HsmStatus.Pendiente);
        var repo      = RepoMock.Of(plantilla);
        var svc = new NotifPlantillaService(
            repo.Object,
            RepoMock.Empty<NotifPlantillaHistorial>().Object,
            RepoMock.Empty<Client>().Object,
            RepoMock.Empty<Invoice>().Object,
            RepoMock.Empty<SystemConfig>().Object,
            MakeAudit());

        var result = await svc.UpdateHsmStatusAsync(
            NotifType.RECORDATORIO_R1, HsmStatus.Aprobada, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(It.Is<NotifPlantilla>(
            p => p.HsmStatus == HsmStatus.Aprobada)), Times.Once);
    }

    [Fact]
    public async Task UpdateHsmStatusAsync_CambiaStatusARechazada()
    {
        var plantilla = MakePlantilla(hsmStatus: HsmStatus.Pendiente);
        var repo      = RepoMock.Of(plantilla);
        var svc = new NotifPlantillaService(
            repo.Object,
            RepoMock.Empty<NotifPlantillaHistorial>().Object,
            RepoMock.Empty<Client>().Object,
            RepoMock.Empty<Invoice>().Object,
            RepoMock.Empty<SystemConfig>().Object,
            MakeAudit());

        var result = await svc.UpdateHsmStatusAsync(
            NotifType.RECORDATORIO_R1, HsmStatus.Rechazada, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.UpdateAsync(It.Is<NotifPlantilla>(
            p => p.HsmStatus == HsmStatus.Rechazada)), Times.Once);
    }

    [Fact]
    public async Task UpdateHsmStatusAsync_TipoInexistente_RetornaFailure()
    {
        var svc = MakeService(plantillas: []);

        var result = await svc.UpdateHsmStatusAsync(
            NotifType.SUSPENSION, HsmStatus.Aprobada, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeFalse();
    }

    // ── RestoreDefaultAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RestoreDefaultAsync_R1_RestauratTextoDefault()
    {
        var plantilla = MakePlantilla(NotifType.RECORDATORIO_R1);
        var repo      = RepoMock.Of(plantilla);
        var svc = new NotifPlantillaService(
            repo.Object,
            RepoMock.Empty<NotifPlantillaHistorial>().Object,
            RepoMock.Empty<Client>().Object,
            RepoMock.Empty<Invoice>().Object,
            RepoMock.Empty<SystemConfig>().Object,
            MakeAudit());

        var result = await svc.RestoreDefaultAsync(
            NotifType.RECORDATORIO_R1, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeTrue();
        var expectedText = NotifShared.DefaultTextos[NotifType.RECORDATORIO_R1];
        repo.Verify(r => r.UpdateAsync(It.Is<NotifPlantilla>(
            p => p.Texto == expectedText)), Times.Once);
    }

    [Fact]
    public async Task RestoreDefaultAsync_R2_FallaPortqueNoHayDefault()
    {
        var svc = MakeService();

        // R2 fue eliminado de DefaultTextos
        var result = await svc.RestoreDefaultAsync(
            NotifType.RECORDATORIO_R2, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RECORDATORIO_R2");
    }

    [Fact]
    public async Task RestoreDefaultAsync_R3_FallaPortqueNoHayDefault()
    {
        var svc = MakeService();

        var result = await svc.RestoreDefaultAsync(
            NotifType.RECORDATORIO_R3, ActorId, ActorName, Ip);

        result.IsSuccess.Should().BeFalse();
    }

    // ── GetVariablesDisponibles ──────────────────────────────────────────────

    [Fact]
    public void GetVariablesDisponibles_RetornaTodasLasVariables()
    {
        var svc = MakeService();

        var vars = svc.GetVariablesDisponibles();

        vars.Should().NotBeEmpty();
        vars.Should().ContainKey("{{nombre}}");
        vars.Should().ContainKey("{{meses_deuda_detalle}}");
        vars.Should().ContainKey("{{deuda}}");
        vars.Should().ContainKey("{{monto}}");
        vars.Should().ContainKey("{{periodo}}");
    }

    // ── GetPlantillasAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlantillasAsync_ExcluyeInactivas()
    {
        var svc = MakeService(plantillas:
        [
            MakePlantilla(NotifType.RECORDATORIO_R1, activa: true),
            MakePlantilla(NotifType.SUSPENSION,       activa: false),
        ]);

        var result = await svc.GetPlantillasAsync();

        result.Should().HaveCount(1);
        result[0].Tipo.Should().Be(NotifType.RECORDATORIO_R1);
    }

    [Fact]
    public async Task GetPlantillasAsync_FiltraPorHsmStatus()
    {
        var svc = MakeService(plantillas:
        [
            MakePlantilla(NotifType.RECORDATORIO_R1, hsmStatus: HsmStatus.Aprobada),
            MakePlantilla(NotifType.SUSPENSION,      hsmStatus: HsmStatus.Pendiente),
        ]);

        var result = await svc.GetPlantillasAsync(hsmFilter: HsmStatus.Aprobada);

        result.Should().HaveCount(1);
        result[0].HsmStatus.Should().Be(HsmStatus.Aprobada);
    }

    [Fact]
    public async Task GetPlantillasAsync_FiltraPorCategoria()
    {
        var cobroPlantilla  = MakePlantilla(NotifType.RECORDATORIO_R1);
        cobroPlantilla.Categoria = PlantillaCategoria.Cobro;

        var ticketPlantilla = MakePlantilla(NotifType.TICKET_CREADO);
        ticketPlantilla.Categoria = PlantillaCategoria.Ticket;

        var svc = MakeService(plantillas: [cobroPlantilla, ticketPlantilla]);

        var result = await svc.GetPlantillasAsync(
            categoriaFilter: PlantillaCategoria.Cobro);

        result.Should().HaveCount(1);
        result[0].Categoria.Should().Be(PlantillaCategoria.Cobro);
    }
}
