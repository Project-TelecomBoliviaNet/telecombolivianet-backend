using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.Services.Admin;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Admin;

/// <summary>
/// GET  /api/admin/settings  — devuelve configuración actual del sistema.
/// PUT  /api/admin/settings  — guarda y aplica en runtime.
/// Acceso: solo Admin.
/// </summary>
[Route("api/admin/settings")]
[Authorize(Policy = "AdminOnly")]
public class AdminSettingsController : BaseController
{
    private readonly AdminSettingsService _svc;

    public AdminSettingsController(AdminSettingsService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get() => OkResult(await _svc.GetCurrentAsync());

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] AdminSettingsDto dto)
    {
        var result = await _svc.SaveAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Configuración guardada y aplicada correctamente.")
            : BadRequestResult(result.ErrorMessage!);
    }
}
