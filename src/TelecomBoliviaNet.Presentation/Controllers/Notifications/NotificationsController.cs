using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.Services.Notifications;
using TelecomBoliviaNet.Domain.Entities.Notifications;

namespace TelecomBoliviaNet.Presentation.Controllers.Notifications;

/// <summary>
/// Controlador para operaciones individuales de notificaciones (US-36, US-39).
/// BUG FIX: reemplazado NotifConfigService monolítico por servicios SRP especializados.
/// </summary>
[Route("api/notifications")]
[Authorize(Policy = "AdminOrTecnico")]
public class NotificationsController : BaseController
{
    private readonly NotifHistorialService _historialSvc;
    private readonly NotifEnvioService     _envioSvc;

    public NotificationsController(
        NotifHistorialService historialSvc,
        NotifEnvioService     envioSvc)
    {
        _historialSvc = historialSvc;
        _envioSvc     = envioSvc;
    }

    // ── US-36 · Historial de notificaciones por cliente ───────────────────────
    /// <summary>
    /// GET /api/clients/{clientId}/notifications
    /// Retorna el historial paginado de notificaciones enviadas a un cliente.
    /// </summary>
    [HttpGet("/api/clients/{clientId:guid}/notifications")]
    // BUG FIX: Policy centralizada
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> GetHistorialCliente(
        Guid     clientId,
        [FromQuery] int      page     = 1,
        [FromQuery] int      pageSize = 20,
        [FromQuery] string?  tipo     = null,
        [FromQuery] DateTime? desde   = null,
        [FromQuery] DateTime? hasta   = null)
    {
        var result = await _historialSvc.GetHistorialClienteAsync(
            clientId, page, pageSize, tipo, desde, hasta);
        return OkResult(result);
    }

    // ── US-39 · Cancelar notificación individual ──────────────────────────────
    /// <summary>
    /// DELETE /api/notifications/{notifId}
    /// Cancela una notificación pendiente o en espera.
    /// </summary>
    [HttpDelete("{notifId:guid}")]
    // BUG FIX: Policy centralizada
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CancelNotif(Guid notifId)
    {
        var result = await _envioSvc.CancelNotifAsync(
            notifId, CurrentUserId, CurrentUserName, ClientIp);

        return result.IsSuccess
            ? OkMessage("Notificación cancelada correctamente.")
            : BadRequestResult(result.ErrorMessage);
    }

    // ── US-39 · Cancelación masiva por tipo ───────────────────────────────────
    /// <summary>
    /// DELETE /api/notifications?tipo={tipo}
    /// Cancela todas las notificaciones pendientes de un tipo dado.
    /// </summary>
    [HttpDelete]
    // BUG FIX: Policy centralizada
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CancelMasiva(
        [FromQuery] string tipo,
        [FromQuery] string? razon = null)
    {
        if (!Enum.TryParse<NotifType>(tipo, out var notifTipo))
            return BadRequestResult($"Tipo de notificación inválido: '{tipo}'.");

        var result = await _envioSvc.CancelMasivaAsync(
            notifTipo, razon, CurrentUserId, CurrentUserName, ClientIp);

        return result.IsSuccess
            ? OkResult(result.Value)
            : BadRequestResult(result.ErrorMessage);
    }
}
