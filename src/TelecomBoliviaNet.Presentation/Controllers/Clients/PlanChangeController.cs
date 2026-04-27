using TelecomBoliviaNet.Application.DTOs.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.Services.Clients;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Clients;

[Route("api/plan-changes")]
[Authorize(Policy = "AdminOrTecnico")]
public class PlanChangeController : BaseController
{
    private readonly PlanChangeService _svc;
    public PlanChangeController(PlanChangeService svc) => _svc = svc;

    /// <summary>
    /// GET /api/plan-changes/pending — lista solicitudes pendientes.
    /// Parámetro opcional: ?clientId={guid} para filtrar por cliente específico.
    /// Usado por la pestaña "Cambio de Plan" en el perfil del cliente.
    /// </summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendientes([FromQuery] Guid? clientId = null)
        => OkResult(await _svc.GetPendientesAsync(clientId));

    /// <summary>
    /// POST /api/clients/{clientId}/plan-change
    /// Solicita un cambio de plan (crea ticket automáticamente).
    /// </summary>
    [HttpPost("/api/clients/{clientId:guid}/plan-change")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> Solicitar(
        Guid clientId,
        [FromBody] SolicitarCambioDto dto)
    {
        var result = await _svc.SolicitarCambioAsync(
            clientId, dto.NewPlanId, dto.Notes,
            CurrentUserId, CurrentUserName, ClientIp);

        return result.IsSuccess
            ? StatusCode(201, new { success = true, data = new { ChangeId = result.Value } })
            : BadRequestResult(result.ErrorMessage!);
    }

    /// <summary>
    /// PATCH /api/plan-changes/{id}/approve
    /// Aprueba el cambio. midMonth=true ejecuta el cambio inmediatamente con facturas proporcionales.
    /// </summary>
    [HttpPatch("{id:guid}/approve")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Aprobar(Guid id, [FromQuery] bool midMonth = false)
    {
        var result = await _svc.AprobarCambioAsync(
            id, midMonth, CurrentUserId, CurrentUserName, ClientIp);

        return result.IsSuccess
            ? OkMessage(midMonth
                ? "Cambio de plan aplicado inmediatamente. Facturas proporcionales generadas."
                : "Cambio de plan aprobado. Se aplicará el 1ro del mes siguiente.")
            : BadRequestResult(result.ErrorMessage!);
    }

    /// <summary>PATCH /api/plan-changes/{id}/reject — rechaza el cambio.</summary>
    [HttpPatch("{id:guid}/reject")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Rechazar(Guid id, [FromBody] RechazarCambioDto dto)
    {
        var result = await _svc.RechazarCambioAsync(
            id, dto.Motivo, CurrentUserId, CurrentUserName, ClientIp);

        return result.IsSuccess
            ? OkMessage("Solicitud rechazada.")
            : BadRequestResult(result.ErrorMessage!);
    }
}

// BUG FIX: DTOs movidos a TelecomBoliviaNet.Application/DTOs/Clients/PlanChangeDtos.cs
