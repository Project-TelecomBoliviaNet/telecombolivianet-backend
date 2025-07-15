using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Services.Clients;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Clients;

/// <summary>
/// Endpoints de QR de pago por cliente.
/// GET  /api/clients/{id}/qr         → descarga imagen (chatbot + admin)
/// POST /api/clients/{id}/qr         → sube nueva imagen (solo admin)
/// GET  /api/clients/{id}/qr/info    → metadatos del QR activo
/// GET  /api/clients/{id}/qr/history → historial de QRs
/// </summary>
[Route("api/clients/{clientId:guid}/qr")]
public class ClientQrController : BaseController
{
    private readonly ClientQrService _svc;

    public ClientQrController(ClientQrService svc) => _svc = svc;

    // ── GET imagen — chatbot y admin ──────────────────────────────────────────

    /// <summary>
    /// GET /api/clients/{clientId}/qr
    /// Devuelve la imagen binaria del QR activo del cliente.
    /// Consumidor: chatbot NestJS — SistemaApiService.getQrImageBuffer()
    /// Devuelve 404 si no hay QR activo o si expiró.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetQrImage(Guid clientId)
    {
        var (_, bytes, contentType) = await _svc.GetActiveQrAsync(clientId);

        // BUG FIX: verificar también contentType para evitar NullReferenceException
        // cuando el archivo existe en BD pero no puede leerse del disco.
        if (bytes is null || contentType is null)
            return NotFoundResult("El cliente no tiene un QR activo o ha expirado.");

        return File(bytes, contentType);
    }

    // ── GET metadatos ─────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/clients/{clientId}/qr/info
    /// Devuelve metadatos del QR activo (URL, expiración, días restantes).
    /// </summary>
    [HttpGet("info")]
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> GetQrInfo(Guid clientId)
    {
        var (dto, _, _) = await _svc.GetActiveQrAsync(clientId);
        if (dto is null)
            return NotFoundResult("El cliente no tiene un QR activo.");
        return OkResult(dto);
    }

    // ── GET historial ─────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/clients/{clientId}/qr/history
    /// Historial completo de QRs del cliente (activos e inactivos).
    /// </summary>
    [HttpGet("history")]
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> GetHistory(Guid clientId)
    {
        var historial = await _svc.GetHistorialAsync(clientId);
        return OkResult(historial);
    }

    // ── POST subir QR — solo admin ────────────────────────────────────────────

    /// <summary>
    /// POST /api/clients/{clientId}/qr
    /// Sube una nueva imagen de QR para el cliente.
    /// Si ya existía un QR activo, lo desactiva automáticamente.
    /// Acepta multipart/form-data con campo "file" y "ExpiresInDays" opcional.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UploadQr(
        Guid clientId,
        IFormFile file,
        [FromForm] UpdateClientQrDto meta)
    {
        var result = await _svc.UploadQrAsync(
            clientId, file, meta,
            CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess)
            return BadRequestResult(result.ErrorMessage!);

        return StatusCode(201, new { success = true, data = result.Value });
    }
}
