using TelecomBoliviaNet.Domain.Entities.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Invoices;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Invoices;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Invoices;

[Route("api/invoices")]
public class InvoicesController : BaseController
{
    private readonly BillingService      _billing;
    private readonly InvoiceQueryService _query;
    private readonly AuditService        _audit;
    private readonly IExportService      _export;

    public InvoicesController(
        BillingService      billing,
        InvoiceQueryService query,
        AuditService        audit,
        IExportService      export)
    {
        _billing = billing;
        _query   = query;
        _audit   = audit;
        _export  = export;
    }

    /// <summary>US-23 · Estadísticas financieras del mes.</summary>
    [HttpGet("stats")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetStats([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2024 || month < 1 || month > 12)
            return BadRequestResult("Año o mes inválido.");
        return OkResult(await _query.GetMonthStatsAsync(year, month));
    }

    /// <summary>US-23 · Listado paginado de facturas con filtros.</summary>
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAll([FromQuery] InvoiceFilterDto filter)
        => OkResult(await _query.GetPagedAsync(filter));

    /// <summary>US-27 · Reporte anual — Admin y SocioLectura.</summary>
    [HttpGet("annual-report")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetAnnualReport([FromQuery] AnnualReportFilterDto filter)
    {
        if (filter.Year < 2024)
            return BadRequestResult("Año inválido.");
        return OkResult(await _query.GetAnnualReportAsync(filter));
    }

    /// <summary>US-26 · Ejecutar manualmente el job de facturación.</summary>
    [HttpPost("generate")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GenerateManual([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2024 || month < 1 || month > 12)
            return BadRequestResult("Año o mes inválido.");
        var result = await _billing.GenerateMonthlyInvoicesAsync(
            year, month, CurrentUserId, CurrentUserName);
        return OkResult(result);
    }

    /// <summary>US-26 · Forzar marcado de facturas vencidas.</summary>
    [HttpPost("mark-overdue")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> MarkOverdue()
    {
        var count = await _billing.MarkOverdueInvoicesAsync(CurrentUserId, CurrentUserName);
        return OkResult(new { MarkedOverdue = count });
    }

    /// <summary>US-24 · Descargar listado de facturas en Excel.</summary>
    [HttpGet("export/excel")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ExportExcel([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2024 || month < 1 || month > 12)
            return BadRequestResult("Año o mes inválido.");

        var stats    = await _query.GetMonthStatsAsync(year, month);
        var invoices = await _query.GetAllForExportAsync(year, month);
        var bytes    = _export.ExportInvoicesToExcel(invoices, stats);

        await _audit.LogAsync("Facturación", "INVOICE_EXPORT",
            $"Exportación Excel facturas {month}/{year}",
            userId: CurrentUserId, userName: CurrentUserName, ip: ClientIp);

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Facturas_{MonthName(month)}_{year}.xlsx");
    }

    /// <summary>US-24 · Descargar listado de facturas en PDF.</summary>
    [HttpGet("export/pdf")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ExportPdf([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2024 || month < 1 || month > 12)
            return BadRequestResult("Año o mes inválido.");

        var stats    = await _query.GetMonthStatsAsync(year, month);
        var invoices = await _query.GetAllForExportAsync(year, month);
        var bytes    = _export.ExportInvoicesToPdf(invoices, stats);

        await _audit.LogAsync("Facturación", "INVOICE_EXPORT",
            $"Exportación PDF facturas {month}/{year}",
            userId: CurrentUserId, userName: CurrentUserName, ip: ClientIp);

        return File(bytes, "application/pdf",
            $"Facturas_{MonthName(month)}_{year}.pdf");
    }

    // BUG FIX: LogExport eliminado — era un endpoint legacy manipulable que permitía
    // contaminar el audit log con datos falsos sin que ninguna exportación real ocurriera.
    // El audit log se registra directamente en ExportExcel y ExportPdf.

    private static string MonthName(int m) => m switch
    {
        1 => "Enero", 2 => "Febrero", 3 => "Marzo", 4 => "Abril",
        5 => "Mayo",  6 => "Junio",   7 => "Julio",  8 => "Agosto",
        9 => "Septiembre", 10 => "Octubre", 11 => "Noviembre", 12 => "Diciembre",
        _ => m.ToString()
    };

    // BUG FIX: LogAnnualExport eliminado — mismo problema que LogExport.
    // El audit se registra en el endpoint de exportación real cuando se implemente.

        /// <summary>US-25 · Anular factura con justificación.</summary>
    [HttpPut("{id:guid}/void")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> VoidInvoice(Guid id, [FromBody] VoidInvoiceDto dto)
    {
        var result = await _billing.VoidInvoiceAsync(
            id, dto.Justification, CurrentUserId, CurrentUserName, ClientIp);
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkMessage("Factura anulada correctamente.");
    }
}
