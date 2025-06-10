using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace TelecomBoliviaNet.Presentation.Configuration;

/// <summary>
/// Configura rate limiting usando el middleware nativo de ASP.NET Core 8
/// (System.Threading.RateLimiting — incluido en el SDK, sin paquetes externos).
///
/// Políticas definidas:
///   "auth"    — endpoints de login: 10 intentos / 5 minutos por IP.
///               Mitiga ataques de fuerza bruta y credential stuffing.
///   "webhook" — webhook de WhatsApp: 100 req / minuto por IP.
///               Protege contra flood de mensajes maliciosos.
///   "api"     — endpoints generales autenticados: 300 req / minuto por IP.
///               Capa de defensa ante scraping o bugs de frontend.
/// </summary>
public static class RateLimitConfiguration
{
    // Nombres de política expuestos para decorar endpoints con [EnableRateLimiting]
    public const string AuthPolicy    = "auth";
    public const string WebhookPolicy = "webhook";
    public const string ApiPolicy     = "api";

    public static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // ── Respuesta cuando se supera el límite ──────────────────────────
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"success\":false,\"message\":\"Demasiadas solicitudes. Por favor espere e intente nuevamente.\"}",
                    cancellationToken: token);
            };

            // ── Política: Auth — 10 intentos / 5 minutos por IP ──────────────
            // Fixed window: reinicia el contador cada 5 minutos.
            // Suficientemente permisivo para uso normal (max 2 logins por minuto),
            // efectivo contra ataques automatizados de fuerza bruta.
            options.AddFixedWindowLimiter(AuthPolicy, limiterOptions =>
            {
                limiterOptions.PermitLimit         = 10;
                limiterOptions.Window              = TimeSpan.FromMinutes(5);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit          = 0;  // sin cola — rechazar inmediatamente
            });

            // ── Política: Webhook — 100 req / minuto por IP ──────────────────
            // Sliding window: más preciso para webhooks con ráfagas cortas.
            // Meta envía ~1 req por mensaje; 100/min cubre picos operativos reales.
            options.AddSlidingWindowLimiter(WebhookPolicy, limiterOptions =>
            {
                limiterOptions.PermitLimit         = 100;
                limiterOptions.Window              = TimeSpan.FromMinutes(1);
                limiterOptions.SegmentsPerWindow   = 4;  // ventana de 15s por segmento
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit          = 5;
            });

            // ── Política: API general — 300 req / minuto por IP ───────────────
            // Fixed window: suficiente para uso intensivo del panel de admin.
            options.AddFixedWindowLimiter(ApiPolicy, limiterOptions =>
            {
                limiterOptions.PermitLimit         = 300;
                limiterOptions.Window              = TimeSpan.FromMinutes(1);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit          = 10;
            });
        });

        return services;
    }
}
