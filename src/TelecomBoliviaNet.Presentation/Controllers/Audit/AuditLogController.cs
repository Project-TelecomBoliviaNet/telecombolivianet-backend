using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Audit;

[Route("api/audit-logs")]
[Authorize(Policy = "AdminOnly")]
public class AuditLogController : BaseController
{
    private readonly AuditLogService _service;

    public AuditLogController(AuditLogService service)
    {
        _service = service;
    }

    /// <summary>
    /// US-09 · Consultar el audit log con filtros.
    /// Solo lectura — el log nunca se modifica ni elimina.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] AuditLogFilterDto filter)
    {
        var result = await _service.GetAsync(filter);
        return OkResult(result);
    }

    /// <summary>
    /// Exporta el audit log filtrado como archivo CSV.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] AuditLogFilterDto filter)
    {
        var csv = await _service.ExportCsvAsync(filter);
        var fileName = $"audit-log-{DateTime.UtcNow:yyyy-MM-dd}.csv";
        return File(csv, "text/csv; charset=utf-8", fileName);
    }
}
