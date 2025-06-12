using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.Dashboard.Queries;
using TelecomBoliviaNet.Application.DTOs.Bot;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Infrastructure.Services.Dashboard;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests para GetDashChatbotHandler (BUG-10):
///   - Chatbot disponible: IsAvailable = true, métricas calculadas correctamente
///   - Chatbot lanza excepción: IsAvailable = false, todas las métricas en 0
///   - Sin conversaciones registradas: TasaResolucionBot = 100, TasaEscaladoHumano = 0
/// </summary>
public class GetDashChatbotHandlerTests
{
    private static GetDashChatbotHandler BuildHandler(IBotProxyService botProxy)
        => new(botProxy, NullLogger<GetDashChatbotHandler>.Instance);

    // ── TC-DASH-CHAT-01: chatbot disponible — IsAvailable y tasas correctas ──

    [Fact]
    public async Task Handle_ChatbotDisponible_RetornaIsAvailableTrueYMetricasCalculadas()
    {
        var stats = new ConversationStatsDto(
            TotalConversaciones: 10,
            Escaladas:           2,
            HoyConversaciones:   3,
            HoyMensajes:         15);

        var botProxy = new Mock<IBotProxyService>();
        botProxy.Setup(b => b.GetStatsAsync()).ReturnsAsync(stats);

        var result = await BuildHandler(botProxy.Object).Handle(new GetDashChatbotQuery(), CancellationToken.None);

        result.IsAvailable.Should().BeTrue();
        result.ConversacionesHoy.Should().Be(3);
        result.ConversacionesMes.Should().Be(10);
        result.TasaResolucionBot.Should().Be(80);      // (10-2)/10 * 100
        result.TasaEscaladoHumano.Should().Be(20);     // 2/10 * 100
    }

    // ── TC-DASH-CHAT-02: chatbot lanza excepción — IsAvailable = false ────────

    [Fact]
    public async Task Handle_ChatbotLanzaExcepcion_RetornaIsAvailableFalseYMetricasEnCero()
    {
        var botProxy = new Mock<IBotProxyService>();
        botProxy.Setup(b => b.GetStatsAsync())
                .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await BuildHandler(botProxy.Object).Handle(new GetDashChatbotQuery(), CancellationToken.None);

        result.IsAvailable.Should().BeFalse();
        result.ConversacionesActivas.Should().Be(0);
        result.ConversacionesHoy.Should().Be(0);
        result.ConversacionesMes.Should().Be(0);
        result.TasaResolucionBot.Should().Be(0);
        result.TasaEscaladoHumano.Should().Be(0);
        result.IntencionesFrecuentes.Should().BeEmpty();
    }

    // ── TC-DASH-CHAT-03: sin conversaciones — tasas por defecto coherentes ────

    [Fact]
    public async Task Handle_SinConversaciones_TasaResolucionEs100YEscaladoEs0()
    {
        var stats = new ConversationStatsDto(
            TotalConversaciones: 0,
            Escaladas:           0,
            HoyConversaciones:   0,
            HoyMensajes:         0);

        var botProxy = new Mock<IBotProxyService>();
        botProxy.Setup(b => b.GetStatsAsync()).ReturnsAsync(stats);

        var result = await BuildHandler(botProxy.Object).Handle(new GetDashChatbotQuery(), CancellationToken.None);

        result.IsAvailable.Should().BeTrue();
        result.TasaResolucionBot.Should().Be(100);
        result.TasaEscaladoHumano.Should().Be(0);
    }
}
