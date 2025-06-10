using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Presentation.Middleware;

/// <summary>
/// FIX-20: respuestas de error consistentes en formato ProblemDetails.
/// Sin stack traces en producción. Centraliza el manejo de todas las excepciones.
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            NotFoundException   => (StatusCodes.Status404NotFound,           ex.Message),
            DomainException     => (StatusCodes.Status400BadRequest,         ex.Message),
            ValidationException => (StatusCodes.Status422UnprocessableEntity, "Datos de entrada inválidos"),
            _                   => (StatusCodes.Status500InternalServerError, "Error interno del servidor"),
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Error no controlado en {Path}", ctx.Request.Path);

        var problem = new ProblemDetails
        {
            Status   = status,
            Title    = title,
            Detail   = env.IsDevelopment() ? ex.ToString() : null,
            Instance = ctx.Request.Path,
        };

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", ct);
        return true;
    }
}
