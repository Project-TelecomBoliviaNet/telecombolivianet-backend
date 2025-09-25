using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Services.Tickets;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Tickets;

/// <summary>CRUD de horarios comerciales configurables. US-07.</summary>
[Route("api/business-schedules")]
[Authorize(Policy = "AdminOnly")]
public class BusinessScheduleController : BaseController
{
    private readonly BusinessScheduleService _svc;

    public BusinessScheduleController(BusinessScheduleService svc)
        => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => OkResult(await _svc.GetAllAsync());

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var dto = await _svc.GetByIdAsync(id);
        return dto is null ? NotFoundResult("Horario no encontrado.") : OkResult(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBusinessScheduleDto dto)
    {
        var result = await _svc.CreateAsync(dto);
        return result.IsSuccess
            ? OkResult(result.Value)
            : BadRequestResult(result.ErrorMessage!);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBusinessScheduleDto dto)
    {
        var result = await _svc.UpdateAsync(id, dto);
        return result.IsSuccess
            ? OkResult(result.Value)
            : BadRequestResult(result.ErrorMessage!);
    }

    [HttpPost("{scheduleId:guid}/holidays")]
    public async Task<IActionResult> AddHoliday(Guid scheduleId, [FromBody] CreateHolidayDto dto)
    {
        var result = await _svc.AddHolidayAsync(scheduleId, dto);
        return result.IsSuccess
            ? OkResult(result.Value)
            : BadRequestResult(result.ErrorMessage!);
    }

    [HttpDelete("{scheduleId:guid}/holidays/{holidayId:guid}")]
    public async Task<IActionResult> DeleteHoliday(Guid scheduleId, Guid holidayId)
    {
        var result = await _svc.DeleteHolidayAsync(scheduleId, holidayId);
        return result.IsSuccess
            ? OkMessage("Feriado eliminado.")
            : BadRequestResult(result.ErrorMessage!);
    }
}
