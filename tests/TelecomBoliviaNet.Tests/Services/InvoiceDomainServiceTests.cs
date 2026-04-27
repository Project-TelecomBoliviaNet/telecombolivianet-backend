using FluentAssertions;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Services;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests del domain service extraído en la CORRECCIÓN Problema #8.
/// Verifica que la lógica de crédito y estado sea correcta y consistente
/// en todos los contextos donde se usa (billing, pagos, M3).
/// </summary>
public class InvoiceDomainServiceTests
{
    // ── AplicarCredito ────────────────────────────────────────────────────────

    [Fact]
    public void AplicarCredito_CubreTotal_DebeRetornarMontoPendienteYCeroRestante()
    {
        var (aplicado, restante) = InvoiceDomainService.AplicarCredito(
            creditoDisponible: 100m, montoPendiente: 100m);

        aplicado.Should().Be(100m);
        restante.Should().Be(0m);
    }

    [Fact]
    public void AplicarCredito_CreditoExcedeMonto_DebeDejarExcedente()
    {
        var (aplicado, restante) = InvoiceDomainService.AplicarCredito(
            creditoDisponible: 150m, montoPendiente: 100m);

        aplicado.Should().Be(100m);
        restante.Should().Be(50m);
    }

    [Fact]
    public void AplicarCredito_CreditoInsuficiente_DebeAplicarParcialmente()
    {
        var (aplicado, restante) = InvoiceDomainService.AplicarCredito(
            creditoDisponible: 30m, montoPendiente: 100m);

        aplicado.Should().Be(30m);
        restante.Should().Be(0m);
    }

    [Fact]
    public void AplicarCredito_SinCredito_DebeRetornarCeroAplicadoYCreditoIntacto()
    {
        var (aplicado, restante) = InvoiceDomainService.AplicarCredito(
            creditoDisponible: 0m, montoPendiente: 100m);

        aplicado.Should().Be(0m);
        restante.Should().Be(0m);
    }

    [Fact]
    public void AplicarCredito_MontoNegativo_DebeRetornarCeroAplicado()
    {
        var (aplicado, restante) = InvoiceDomainService.AplicarCredito(
            creditoDisponible: 50m, montoPendiente: -10m);

        aplicado.Should().Be(0m);
        restante.Should().Be(50m);
    }

    // ── CalcularEstado ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100, 0,   InvoiceStatus.Emitida)]
    [InlineData(100, 50,  InvoiceStatus.ParcialmentePagada)]
    [InlineData(100, 100, InvoiceStatus.Pagada)]
    [InlineData(100, 120, InvoiceStatus.Pagada)]  // pago excesivo → Pagada
    public void CalcularEstado_DebeRetornarEstadoCorrecto(
        decimal amount, decimal amountPaid, InvoiceStatus expected)
    {
        var result = InvoiceDomainService.CalcularEstado(amount, amountPaid);
        result.Should().Be(expected);
    }

    // ── EstadoInicialConCredito ───────────────────────────────────────────────

    [Fact]
    public void EstadoInicialConCredito_SinCredito_DebeSerEmitida()
        => InvoiceDomainService.EstadoInicialConCredito(100m, 0m)
            .Should().Be(InvoiceStatus.Emitida);

    [Fact]
    public void EstadoInicialConCredito_CreditoParcial_DebeSerParcialmentePagada()
        => InvoiceDomainService.EstadoInicialConCredito(100m, 40m)
            .Should().Be(InvoiceStatus.ParcialmentePagada);

    [Fact]
    public void EstadoInicialConCredito_CreditoTotal_DebeSerPagada()
        => InvoiceDomainService.EstadoInicialConCredito(100m, 100m)
            .Should().Be(InvoiceStatus.Pagada);

    // ── TransicionEsValida ────────────────────────────────────────────────────

    [Theory]
    [InlineData(InvoiceStatus.Emitida,   InvoiceStatus.Enviada,   true)]
    [InlineData(InvoiceStatus.Emitida,   InvoiceStatus.Pendiente, true)]
    [InlineData(InvoiceStatus.Emitida,   InvoiceStatus.Anulada,   true)]
    [InlineData(InvoiceStatus.Emitida,   InvoiceStatus.Pagada,    false)]
    [InlineData(InvoiceStatus.Pagada,    InvoiceStatus.Anulada,   false)] // final
    [InlineData(InvoiceStatus.Anulada,   InvoiceStatus.Emitida,   false)] // final
    [InlineData(InvoiceStatus.Pendiente, InvoiceStatus.Vencida,   true)]
    [InlineData(InvoiceStatus.Vencida,   InvoiceStatus.Anulada,   true)]
    [InlineData(InvoiceStatus.ParcialmentePagada, InvoiceStatus.Pagada,  true)]
    [InlineData(InvoiceStatus.ParcialmentePagada, InvoiceStatus.Anulada, true)]
    public void TransicionEsValida_DebeValidarCorrectamente(
        InvoiceStatus actual, InvoiceStatus nuevo, bool expected)
    {
        InvoiceDomainService.TransicionEsValida(actual, nuevo).Should().Be(expected);
    }

    [Fact]
    public void GetTransicionesPermitidas_EstadoFinal_DebeRetornarVacio()
    {
        var pagada  = InvoiceDomainService.GetTransicionesPermitidas(InvoiceStatus.Pagada);
        var anulada = InvoiceDomainService.GetTransicionesPermitidas(InvoiceStatus.Anulada);

        pagada.Should().BeEmpty();
        anulada.Should().BeEmpty();
    }
}
