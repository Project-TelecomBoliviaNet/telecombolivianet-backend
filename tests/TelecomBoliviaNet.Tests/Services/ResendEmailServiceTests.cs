using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using TelecomBoliviaNet.Infrastructure.Services;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

public class ResendEmailServiceTests
{
    private const string ValidApiKey = "re_test_key_123";
    private const string TestEmail   = "usuario@telecom.bo";
    private const string TestName    = "Juan Pérez";
    private const string TestLink    = "http://localhost:5173/reset-password?token=abc";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ResendEmailService service, Mock<HttpMessageHandler> handlerMock)
        MakeService(string apiKey = ValidApiKey, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content    = new StringContent("{\"id\":\"msg_123\"}", Encoding.UTF8, "application/json"),
            });

        var httpClient = new HttpClient(handlerMock.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:ApiKey"]      = apiKey,
                ["Email:FromAddress"] = "onboarding@resend.dev",
                ["Email:FromName"]    = "TelecomBoliviaNet",
            })
            .Build();

        var service = new ResendEmailService(
            httpClient, config, NullLogger<ResendEmailService>.Instance);

        return (service, handlerMock);
    }

    // ── TC-ES-01 · ApiKey configurada → llama a la API de Resend ─────────────

    [Fact]
    public async Task SendPasswordResetAsync_ConApiKey_LlamaAResendApi()
    {
        var (service, handlerMock) = MakeService();

        await service.SendPasswordResetAsync(TestEmail, TestName, TestLink);

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("api.resend.com/emails")),
            ItExpr.IsAny<CancellationToken>());
    }

    // ── TC-ES-02 · ApiKey vacía → modo STUB, sin llamada HTTP ────────────────

    [Fact]
    public async Task SendPasswordResetAsync_SinApiKey_ModoStubSinLlamadaHttp()
    {
        var (service, handlerMock) = MakeService(apiKey: "");

        await service.SendPasswordResetAsync(TestEmail, TestName, TestLink);

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // ── TC-ES-03 · Notificación de cambio también respeta modo STUB ──────────

    [Fact]
    public async Task SendPasswordChangedNotificationAsync_SinApiKey_ModoStub()
    {
        var (service, handlerMock) = MakeService(apiKey: "");

        await service.SendPasswordChangedNotificationAsync(TestEmail, TestName);

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // ── TC-ES-04 · Resend retorna error HTTP → no lanza excepción ────────────

    [Fact]
    public async Task SendPasswordResetAsync_ResendRetornaError_NoLanzaExcepcion()
    {
        var (service, _) = MakeService(statusCode: HttpStatusCode.UnprocessableEntity);

        var act = async () => await service.SendPasswordResetAsync(TestEmail, TestName, TestLink);

        await act.Should().NotThrowAsync();
    }

    // ── TC-ES-05 · Error de red → no lanza excepción (protección al flujo) ───

    [Fact]
    public async Task SendPasswordResetAsync_ErrorDeRed_NoLanzaExcepcion()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Sin conexión"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:ApiKey"] = ValidApiKey,
            })
            .Build();

        var service = new ResendEmailService(
            new HttpClient(handlerMock.Object),
            config,
            NullLogger<ResendEmailService>.Instance);

        var act = async () => await service.SendPasswordResetAsync(TestEmail, TestName, TestLink);

        await act.Should().NotThrowAsync();
    }

    // ── TC-ES-06 · Notificación de cambio con ApiKey → llama a Resend ────────

    [Fact]
    public async Task SendPasswordChangedNotificationAsync_ConApiKey_LlamaAResend()
    {
        var (service, handlerMock) = MakeService();

        await service.SendPasswordChangedNotificationAsync(TestEmail, TestName);

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post),
            ItExpr.IsAny<CancellationToken>());
    }
}
