using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Installations.Commands;
using TelecomBoliviaNet.Application.Installations.Queries;
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
    private readonly IMediator _mediator;

    public InstallationsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("slots-disponibles")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetSlotsDisponibles([FromQuery] int dias = 7)
    {
        if (dias < 1 || dias > 30)
            return BadRequestResult("El parámetro 'dias' debe estar entre 1 y 30.");

        var slots = await _mediator.Send(new GetInstalacionSlotsQuery(dias));
        return OkResult(slots);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] InstalacionFilterDto filter)
        => OkResult(await _mediator.Send(new GetInstalacionesListQuery(filter)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var detalle = await _mediator.Send(new GetInstalacionDetalleQuery(id));
        return detalle is null
            ? NotFoundResult("Instalación no encontrada.")
            : OkResult(detalle);
    }

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

        var result = await _mediator.Send(
            new CrearInstalacionCommand(dto, CurrentUserId, CurrentUserName, ClientIp));
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage!);

        return StatusCode(201, new { success = true, data = result.Value });
    }

    [HttpPost("admin")]
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> CrearAdmin([FromBody] CrearInstalacionAdminDto dto)
    {
        var result = await _mediator.Send(
            new CrearInstalacionAdminCommand(dto, CurrentUserId, CurrentUserName, ClientIp));
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage!);

        return StatusCode(201, new { success = true, data = result.Value });
    }

    [HttpPatch("{id:guid}/cancelar")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelarInstalacionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.MotivoCancelacion))
            return BadRequestResult("El motivo de cancelación es obligatorio.");

        var result = await _mediator.Send(
            new CancelarInstalacionCommand(id, dto, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess
            ? OkMessage("Instalación cancelada correctamente.")
            : BadRequestResult(result.ErrorMessage!);
    }

    [HttpPatch("{id:guid}/reprogramar")]
    public async Task<IActionResult> Reprogramar(Guid id, [FromBody] ReprogramarInstalacionDto dto)
    {
        var result = await _mediator.Send(
            new ReprogramarInstalacionCommand(id, dto, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess
            ? OkMessage("Instalación reprogramada correctamente.")
            : BadRequestResult(result.ErrorMessage!);
    }

    [HttpPatch("{id:guid}/completar")]
    public async Task<IActionResult> Completar(Guid id, [FromBody] CompletarInstalacionDto dto)
    {
        var result = await _mediator.Send(
            new CompletarInstalacionCommand(id, dto, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess
            ? OkMessage("Instalación completada. El ticket fue resuelto automáticamente.")
            : BadRequestResult(result.ErrorMessage!);
    }

    [HttpPatch("{id:guid}/tecnico")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AsignarTecnico(Guid id, [FromBody] AsignarTecnicoDto dto)
    {
        var result = await _mediator.Send(
            new AsignarTecnicoCommand(id, dto, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess
            ? OkMessage("Técnico asignado correctamente.")
            : BadRequestResult(result.ErrorMessage!);
    }
}
