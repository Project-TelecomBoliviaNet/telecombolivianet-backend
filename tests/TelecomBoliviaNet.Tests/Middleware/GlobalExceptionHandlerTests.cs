using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Domain.Primitives;
using TelecomBoliviaNet.Presentation.Middleware;
using Xunit;

namespace TelecomBoliviaNet.Tests.Middleware;

// ══════════════════════════════════════════════════════════════════════════════
// FIX-20 — GlobalExceptionHandler: mapeo de excepciones a HTTP + ProblemDetails
// Tests puramente unitarios — DefaultHttpContext en memoria, sin servidor real.
// ══════════════════════════════════════════════════════════════════════════════

public class GlobalExceptionHandlerTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static GlobalExceptionHandler BuildHandler(bool isDevelopment = false)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName)
           .Returns(isDevelopment ? Environments.Development : Environments.Production);
        return new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            env.Object);
    }

    private static async Task<(int StatusCode, string Body)> InvokeAsync(
        GlobalExceptionHandler handler, Exception ex)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ctx.Response.Body);
        return (ctx.Response.StatusCode, await reader.ReadToEndAsync());
    }

    // ── Mapeos de estado HTTP ──────────────────────────────────────────────────

    [Fact]
    public async Task DomainException_MapsTo400_WithExceptionMessageAsTitle()
    {
        var handler = BuildHandler();
        var (status, body) = await InvokeAsync(handler, new DomainException("Regla de negocio violada"));

        status.Should().Be(400);
        body.Should().Contain("400");
        body.Should().Contain("Regla de negocio violada");
    }

    [Fact]
    public async Task NotFoundException_MapsTo404_WithExceptionMessageAsTitle()
    {
        var handler = BuildHandler();
        var ex = NotFoundException.For("Cliente", Guid.NewGuid());

        var (status, body) = await InvokeAsync(handler, ex);

        status.Should().Be(404);
        body.Should().Contain("404");
        body.Should().Contain("Cliente");
    }

    [Fact]
    public async Task ValidationException_MapsTo422_WithFixedTitle()
    {
        var handler = BuildHandler();
        var (status, body) = await InvokeAsync(handler, new ValidationException("Campo requerido"));

        status.Should().Be(422);
        body.Should().Contain("422");
        body.Should().Contain("Datos de entrada inv");  // "inválidos" — avoids encoding issues
    }

    [Fact]
    public async Task UnknownException_MapsTo500_WithFixedTitle()
    {
        var handler = BuildHandler();
        var (status, body) = await InvokeAsync(handler, new InvalidOperationException("boom"));

        status.Should().Be(500);
        body.Should().Contain("500");
        body.Should().Contain("Error interno del servidor");
    }

    // ── Retorno y ContentType ──────────────────────────────────────────────────

    [Fact]
    public async Task TryHandleAsync_AlwaysReturnsTrue_ForAnyException()
    {
        var handler = BuildHandler();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var result = await handler.TryHandleAsync(ctx, new Exception("x"), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(DomainException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task Response_ContentType_IsProblemJson(Type exType)
    {
        var handler = BuildHandler();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var ex = (Exception)Activator.CreateInstance(exType, "test")!;

        await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        ctx.Response.ContentType.Should().Contain("application/problem+json");
    }

    // ── Detail (stack trace) ────────────────────────────────────────────────────

    [Fact]
    public async Task Detail_IsAbsent_InProduction()
    {
        var handler = BuildHandler(isDevelopment: false);
        var (_, body) = await InvokeAsync(handler, new DomainException("test"));

        // "detail":null o sin clave "detail" — en ningún caso el stack trace
        body.Should().NotContain("at TelecomBoliviaNet");
    }

    [Fact]
    public async Task Detail_ContainsExceptionInfo_InDevelopment()
    {
        var handler = BuildHandler(isDevelopment: true);
        var (_, body) = await InvokeAsync(handler, new DomainException("test detail check"));

        body.Should().Contain("DomainException");
    }

    // ── Verificación de múltiples excepciones como tabla ──────────────────────

    [Theory]
    [InlineData(400, "DomainException")]
    [InlineData(404, "NotFoundException")]
    [InlineData(422, "ValidationException")]
    public async Task ExceptionType_ProducesExpectedStatusCode(int expectedStatus, string exTypeName)
    {
        var handler = BuildHandler();
        Exception ex = exTypeName switch
        {
            "DomainException"     => new DomainException("msg"),
            "NotFoundException"   => new NotFoundException("msg"),
            "ValidationException" => new ValidationException("msg"),
            _                     => throw new ArgumentOutOfRangeException(),
        };

        var (status, _) = await InvokeAsync(handler, ex);

        status.Should().Be(expectedStatus);
    }
}
