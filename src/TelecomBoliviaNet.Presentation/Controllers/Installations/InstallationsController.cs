using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Services.Installations;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Installations;

/// <summary>
/// Módulo de Instalaciones — endpoints REST.
///
/// Consumidores:
///   - Chatbot NestJS → slots-disponibles, POST /, PATCH /{id}/cancelar
///   - Panel Admin React → todos los endpoints
/// </summary>
[Route("api/instalaciones")]
[Authorize(Policy = "AdminOrTecnico")]
public class InstallationsController : BaseController
{
    private readonly InstallationService _svc;

    public InstallationsController(InstallationService svc) => _svc = svc;

    // ── GET slots disponibles (consumido por el chatbot) ──────────────────────

    /// <summary>
    /// GET /api/instalaciones/slots-disponibles?dias=7
    /// Devuelve los slots de tiempo disponibles para los próximos N días.
    /// Consumidor principal: chatbot NestJS — SistemaApiService.getInstallationSlots()
    /// </summary>
    [HttpGet("slots-disponibles")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetSlotsDisponibles([FromQuery] int dias = 7)
    {
        if (dias < 1 || dias > 30)
            return BadRequestResult("El parámetro 'dias' debe estar entre 1 y 30.");

        var slots = await _svc.GetSlotsDisponiblesAsync(dias);
        return OkResult(slots);
    }

    // ── GET listado (panel admin) ─────────────────────────────────────────────

    /// <summary>
    /// GET /api/instalaciones
    /// Lista paginada de instalaciones con filtros.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] InstalacionFilterDto filter)
        => OkResult(await _svc.GetAllAsync(filter));

    // ── GET detalle ───────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/instalaciones/{id}
    /// Detalle completo de una instalación.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var detalle = await _svc.GetDetalleAsync(id);
        return detalle is null
            ? NotFoundResult("Instalación no encontrada.")
            : OkResult(detalle);
    }

    // ── POST crear (consumido por el chatbot y el panel admin) ────────────────

    /// <summary>
    /// POST /api/instalaciones
    /// Agenda una instalación nueva y crea el ticket automáticamente.
    /// Consumidor: chatbot NestJS (SistemaApiService.createInstallation())
    ///             y panel admin React.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> Crear([FromBody] CrearInstalacionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Fecha))
            return BadRequestResult("La fecha es obligatoria.");
        if (string.IsNullOrWhiteSpace(dto.HoraInicio))
            return BadRequestResult("La hora de inicio es obligatoria.");
        if (string.IsNullOrWhiteSpace(dto.Direccion))
            return BadRequestResult("La dirección es obligatoria.");

        var result = await _svc.CrearAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage!);

        return StatusCode(201, new { success = true, data = result.Value });
    }

    /// <summary>
    /// POST /api/instalaciones/admin
    /// Versión extendida para el panel admin (incluye técnico asignable, duración).
    /// </summary>
    [HttpPost("admin")]
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> CrearAdmin([FromBody] CrearInstalacionAdminDto dto)
    {
        var result = await _svc.CrearAdminAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage!);

        return StatusCode(201, new { success = true, data = result.Value });
    }

    // ── PATCH cancelar ────────────────────────────────────────────────────────

    /// <summary>
    /// PATCH /api/instalaciones/{id}/cancelar
    /// Cancela una instalación y cierra el ticket asociado.
    /// Consumidor: chatbot NestJS (SistemaApiService.cancelInstallation())
    ///             y panel admin React.
    /// </summary>
    [HttpPatch("{id:guid}/cancelar")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelarInstalacionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.MotivoCancelacion))
            return BadRequestResult("El motivo de cancelación es obligatorio.");

        var result = await _svc.CancelarAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Instalación cancelada correctamente.")
            : BadRequestResult(result.ErrorMessage!);
    }

    // ── PATCH reprogramar ─────────────────────────────────────────────────────

    /// <summary>
    /// PATCH /api/instalaciones/{id}/reprogramar
    /// Mueve la instalación a otro slot disponible.
    /// </summary>
    [HttpPatch("{id:guid}/reprogramar")]
    public async Task<IActionResult> Reprogramar(Guid id, [FromBody] ReprogramarInstalacionDto dto)
    {
        var result = await _svc.ReprogramarAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Instalación reprogramada correctamente.")
            : BadRequestResult(result.ErrorMessage!);
    }

    // ── PATCH completar ───────────────────────────────────────────────────────

    /// <summary>
    /// PATCH /api/instalaciones/{id}/completar
    /// Marca la instalación como completada y resuelve el ticket.
    /// Solo Admin o Técnico.
    /// </summary>
    [HttpPatch("{id:guid}/completar")]
    public async Task<IActionResult> Completar(Guid id, [FromBody] CompletarInstalacionDto dto)
    {
        var result = await _svc.CompletarAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Instalación completada. El ticket fue resuelto automáticamente.")
            : BadRequestResult(result.ErrorMessage!);
    }

    // ── PATCH asignar técnico ─────────────────────────────────────────────────

    /// <summary>
    /// PATCH /api/instalaciones/{id}/tecnico
    /// Asigna un técnico a la instalación.
    /// </summary>
    [HttpPatch("{id:guid}/tecnico")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AsignarTecnico(Guid id, [FromBody] AsignarTecnicoDto dto)
    {
        var result = await _svc.AsignarTecnicoAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Técnico asignado correctamente.")
            : BadRequestResult(result.ErrorMessage!);
    }
}
