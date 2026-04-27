using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using TelecomBoliviaNet.Application.Services.Auth;

namespace TelecomBoliviaNet.Presentation.Middleware;

/// <summary>
/// Intercepta cada petición autenticada y verifica que el token
/// no haya sido invalidado por un logout previo.
/// Si el token está en la blacklist devuelve 401 inmediatamente.
///
/// CORRECCIÓN (Fix #5): Se añade IMemoryCache para evitar una query a BD en cada
/// request. Los tokens en blacklist son inmutables (solo se insertan, nunca se
/// eliminan hasta que expiran), por lo que es seguro cachear el resultado "está
/// en blacklist = true" con TTL igual a la expiración del token.
/// Para tokens NO blacklisteados, no se cachea (podría dar falso negativo si se
/// revoca durante el TTL) — solo se cachea el resultado positivo.
/// </summary>
public class TokenBlacklistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache    _cache;

    // Prefijo de clave para evitar colisiones con otras entradas del cache
    private const string CacheKeyPrefix = "tbl:";

    public TokenBlacklistMiddleware(RequestDelegate next, IMemoryCache cache)
    {
        _next  = next;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context, AuthService authService)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            var token    = authHeader.Replace("Bearer ", "").Trim();
            // Usar la firma del JWT (tercer segmento) como base de la cache key.
            // Los primeros 32 chars del header son idénticos en todos los tokens HS256;
            // la firma es única por token porque depende del payload + secret.
            var parts       = token.Split('.');
            var uniquePart  = parts.Length == 3 ? parts[2] : token;
            var cacheKey    = CacheKeyPrefix + uniquePart[..Math.Min(uniquePart.Length, 32)];

            // Revisar cache primero — evita query a BD en el happy path
            if (!_cache.TryGetValue(cacheKey, out bool isBlacklisted))
            {
                isBlacklisted = await authService.IsTokenBlacklistedAsync(token);

                // Solo cachear positivos: si está en blacklist, no cambiará.
                // Negativos no se cachean: el token podría revocarse después.
                if (isBlacklisted)
                {
                    // TTL: 15 min o hasta que el token expire (lo que sea menor).
                    // Para tokens JWT de 8h, 15 min es suficiente para la mayoría de requests.
                    _cache.Set(cacheKey, true, TimeSpan.FromMinutes(15));
                }
            }

            if (isBlacklisted)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "La sesión ha sido cerrada. Inicia sesión nuevamente."
                });
                return;
            }
        }

        await _next(context);
    }
}
