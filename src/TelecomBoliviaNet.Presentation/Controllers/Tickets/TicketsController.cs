using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Services.Tickets;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Tickets;

[Route("api/tickets")]
[Authorize(Policy = "AdminOrTecnico")]
public class TicketsController : BaseController
{
    private readonly TicketService                     _svc;
    private readonly IValidator<CreateTicketDto>       _createVal;
    private readonly IValidator<UpdateTicketDto>       _updateVal;
    private readonly IValidator<ChangeTicketStatusDto> _statusVal;
    private readonly IValidator<AssignTicketDto>       _assignVal;
    private readonly IValidator<AddCommentDto>         _commentVal;
    private readonly IValidator<AddWorkLogDto>         _workLogVal;
    private readonly IValidator<ScheduleVisitDto>      _visitVal;
    // BUG FIX: campos faltantes — causaban NullReferenceException en balanceo y adjuntos
    private readonly TicketBalanceoService             _balanceoSvc;
    private readonly TicketAttachmentService           _attSvc;

    public TicketsController(
        TicketService                     svc,
        IValidator<CreateTicketDto>       createVal,
        IValidator<UpdateTicketDto>       updateVal,
        IValidator<ChangeTicketStatusDto> statusVal,
        IValidator<AssignTicketDto>       assignVal,
        IValidator<AddCommentDto>         commentVal,
        IValidator<AddWorkLogDto>         workLogVal,
        IValidator<ScheduleVisitDto>      visitVal,
        TicketBalanceoService             balanceoSvc,
        TicketAttachmentService           attSvc)
    {
        _svc        = svc;
        _createVal  = createVal;
        _updateVal  = updateVal;
        _statusVal  = statusVal;
        _assignVal  = assignVal;
        _commentVal = commentVal;
        _workLogVal = workLogVal;
        _visitVal   = visitVal;
        _balanceoSvc = balanceoSvc;
        _attSvc      = attSvc;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] TicketFilterDto f)
        => OkResult(await _svc.GetAllAsync(f));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var t = await _svc.GetByIdAsync(id);
        return t is null ? NotFoundResult("Ticket no encontrado.") : OkResult(t);
    }

    [HttpGet("kanban")]
    public async Task<IActionResult> GetKanban([FromQuery] TicketFilterDto f)
        => OkResult(await _svc.GetKanbanAsync(f));

    [HttpGet("kpi")]
    public async Task<IActionResult> GetKpi()
        => OkResult(await _svc.GetKpiAsync());

    [HttpGet("overdue-sla")]
    public async Task<IActionResult> GetOverdueSla()
        => OkResult(await _svc.GetOverdueSlaAsync());

    [HttpGet("by-client/{clientId:guid}")]
    public async Task<IActionResult> GetByClient(Guid clientId)
        => OkResult(await _svc.GetByClientAsync(clientId));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTicketDto dto)
    {
        var v = await _createVal.ValidateAsync(dto);
        if (!v.IsValid) return BadRequestResult(string.Join(" ", v.Errors.Select(e => e.ErrorMessage)));
        var r = await _svc.CreateAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTicketDto dto)
    {
        var v = await _updateVal.ValidateAsync(dto);
        if (!v.IsValid) return BadRequestResult(string.Join(" ", v.Errors.Select(e => e.ErrorMessage)));
        var r = await _svc.UpdateAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeTicketStatusDto dto)
    {
        var v = await _statusVal.ValidateAsync(dto);
        if (!v.IsValid) return BadRequestResult(string.Join(" ", v.Errors.Select(e => e.ErrorMessage)));
        var r = await _svc.ChangeStatusAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPatch("{id:guid}/assign")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTicketDto dto)
    {
        var v = await _assignVal.ValidateAsync(dto);
        if (!v.IsValid) return BadRequestResult(string.Join(" ", v.Errors.Select(e => e.ErrorMessage)));
        var r = await _svc.AssignTechnicianAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPatch("{id:guid}/close")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Close(Guid id)
    {
        var r = await _svc.CloseAsync(id, CurrentUserId, CurrentUserName, ClientIp);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPost("{id:guid}/comments")]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] AddCommentDto dto)
    {
        var v = await _commentVal.ValidateAsync(dto);
        if (!v.IsValid) return BadRequestResult(string.Join(" ", v.Errors.Select(e => e.ErrorMessage)));
        var r = await _svc.AddCommentAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPost("{id:guid}/worklogs")]
    public async Task<IActionResult> AddWorkLog(Guid id, [FromBody] AddWorkLogDto dto)
    {
        var v = await _workLogVal.ValidateAsync(dto);
        if (!v.IsValid) return BadRequestResult(string.Join(" ", v.Errors.Select(e => e.ErrorMessage)));
        var r = await _svc.AddWorkLogAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPost("{id:guid}/visits")]
    public async Task<IActionResult> ScheduleVisit(Guid id, [FromBody] ScheduleVisitDto dto)
    {
        var v = await _visitVal.ValidateAsync(dto);
        if (!v.IsValid) return BadRequestResult(string.Join(" ", v.Errors.Select(e => e.ErrorMessage)));
        var r = await _svc.ScheduleVisitAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPost("{id:guid}/csat")]
    [AllowAnonymous]
    public async Task<IActionResult> SubmitCsat(Guid id, [FromBody] SubmitCsatDto dto)
    {
        var r = await _svc.SubmitCsatAsync(id, dto, ClientIp);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    // SLA Plans
    [HttpGet("sla-plans")]
    public async Task<IActionResult> GetSlaPlans()
        => OkResult(await _svc.GetSlaPlansAsync());

    [HttpPost("sla-plans")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CreateSlaPlan([FromBody] CreateSlaPlanDto dto)
    {
        var r = await _svc.CreateSlaPlanAsync(dto);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpPut("sla-plans/{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateSlaPlan(Guid id, [FromBody] UpdateSlaPlanDto dto)
    {
        var r = await _svc.UpdateSlaPlanAsync(id, dto);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    [HttpDelete("sla-plans/{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteSlaPlan(Guid id)
    {
        var r = await _svc.DeleteSlaPlanAsync(id);
        return r.IsSuccess ? OkResult(r.Value) : BadRequestResult(r.ErrorMessage);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M9 — US-TKT-TIPOS · Tipos disponibles
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet("types")]
    [Authorize(Policy = "AllRoles")]
    public IActionResult GetTypes()
        => OkResult(Enum.GetNames<TicketType>());

    // ════════════════════════════════════════════════════════════════════════
    // M9 — US-TKT-BALANCEO · Resumen de carga de técnicos
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet("balanceo")]
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> GetBalanceoResumen()
        => OkResult(new BalanceoResumenDto(await _balanceoSvc.GetCargaResumenAsync()));

    // ════════════════════════════════════════════════════════════════════════
    // M9 — US-TKT-ADJ · Adjuntos de ticket
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet("{id:guid}/attachments")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetAttachments(Guid id)
        => OkResult(await _attSvc.GetByTicketAsync(id));

    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = "AdminOrTecnico")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(
        Guid id, IFormFile file, [FromForm] string? descripcion = null)
    {
        if (file is null || file.Length == 0) return BadRequestResult("No se recibió archivo.");
        using var stream = file.OpenReadStream();
        var result = await _attSvc.UploadAsync(
            id, file.FileName, file.ContentType, file.Length,
            stream, descripcion, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkResult(result.Value) : BadRequestResult(result.ErrorMessage);
    }

    [HttpGet("{id:guid}/attachments/{attId:guid}/download")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid attId)
    {
        var result = await _attSvc.DownloadAsync(attId);
        if (!result.IsSuccess) return NotFoundResult(result.ErrorMessage);
        var (stream, ct, fn) = result.Value;
        return File(stream, ct, fn);
    }

    [HttpDelete("{id:guid}/attachments/{attId:guid}")]
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attId)
    {
        var result = await _attSvc.DeleteAsync(attId, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkMessage("Adjunto eliminado.") : BadRequestResult(result.ErrorMessage);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M9 — US-TKT-SLA · Reporte SLA extendido
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet("sla-report")]
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> GetSlaReport(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        [FromQuery] string?   tecnico = null)
    {
        // BUG FIX: pasar los filtros reales al servicio en lugar de ignorarlos
        var overdue = await _svc.GetSlaReportAsync(desde, hasta, tecnico);
        return OkResult(overdue);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M9 — US-TKT-09 · Reabrir ticket cerrado (solo Admin)
    // ════════════════════════════════════════════════════════════════════════

    [HttpPost("{id:guid}/reopen")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Reopen(Guid id, [FromBody] ReopenTicketDto dto)
    {
        var result = await _svc.ChangeStatusAsync(id,
            new ChangeTicketStatusDto { Status = "Abierto", ResolutionMessage = null },
            CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Ticket reabierto.")
            : BadRequestResult(result.ErrorMessage);
    }
}

public record ReopenTicketDto(string Motivo);
