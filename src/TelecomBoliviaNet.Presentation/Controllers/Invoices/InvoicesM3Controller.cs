using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Invoices;
using TelecomBoliviaNet.Application.Services.Invoices;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Invoices;

/// <summary>
/// BUG FIX: Controller creado para exponer los endpoints de InvoiceM3Service
/// que no tenían controller REST y eran inaccesibles desde el frontend.
/// US-M3: facturas extraordinarias, transición de estado, marcar enviadas, aplicar crédito.
/// </summary>
[Route("api/invoices")]
[Authorize(Policy = "AdminOnly")]
public class InvoicesM3Controller : BaseController
{
    private readonly InvoiceM3Service _m3;

    public InvoicesM3Controller(InvoiceM3Service m3) => _m3 = m3;

    // ── US-M3 · Crear factura extraordinaria ─────────────────────────────────
    /// <summary>Crea una factura extraordinaria (instalación adicional, servicio especial, etc.).</summary>
    [HttpPost("extraordinary")]
    public async Task<IActionResult> CreateExtraordinary([FromBody] CreateExtraordinaryInvoiceDto dto)
    {
        var result = await _m3.CreateExtraordinaryAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? StatusCode(201, new { success = true, data = result.Value })
            : BadRequestResult(result.ErrorMessage);
    }

    // ── US-M3 · Transicionar estado de factura ────────────────────────────────
    /// <summary>Cambia el estado de una factura (Pendiente → Vencida, etc.).</summary>
    [HttpPatch("{id:guid}/estado")]
    public async Task<IActionResult> TransicionarEstado(Guid id, [FromBody] TransicionEstadoDto dto)
    {
        if (!Enum.TryParse<InvoiceStatus>(dto.NuevoEstado, out var estado))
            return BadRequestResult($"Estado inválido: {dto.NuevoEstado}.");

        var result = await _m3.TransicionarEstadoAsync(id, estado, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Estado de factura actualizado.")
            : BadRequestResult(result.ErrorMessage);
    }

    // ── US-M3 · Marcar facturas como enviadas ────────────────────────────────
    /// <summary>Marca todas las facturas de un período como enviadas (notificadas al cliente).</summary>
    [HttpPost("marcar-enviadas")]
    public async Task<IActionResult> MarcarEnviadas([FromBody] MarcarEnviadasDto dto)
    {
        var count = await _m3.MarcarFacturasEnviadasAsync(dto.Year, dto.Month, CurrentUserId, CurrentUserName, ClientIp);
        return OkResult(new MarcarEnviadasResultDto(count, $"{dto.Month:D2}/{dto.Year}"));
    }

    // ── US-M3 · Aplicar crédito a factura ────────────────────────────────────
    /// <summary>Aplica el crédito disponible del cliente para saldar (parcial o totalmente) una factura.</summary>
    [HttpPost("{id:guid}/aplicar-credito")]
    public async Task<IActionResult> AplicarCredito(Guid id)
    {
        var result = await _m3.AplicarCreditoAsync(id, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess
            ? OkMessage("Crédito aplicado correctamente.")
            : BadRequestResult(result.ErrorMessage);
    }
}
