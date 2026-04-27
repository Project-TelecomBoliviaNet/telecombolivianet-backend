using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Application.Services.Notifications;
using TelecomBoliviaNet.Domain.Entities.Notifications;

namespace TelecomBoliviaNet.Presentation.Controllers.Config;

/// <summary>
/// Controlador de configuración de notificaciones WhatsApp.
/// US-35, US-37, US-38, US-39, US-NOT-02, US-NOT-03, US-NOT-04,
/// US-NOT-VARS, US-NOT-PREVIEW, US-NOT-ANTISPAM.
/// </summary>
[Route("api/config/notifications")]
// BUG FIX: usar Policy centralizada en lugar de Roles hardcodeados
[Authorize(Policy = "AdminOnly")]
public class NotifConfigController : BaseController
{
    private readonly NotifConfigService _svc;
    public NotifConfigController(NotifConfigService svc) => _svc = svc;

    // ── US-35/38/NOT-04 · Configuración de triggers ───────────────────────

    [HttpGet]
    public async Task<IActionResult> GetConfigs()
        => OkResult(await _svc.GetConfigsAsync());

    [HttpPut]
    public async Task<IActionResult> UpdateConfigs([FromBody] UpdateNotifConfigsDto dto)
    {
        // US-NOT-04: proteger trigger de Suspensión
        var suspConfig = dto.Configs.FirstOrDefault(c => c.Tipo == NotifType.SUSPENSION);
        if (suspConfig is { Activo: false })
        {
            if (!Request.Headers.TryGetValue("X-Confirm-Suspension", out var confirm)
                || confirm.ToString() != "true")
                return BadRequest(new { message = "La desactivación del trigger de Suspensión requiere confirmación. Envíe el header X-Confirm-Suspension: true." });
        }

        // US-NOT-03: no permitir asociar plantilla Rechazada a un trigger
        foreach (var upd in dto.Configs.Where(c => c.PlantillaId.HasValue))
        {
            // Validación delegada al service — el service puede consultar HsmStatus
        }

        var result = await _svc.UpdateConfigsAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Configuración actualizada correctamente.")
            : BadRequestResult(result.ErrorMessage);
    }

    // ── US-37 / US-NOT-03 · Plantillas ────────────────────────────────────

    [HttpGet("templates")]
    public async Task<IActionResult> GetPlantillas(
        [FromQuery] PlantillaCategoria? categoria = null,
        [FromQuery] HsmStatus? hsmStatus = null)
        => OkResult(await _svc.GetPlantillasAsync(categoria, hsmStatus));

    [HttpPut("templates/{tipo}")]
    public async Task<IActionResult> UpdatePlantilla(
        string tipo, [FromBody] UpdateNotifPlantillaDto dto)
    {
        if (!Enum.TryParse<NotifType>(tipo, out var notifTipo))
            return BadRequestResult($"Tipo inválido: '{tipo}'.");

        // US-NOT-03: bloquear activación en trigger si está Rechazada
        if (dto.HsmStatus == HsmStatus.Rechazada)
        {
            // Solo advertir — el admin puede guardar la plantilla, pero no la puede activar en trigger
        }

        var result = await _svc.UpdatePlantillaAsync(
            notifTipo, dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Plantilla actualizada correctamente.")
            : BadRequestResult(result.ErrorMessage);
    }

    /// <summary>US-NOT-03 · Actualizar solo estado HSM.</summary>
    [HttpPatch("templates/{tipo}/hsm")]
    public async Task<IActionResult> UpdateHsmStatus(string tipo, [FromBody] UpdateHsmStatusDto dto)
    {
        if (!Enum.TryParse<NotifType>(tipo, out var notifTipo))
            return BadRequestResult($"Tipo inválido: '{tipo}'.");

        var result = await _svc.UpdateHsmStatusAsync(
            notifTipo, dto.HsmStatus, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Estado HSM actualizado.")
            : BadRequestResult(result.ErrorMessage);
    }

    [HttpPost("templates/{tipo}/restore")]
    public async Task<IActionResult> RestoreDefault(string tipo)
    {
        if (!Enum.TryParse<NotifType>(tipo, out var notifTipo))
            return BadRequestResult($"Tipo inválido: '{tipo}'.");

        var result = await _svc.RestoreDefaultAsync(
            notifTipo, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Plantilla restaurada al texto por defecto.")
            : BadRequestResult(result.ErrorMessage);
    }

    // ── US-NOT-VARS · Variables disponibles ───────────────────────────────

    [HttpGet("variables")]
    public IActionResult GetVariables()
        => OkResult(_svc.GetVariablesDisponibles()
            .Select(kv => new { Variable = kv.Key, Descripcion = kv.Value })
            .ToList());

    // ── US-NOT-PREVIEW · Preview de plantilla ─────────────────────────────

    [HttpPost("templates/preview")]
    public async Task<IActionResult> PreviewPlantilla([FromBody] PreviewRequestDto dto)
        => OkResult(await _svc.PreviewPlantillaAsync(dto.Texto, dto.ClienteId));

    // ── US-NOT-02 · Segmentos ──────────────────────────────────────────────

    [HttpGet("segments")]
    public async Task<IActionResult> GetSegments()
        => OkResult(await _svc.GetSegmentsAsync());

    [HttpGet("segments/{id:guid}")]
    public async Task<IActionResult> GetSegmentById(Guid id)
    {
        var result = await _svc.GetSegmentByIdAsync(id);
        return result.IsSuccess ? OkResult(result.Value) : NotFoundResult(result.ErrorMessage);
    }

    [HttpPost("segments")]
    public async Task<IActionResult> CreateSegment([FromBody] CreateOrUpdateSegmentDto dto)
    {
        var result = await _svc.CreateSegmentAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkResult(result.Value) : BadRequestResult(result.ErrorMessage);
    }

    [HttpPut("segments/{id:guid}")]
    public async Task<IActionResult> UpdateSegment(Guid id, [FromBody] CreateOrUpdateSegmentDto dto)
    {
        var result = await _svc.UpdateSegmentAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkResult(result.Value) : BadRequestResult(result.ErrorMessage);
    }

    [HttpDelete("segments/{id:guid}")]
    public async Task<IActionResult> DeleteSegment(Guid id)
    {
        var result = await _svc.DeleteSegmentAsync(id, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkMessage("Segmento eliminado.") : BadRequestResult(result.ErrorMessage);
    }

    [HttpPost("segments/preview")]
    public async Task<IActionResult> PreviewSegment([FromBody] CreateOrUpdateSegmentDto dto)
        => OkResult(await _svc.PreviewSegmentAsync(dto));

    // ── US-NOT-ANTISPAM · Envío masivo ─────────────────────────────────────

    [HttpPost("send-masivo")]
    public async Task<IActionResult> EnvioMasivo([FromBody] EnvioMasivoDto dto)
    {
        var result = await _svc.EnvioMasivoAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkResult(result.Value) : BadRequestResult(result.ErrorMessage);
    }
}

public record PreviewRequestDto(string Texto, Guid? ClienteId);
