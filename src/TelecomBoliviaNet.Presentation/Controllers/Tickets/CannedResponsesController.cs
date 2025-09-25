using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Tickets.Commands;
using TelecomBoliviaNet.Application.Tickets.Queries;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Tickets;

[Route("api/canned-responses")]
[Authorize(Policy = "AdminOrTecnico")]
public class CannedResponsesController : BaseController
{
    private readonly IMediator _mediator;

    public CannedResponsesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false)
        => OkResult(await _mediator.Send(new GetCannedResponsesQuery(includeInactive)));

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateCannedResponseDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Body))
            return BadRequestResult("Título y cuerpo son obligatorios.");
        var r = await _mediator.Send(new CreateCannedResponseCommand(dto, CurrentUserId));
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCannedResponseDto dto)
    {
        var r = await _mediator.Send(new UpdateCannedResponseCommand(id, dto));
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var r = await _mediator.Send(new DeleteCannedResponseCommand(id));
        return r.IsSuccess ? OkMessage("Plantilla eliminada.") : BadRequestResult(r.ErrorMessage);
    }
}
