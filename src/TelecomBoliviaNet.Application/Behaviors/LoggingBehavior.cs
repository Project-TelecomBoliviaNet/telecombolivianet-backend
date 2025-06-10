using MediatR;
using Microsoft.Extensions.Logging;

namespace TelecomBoliviaNet.Application.Behaviors;

// FIX-28: Pipeline Behavior — registra el inicio y fin de cada Command/Query
// junto con su duración. Error inesperado se re-lanza sin silenciar.
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        var name      = typeof(TRequest).Name;
        var startedAt = DateTime.UtcNow;

        _logger.LogInformation("→ {Command} iniciado", name);

        try
        {
            var result = await next();

            _logger.LogInformation(
                "← {Command} completado en {ElapsedMs}ms",
                name,
                (DateTime.UtcNow - startedAt).TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "✗ {Command} falló tras {ElapsedMs}ms",
                name,
                (DateTime.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
    }
}
