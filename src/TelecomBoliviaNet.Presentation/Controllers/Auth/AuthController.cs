using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Presentation.Configuration;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Auth;

[Route("api/auth")]
public class AuthController : BaseController
{
    private readonly AuthService                   _authService;
    private readonly IValidator<LoginDto>          _loginValidator;
    private readonly IValidator<ChangePasswordDto> _changePassValidator;

    // Nombre de la cookie httpOnly que almacena el refresh token.
    // Constante pública para que los tests puedan referenciarla.
    public const string RefreshTokenCookieName = "rt";

    public AuthController(
        AuthService                    authService,
        IValidator<LoginDto>           loginValidator,
        IValidator<ChangePasswordDto>  changePassValidator)
    {
        _authService         = authService;
        _loginValidator      = loginValidator;
        _changePassValidator = changePassValidator;
    }

    /// <summary>
    /// US-01 - Inicio de sesion.
    /// BUG A FIX: El refresh token ya NO se retorna en el body JSON.
    /// Se emite como cookie httpOnly, Secure, SameSite=Strict para protegerlo de XSS.
    /// El access token (vida corta, 8h) sí se retorna en el body — es aceptable
    /// guardarlo en memoria (no en localStorage).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitConfiguration.AuthPolicy)]
    [ProducesResponseType(typeof(LoginResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var validation = await _loginValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return BadRequest(new
            {
                success = false,
                errors  = validation.Errors.Select(e => e.ErrorMessage)
            });

        var result = await _authService.LoginAsync(dto, ClientIp);
        if (!result.IsSuccess)
            return Unauthorized(new { success = false, message = result.ErrorMessage });

        // BUG A FIX: emitir refresh token como httpOnly cookie, no en el body.
        SetRefreshTokenCookie(result.Value!.RefreshToken);

        // Retornar al frontend solo el access token y datos de sesión.
        // RefreshToken se omite del body — el frontend no debe verlo ni almacenarlo.
        return OkResult(result.Value! with { RefreshToken = string.Empty });
    }

    /// <summary>
    /// Renovar access token usando el refresh token de la cookie httpOnly.
    /// BUG A FIX: Lee el refresh token de la cookie, no del body.
    /// El nuevo refresh token rotado también se emite como cookie httpOnly.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitConfiguration.AuthPolicy)]
    [ProducesResponseType(typeof(RefreshResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> Refresh()
    {
        // BUG A FIX: leer el refresh token de la cookie httpOnly, no del body.
        // Si no hay cookie, el request no está autenticado.
        if (!Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken)
            || string.IsNullOrWhiteSpace(rawRefreshToken))
            return Unauthorized(new { success = false, message = "Sesión expirada. Inicia sesión nuevamente." });

        var result = await _authService.RefreshAsync(rawRefreshToken, ClientIp);
        if (!result.IsSuccess)
        {
            // Token inválido/revocado — limpiar la cookie
            DeleteRefreshTokenCookie();
            return Unauthorized(new { success = false, message = result.ErrorMessage });
        }

        // Rotar la cookie con el nuevo refresh token
        SetRefreshTokenCookie(result.Value!.RefreshToken);

        // Retornar solo el nuevo access token al body
        return OkResult(new { Token = result.Value!.Token });
    }

    /// <summary>
    /// US-03 - Cierre de sesion.
    /// BUG A FIX: Lee el refresh token de la cookie, revoca en servidor y limpia la cookie.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Logout()
    {
        // Leer refresh token de la cookie para revocarlo en el servidor
        Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken);

        await _authService.LogoutAsync(
            RawToken,
            rawRefreshToken,
            CurrentUserId, CurrentUserName, ClientIp);

        // Eliminar la cookie httpOnly del cliente
        DeleteRefreshTokenCookie();

        return OkMessage("Sesion cerrada correctamente.");
    }

    /// <summary>
    /// US-07 / US-08 - Cambio de contraseña.
    /// </summary>
    [HttpPut("change-password")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var validation = await _changePassValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return BadRequest(new
            {
                success = false,
                errors  = validation.Errors.Select(e => e.ErrorMessage)
            });

        var result = await _authService.ChangePasswordAsync(
            CurrentUserId, dto, ClientIp);

        if (!result.IsSuccess)
            return BadRequestResult(result.ErrorMessage);

        return OkMessage("Contrasena cambiada exitosamente.");
    }

    /// <summary>
    /// US-04 - Verificar sesión activa.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public IActionResult Me()
    {
        return OkResult(new
        {
            UserId   = CurrentUserId,
            FullName = CurrentUserName,
            Role     = CurrentUserRole
        });
    }

    // ── Helpers de cookie ─────────────────────────────────────────────────────

    private void SetRefreshTokenCookie(string refreshToken)
    {
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            // httpOnly: inaccesible desde JavaScript — protege contra XSS
            HttpOnly = true,
            // Secure: solo se envía por HTTPS — en dev local puede ser false
            Secure   = !HttpContext.Request.Host.Host.Contains("localhost"),
            // SameSite=Strict: no se envía en requests cross-site — protege contra CSRF
            SameSite = SameSiteMode.Strict,
            // Expira en 30 días (igual que el token en BD)
            Expires  = DateTimeOffset.UtcNow.AddDays(30),
            // Path restringido a /api/auth para no enviar la cookie en otros endpoints
            Path     = "/api/auth",
        });
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure   = !HttpContext.Request.Host.Host.Contains("localhost"),
            SameSite = SameSiteMode.Strict,
            Path     = "/api/auth",
        });
    }
}

/// <summary>Body del refresh — ya no se usa (refresh token viene en cookie).</summary>
public record RefreshTokenDto(string RefreshToken);

/// <summary>Body del logout — ya no se usa (refresh token viene en cookie).</summary>
public record LogoutDto(string? RefreshToken);
