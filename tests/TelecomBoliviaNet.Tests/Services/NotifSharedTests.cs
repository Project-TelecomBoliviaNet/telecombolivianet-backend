using FluentAssertions;
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Application.Services.Notifications;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests para NotifShared: DefaultTextos, mappers DTO y zona horaria.
/// Verifica que R2/R3 están deshabilitados y R1 es el único recordatorio.
/// </summary>
public class NotifSharedTests
{
    // ── DefaultTextos ────────────────────────────────────────────────────────

    [Fact]
    public void DefaultTextos_ContieneR1_ComoUnicoRecordatorio()
    {
        NotifShared.DefaultTextos.Should().ContainKey(NotifType.RECORDATORIO_R1);
    }

    [Fact]
    public void DefaultTextos_NoContieneR2_TrasBajaDeRecordatorios()
    {
        NotifShared.DefaultTextos.Should().NotContainKey(NotifType.RECORDATORIO_R2);
    }

    [Fact]
    public void DefaultTextos_NoContieneR3_TrasBajaDeRecordatorios()
    {
        NotifShared.DefaultTextos.Should().NotContainKey(NotifType.RECORDATORIO_R3);
    }

    [Fact]
    public void DefaultTextos_R1_ContieneVariablesEsperadas()
    {
        var texto = NotifShared.DefaultTextos[NotifType.RECORDATORIO_R1];

        texto.Should().Contain("{{nombre}}");
        texto.Should().Contain("{{meses_deuda_detalle}}");
        texto.Should().Contain("{{deuda}}");
    }

    [Fact]
    public void DefaultTextos_R1_NoContieneQrEnlaceNiEmpresaVariable()
    {
        var texto = NotifShared.DefaultTextos[NotifType.RECORDATORIO_R1];

        texto.Should().NotContain("{{qr_enlace}}");
        texto.Should().NotContain("{{empresa}}");
    }

    [Fact]
    public void DefaultTextos_ConfirmacionPago_ContieneVariablesEsperadas()
    {
        var texto = NotifShared.DefaultTextos[NotifType.CONFIRMACION_PAGO];

        texto.Should().Contain("{{nombre}}");
        texto.Should().Contain("{{monto}}");
        texto.Should().Contain("{{periodo}}");
    }

    [Fact]
    public void DefaultTextos_Suspension_ContieneNombreYDeuda_SinQr()
    {
        var texto = NotifShared.DefaultTextos[NotifType.SUSPENSION];

        texto.Should().Contain("{{nombre}}");
        texto.Should().Contain("{{deuda}}");
        texto.Should().NotContain("{{qr_enlace}}");
    }

    [Fact]
    public void DefaultTextos_Reactivacion_ContieneNombre_SinEmpresaVariable()
    {
        var texto = NotifShared.DefaultTextos[NotifType.REACTIVACION];

        texto.Should().Contain("{{nombre}}");
        texto.Should().NotContain("{{empresa}}");
    }

    [Theory]
    [InlineData(NotifType.SUSPENSION)]
    [InlineData(NotifType.REACTIVACION)]
    [InlineData(NotifType.RECORDATORIO_R1)]
    [InlineData(NotifType.FACTURA_VENCIDA)]
    [InlineData(NotifType.CONFIRMACION_PAGO)]
    [InlineData(NotifType.TICKET_CREADO)]
    [InlineData(NotifType.TICKET_ASIGNADO)]
    [InlineData(NotifType.TICKET_RESUELTO)]
    [InlineData(NotifType.CAMBIO_PLAN)]
    public void DefaultTextos_TodosLosTypesActivos_TienenTextoNoVacio(NotifType tipo)
    {
        NotifShared.DefaultTextos.Should().ContainKey(tipo);
        NotifShared.DefaultTextos[tipo].Should().NotBeNullOrWhiteSpace();
    }

    // ── Mapper: ToConfigDto ──────────────────────────────────────────────────

