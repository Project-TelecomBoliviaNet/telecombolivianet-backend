using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace TelecomBoliviaNet.Presentation.Controllers;

[ApiController]
public abstract class BaseController : ControllerBase
{
    // BUG FIX: si el claim NameIdentifier falta (token malformado que pasó validación),
    // lanzar UnauthorizedAccessException en lugar de retornar Guid.Empty silenciosamente.
    // Guid.Empty como actorId contaminaría audit logs con 00000000-0000-... y podría
    // saltarse guards del tipo "if (id == CurrentUserId)".
    protected Guid CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(raw, out var id) || id == Guid.Empty)
                throw new UnauthorizedAccessException(
                    "Token JWT válido pero sin claim NameIdentifier. Vuelva a iniciar sesión.");
            return id;
        }
    }

    protected string CurrentUserName =>
        User.FindFirstValue(ClaimTypes.Name) ?? "Desconocido";

    protected string CurrentUserRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    protected string ClientIp =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    protected string RawToken =>
        Request.Headers.Authorization.ToString().Replace("Bearer ", "").Trim();

    protected IActionResult OkResult(object? data) =>
        Ok(new { success = true, data });

    protected IActionResult OkMessage(string message) =>
        Ok(new { success = true, message });

    protected IActionResult BadRequestResult(string message) =>
        BadRequest(new { success = false, message });

    protected IActionResult NotFoundResult(string message) =>
        NotFound(new { success = false, message });
}
