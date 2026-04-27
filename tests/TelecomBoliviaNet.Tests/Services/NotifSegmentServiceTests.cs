using FluentAssertions;
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Application.Services.Notifications;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests para NotifShared.EvaluaCondicion — lógica de filtrado de segmentos.
/// Extraída de NotifConfigService en la CORRECCIÓN Problema #6.
/// </summary>
public class NotifSegmentFilterTests
{
    private static Client BuildClient(string zona = "Zona Norte", string estado = "Activo", string plan = "Plan Básico")
    {
        var client = new Client { Zone = zona, Status = Enum.Parse<ClientStatus>(estado) };
        client.Plan = new Plan { Name = plan };
        return client;
    }

    private static List<Invoice> BuildDeuda(decimal monto, int diasMora = 0)
    {
        var due = DateTime.UtcNow.AddDays(-diasMora);
        return [new Invoice { Amount = monto, DueDate = due, Status = InvoiceStatus.Pendiente }];
    }

    // ── Condiciones de zona ───────────────────────────────────────────────────

    [Fact]
    public void EvaluaCondicion_Zona_Igual_DebeMatchear()
    {
        var cond = new SegmentCondition("zona", "=", "Zona Norte");
        NotifShared.EvaluaCondicion(BuildClient("Zona Norte"), [], cond).Should().BeTrue();
    }

    [Fact]
    public void EvaluaCondicion_Zona_Igual_NoDebeMatchearOtraZona()
    {
        var cond = new SegmentCondition("zona", "=", "Zona Sur");
        NotifShared.EvaluaCondicion(BuildClient("Zona Norte"), [], cond).Should().BeFalse();
    }

    [Fact]
    public void EvaluaCondicion_Zona_Diferente_DebeMatchearOtraZona()
    {
        var cond = new SegmentCondition("zona", "!=", "Zona Sur");
        NotifShared.EvaluaCondicion(BuildClient("Zona Norte"), [], cond).Should().BeTrue();
    }

    // ── Condiciones de deuda ──────────────────────────────────────────────────

    [Fact]
    public void EvaluaCondicion_Deuda_MayorQue_DebeMatchear()
    {
        var cond = new SegmentCondition("deuda", ">", "100");
        NotifShared.EvaluaCondicion(BuildClient(), BuildDeuda(150m), cond).Should().BeTrue();
    }

    [Fact]
    public void EvaluaCondicion_Deuda_MayorQue_NoDebeMatchearMenor()
    {
        var cond = new SegmentCondition("deuda", ">", "200");
        NotifShared.EvaluaCondicion(BuildClient(), BuildDeuda(150m), cond).Should().BeFalse();
    }

    [Theory]
    [InlineData(">=", 100, 100, true)]
    [InlineData(">=", 100, 99,  false)]
    [InlineData("<=", 100, 100, true)]
    [InlineData("<=", 100, 101, false)]
    [InlineData("=",  100, 100, true)]
    [InlineData("=",  100, 101, false)]
    [InlineData("!=", 100, 101, true)]
    [InlineData("!=", 100, 100, false)]
    public void EvaluaCondicion_Deuda_Operadores_DebeEvaluarCorrectamente(
        string op, decimal valor, decimal deudaCliente, bool expected)
    {
        var cond = new SegmentCondition("deuda", op, valor.ToString());
        NotifShared.EvaluaCondicion(BuildClient(), BuildDeuda(deudaCliente), cond)
            .Should().Be(expected);
    }

    // ── Condiciones de días mora ──────────────────────────────────────────────

    [Fact]
    public void EvaluaCondicion_DiasMora_SinFacturas_DebeRetornarCero()
    {
        var cond = new SegmentCondition("dias_mora", ">", "5");
        NotifShared.EvaluaCondicion(BuildClient(), [], cond).Should().BeFalse();
    }

    [Fact]
    public void EvaluaCondicion_DiasMora_ConMora_DebeMatchear()
    {
        var cond = new SegmentCondition("dias_mora", ">", "5");
        NotifShared.EvaluaCondicion(BuildClient(), BuildDeuda(100m, diasMora: 10), cond)
            .Should().BeTrue();
    }

    // ── Operador desconocido ──────────────────────────────────────────────────

    [Fact]
    public void EvaluaCondicion_OperadorDesconocido_DebeRetornarFalseSinExcepcion()
    {
        var cond = new SegmentCondition("deuda", "LIKE", "100");
        NotifShared.EvaluaCondicion(BuildClient(), BuildDeuda(100m), cond).Should().BeFalse();
    }

    [Fact]
    public void EvaluaCondicion_CampoDesconocido_DebeRetornarFalseSinExcepcion()
    {
        var cond = new SegmentCondition("campo_inexistente", "=", "valor");
        NotifShared.EvaluaCondicion(BuildClient(), [], cond).Should().BeFalse();
    }

    [Fact]
    public void EvaluaCondicion_ValorNoNumericoEnDeuda_DebeRetornarFalseSinExcepcion()
    {
        var cond = new SegmentCondition("deuda", ">", "no_es_numero");
        NotifShared.EvaluaCondicion(BuildClient(), BuildDeuda(100m), cond).Should().BeFalse();
    }
}