    [Fact]
    public void ToConfigDto_MapeaTodosLosCamposCorrectamente()
    {
        var config = new NotifConfig
        {
            Tipo          = NotifType.RECORDATORIO_R1,
            Activo        = true,
            DelaySegundos = 300,
            HoraInicio    = new TimeOnly(8, 0),
            HoraFin       = new TimeOnly(20, 0),
            Inmediato     = false,
            DiasAntes     = 5,
            PlantillaId   = Guid.NewGuid(),
        };

        var dto = NotifShared.ToConfigDto(config);

        dto.Tipo.Should().Be(NotifType.RECORDATORIO_R1);
        dto.Activo.Should().BeTrue();
        dto.DelaySegundos.Should().Be(300);
        dto.HoraInicio.Should().Be("08:00");
        dto.HoraFin.Should().Be("20:00");
        dto.Inmediato.Should().BeFalse();
        dto.DiasAntes.Should().Be(5);
        dto.PlantillaId.Should().Be(config.PlantillaId);
    }

    [Fact]
    public void ToConfigDto_ConfirmacionPago_InmediatoEsTrue()
    {
        var config = new NotifConfig
        {
            Tipo      = NotifType.CONFIRMACION_PAGO,
            Activo    = true,
            Inmediato = true,
            HoraInicio = new TimeOnly(8, 0),
            HoraFin    = new TimeOnly(20, 0),
        };

        var dto = NotifShared.ToConfigDto(config);

        dto.Inmediato.Should().BeTrue();
    }

    // ── Mapper: ToPlantillaDto ───────────────────────────────────────────────

    [Fact]
    public void ToPlantillaDto_MapeaCamposMetaCorrectamente()
    {
        var plantilla = new NotifPlantilla
        {
            Id               = Guid.NewGuid(),
            Tipo             = NotifType.RECORDATORIO_R1,
            Texto            = "Estimado/a {{nombre}}...",
            Activa           = true,
            Categoria        = PlantillaCategoria.Cobro,
            HsmStatus        = HsmStatus.Aprobada,
            CreadoAt         = DateTime.UtcNow,
            MetaTemplateName = "aviso_pago_pendiente",
            MetaLanguageCode = "es",
            MetaParamOrder   = @"[""nombre"",""meses_deuda_detalle"",""deuda""]",
        };

        var dto = NotifShared.ToPlantillaDto(plantilla);

        dto.MetaTemplateName.Should().Be("aviso_pago_pendiente");
        dto.MetaLanguageCode.Should().Be("es");
        dto.MetaParamOrder.Should().Be(@"[""nombre"",""meses_deuda_detalle"",""deuda""]");
        dto.HsmStatus.Should().Be(HsmStatus.Aprobada);
        dto.Tipo.Should().Be(NotifType.RECORDATORIO_R1);
    }

    [Fact]
    public void ToPlantillaDto_SinMeta_CamposNulos()
    {
        var plantilla = new NotifPlantilla
        {
            Id       = Guid.NewGuid(),
            Tipo     = NotifType.SUSPENSION,
            Texto    = "Texto",
            Activa   = true,
            Categoria = PlantillaCategoria.General,
            HsmStatus = HsmStatus.Pendiente,
            CreadoAt  = DateTime.UtcNow,
        };

        var dto = NotifShared.ToPlantillaDto(plantilla);

        dto.MetaTemplateName.Should().BeNull();
        dto.MetaLanguageCode.Should().Be("es"); // entidad tiene default "es"
        dto.MetaParamOrder.Should().BeNull();
    }

    // ── Zona horaria Bolivia ─────────────────────────────────────────────────

    [Fact]
    public void BoliviaZone_EsUTCMenos4()
    {
        NotifShared.BoliviaZone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(-4));
    }

    [Fact]
    public void BoliviaZone_NoTieneDST()
    {
        NotifShared.BoliviaZone.SupportsDaylightSavingTime.Should().BeFalse();
    }

    // ── VariableDescriptions ─────────────────────────────────────────────────

    [Fact]
    public void VariableDescriptions_ContieneVariablesPrioritarias()
    {
        NotifShared.VariableDescriptions.Should().ContainKey("{{meses_deuda_detalle}}");
        NotifShared.VariableDescriptions.Should().ContainKey("{{qr_enlace}}");
    }

    [Fact]
    public void VariableDescriptions_ContieneVariablesDeTicket()
    {
        NotifShared.VariableDescriptions.Should().ContainKey("{{num_ticket}}");
        NotifShared.VariableDescriptions.Should().ContainKey("{{tecnico}}");
        NotifShared.VariableDescriptions.Should().ContainKey("{{fecha_visita}}");
    }
}
