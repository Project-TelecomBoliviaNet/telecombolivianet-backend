using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Clients;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Clients;

[Route("api/clients")]
[Authorize(Policy = "AdminOrTecnico")]
public class ClientsController : BaseController
{
    private readonly ClientService              _service;
    private readonly IValidator<RegisterClientDto>  _registerVal;
    private readonly IValidator<UpdateClientDto>    _updateVal;
    private readonly IValidator<RegisterPaymentDto> _paymentVal;
    // BUG FIX: campos faltantes que causaban NullReferenceException en runtime
    private readonly ClientAttachmentService    _attSvc;
    private readonly ClientHistorialService     _histSvc;
    private readonly IPaymentCreditService      _creditSvc;

    public ClientsController(
        ClientService                  service,
        IValidator<RegisterClientDto>  registerVal,
        IValidator<UpdateClientDto>    updateVal,
        IValidator<RegisterPaymentDto> paymentVal,
        ClientAttachmentService        attSvc,
        ClientHistorialService         histSvc,
        IPaymentCreditService          creditSvc)
    {
        _service     = service;
        _registerVal = registerVal;
        _updateVal   = updateVal;
        _paymentVal  = paymentVal;
        _attSvc      = attSvc;
        _histSvc     = histSvc;
        _creditSvc   = creditSvc;
    }

    // ── US-11 · Previsualizar próximo TBN ────────────────────────────────────

    /// <summary>Devuelve el código TBN que se generará en el próximo registro.</summary>
    [HttpGet("next-tbn")]
    public async Task<IActionResult> PeekTbn()
    {
        var code = await _service.PeekTbnAsync();
        return OkResult(new { NextTbn = code });
    }

    // ── BOT · Buscar cliente por número de teléfono ──────────────────────────

    /// <summary>
    /// GET /api/clients/by-phone?phone={phone}
    /// Busca un cliente por su número WhatsApp principal o secundario.
    /// Acepta el número con o sin prefijo internacional 591.
    /// Devuelve 404 si no existe.
    /// Consumidor: chatbot NestJS — SistemaApiService.getClientByPhone()
    /// </summary>
    [HttpGet("by-phone")]
    [Authorize(Policy = "AllRoles")]   // el bot usa JWT de usuario bot
    public async Task<IActionResult> GetByPhone([FromQuery] string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequestResult("El parámetro 'phone' es obligatorio.");

        // BUG FIX: usar método de instancia en ClientService — ya no se necesita _clientRepo en el controller
        var dto = await _service.GetByPhoneAsync(phone);

        if (dto is null)
            return NotFoundResult("No se encontró un cliente con ese número de teléfono.");

        return OkResult(dto);
    }

    // ── US-13 · Listar clientes ───────────────────────────────────────────────

    /// <summary>Lista paginada de clientes con búsqueda y filtros.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ClientFilterDto filter)
    {
        var result = await _service.GetAllAsync(filter);
        return OkResult(result);
    }

    // ── US-14 · Perfil del cliente ────────────────────────────────────────────

    /// <summary>Obtiene el perfil completo de un cliente.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var client = await _service.GetByIdAsync(id);
        if (client is null) return NotFoundResult("Cliente no encontrado.");
        return OkResult(client);
    }

    // ── US-15 · Grid de facturas del cliente ──────────────────────────────────

    /// <summary>Devuelve el grid de 12 meses de facturas de un cliente.</summary>
    [HttpGet("{id:guid}/invoices")]
    public async Task<IActionResult> GetInvoices(Guid id, [FromQuery] int year = 0)
    {
        if (year == 0) year = DateTime.UtcNow.Year;
        var grid = await _service.GetInvoiceGridAsync(id, year);
        return OkResult(grid);
    }

    // ── BOT · Facturas del cliente por ID (filtradas por año) ────────────────

    /// <summary>
    /// GET /api/clients/{id}/invoices?year={year}
    /// Devuelve todas las facturas de un cliente para el año indicado.
    /// El endpoint ya existía arriba; esta ruta es la misma —
    /// el chatbot lo usa desde SistemaApiService.getClientInvoices().
    /// Respuesta mapeada al formato que espera el bot (campos en PascalCase).
    /// </summary>
    [HttpGet("{id:guid}/invoices/bot")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetInvoicesForBot(Guid id, [FromQuery] int year = 0)
    {
        if (year == 0) year = DateTime.UtcNow.Year;
        var grid = await _service.GetInvoiceGridAsync(id, year);

        // BUG FIX: cruzar con grid.Payments para obtener PaidAt real de cada factura.
        // Payments tiene PaymentInvoices → podemos saber qué pago cubrió cada factura.
        var paidAtMap = grid.Payments
            .SelectMany(p => p.CoveredMonths
                .Select(month => new { Month = month, p.PaidAt }))
            .GroupBy(x => x.Month)
            .ToDictionary(g => g.Key, g => (DateTime?)g.First().PaidAt);

        var invoices = grid.Invoices.Select(i =>
        {
            var monthKey = i.Status == "Pagada"
                ? $"{new System.Globalization.CultureInfo("es-BO").DateTimeFormat.GetMonthName(i.Month)} {i.Year}"
                : null;
            return new
            {
                Id      = i.Id,
                Month   = i.Month,
                Year    = i.Year,
                Amount  = i.Amount,
                DueDate = i.DueDate,
                Status  = i.Status,
                PaidAt  = monthKey != null && paidAtMap.TryGetValue(monthKey, out var paid) ? paid : (DateTime?)null
            };
        });

        return OkResult(new { Invoices = invoices });
    }

    // ── US-12 · Registrar cliente ─────────────────────────────────────────────

    /// <summary>Registra un nuevo cliente con datos personales, instalación y pago inicial.</summary>
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterClientDto dto)
    {
        var v = await _registerVal.ValidateAsync(dto);
        if (!v.IsValid)
            return BadRequest(new { success = false, errors = v.Errors.Select(e => e.ErrorMessage) });

        var result = await _service.RegisterAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return StatusCode(201, new { success = true, data = result.Value });
    }

    // ── US-17 · Editar cliente ────────────────────────────────────────────────

    /// <summary>Edita datos del cliente. Solo Admin puede hacerlo.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClientDto dto)
    {
        var v = await _updateVal.ValidateAsync(dto);
        if (!v.IsValid)
            return BadRequest(new { success = false, errors = v.Errors.Select(e => e.ErrorMessage) });

        var result = await _service.UpdateAsync(id, dto, CurrentUserId, CurrentUserName, ClientIp);
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkMessage("Cliente actualizado correctamente.");
    }

    // ── US-18 · Suspender / Reactivar ────────────────────────────────────────

    /// <summary>Suspende el servicio del cliente y envía notificación WhatsApp.</summary>
    [HttpPut("{id:guid}/suspend")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Suspend(Guid id)
    {
        var result = await _service.SuspendAsync(id, CurrentUserId, CurrentUserName, ClientIp);
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkMessage("Servicio suspendido. Se notificó al cliente por WhatsApp.");
    }

    /// <summary>Reactiva el servicio del cliente.</summary>
    [HttpPut("{id:guid}/reactivate")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var result = await _service.ReactivateAsync(id, CurrentUserId, CurrentUserName, ClientIp);
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkMessage("Servicio reactivado correctamente.");
    }

    // ── US-19 · Baja del servicio ─────────────────────────────────────────────

    /// <summary>
    /// Da de baja al cliente.
    /// Si tiene deuda y confirmed=false, devuelve 409 con el monto pendiente.
    /// Si confirmed=true, procede aunque tenga deuda.
    /// </summary>
    [HttpPut("{id:guid}/cancel")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Cancel(Guid id, [FromQuery] bool confirmed = false)
    {
        var result = await _service.CancelAsync(id, confirmed, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess)
            return BadRequestResult(result.ErrorMessage);

        if (result.Value == "needs_confirmation")
            return StatusCode(409, new
            {
                success = false,
                message = "El cliente tiene deuda pendiente. Envía confirmed=true para dar de baja igualmente.",
                requiresConfirmation = true
            });

        var message = result.Value == "cancelled_with_ticket"
            ? "Cliente dado de baja. Se creó un ticket de recolección de equipo automáticamente."
            : "Cliente dado de baja correctamente.";

        return OkMessage(message);
    }

    // ── US-15 · Registrar pago ────────────────────────────────────────────────

    /// <summary>Registra un pago y marca las facturas seleccionadas como pagadas.</summary>
    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> RegisterPayment(
        Guid id, [FromBody] RegisterPaymentDto dto)
    {
        if (dto.ClientId != id)
            return BadRequestResult("El ID del cliente no coincide.");

        var v = await _paymentVal.ValidateAsync(dto);
        if (!v.IsValid)
            return BadRequest(new { success = false, errors = v.Errors.Select(e => e.ErrorMessage) });

        var result = await _service.RegisterPaymentAsync(dto, CurrentUserId, CurrentUserName, ClientIp);
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkMessage("Pago registrado correctamente. Se notificó al cliente por WhatsApp.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // M5 — US-CLI-BUSQUEDA · Búsqueda avanzada
    // ════════════════════════════════════════════════════════════════════════

    [HttpPost("search")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> Search([FromBody] ClientSearchDto dto)
        => OkResult(await _service.SearchAsync(dto));

    // ════════════════════════════════════════════════════════════════════════
    // M5 — US-CLI-ADJUNTOS · Documentos adjuntos
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Listar adjuntos de un cliente.</summary>
    [HttpGet("{id:guid}/attachments")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetAttachments(Guid id)
        => OkResult(await _attSvc.GetByClientAsync(id));

    /// <summary>Subir nuevo adjunto (multipart/form-data).</summary>
    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = "AdminOrOperador")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(
        Guid id,
        IFormFile file,
        [FromForm] string tipoDoc  = "Otro",
        [FromForm] string? descripcion = null)
    {
        if (file is null || file.Length == 0)
            return BadRequestResult("No se recibió ningún archivo.");

        using var stream = file.OpenReadStream();
        var result = await _attSvc.UploadAsync(
            id,
            file.FileName,
            file.ContentType,
            file.Length,
            stream,
            tipoDoc,
            descripcion,
            CurrentUserId,
            CurrentUserName,
            ClientIp);

        return result.IsSuccess ? OkResult(result.Value) : BadRequestResult(result.ErrorMessage);
    }

    /// <summary>Descargar un adjunto.</summary>
    [HttpGet("{id:guid}/attachments/{attachId:guid}/download")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid attachId)
    {
        var result = await _attSvc.DownloadAsync(attachId);
        if (!result.IsSuccess) return NotFoundResult(result.ErrorMessage);
        var (stream, contentType, fileName) = result.Value;
        return File(stream, contentType, fileName);
    }

    /// <summary>Eliminar un adjunto (soft delete).</summary>
    [HttpDelete("{id:guid}/attachments/{attachId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachId)
    {
        var result = await _attSvc.DeleteAsync(
            attachId, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkMessage("Adjunto eliminado.") : BadRequestResult(result.ErrorMessage);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M5 — US-CLI-HISTORIAL · Historial de actividad
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Historial cronológico completo del cliente.</summary>
    [HttpGet("{id:guid}/historial")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetHistorial(
        Guid    id,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 25,
        [FromQuery] string? tipo     = null,
        [FromQuery] DateTime? desde  = null,
        [FromQuery] DateTime? hasta  = null)
    {
        var result = await _histSvc.GetHistorialAsync(
            id, page, pageSize, tipo, desde, hasta);
        return OkResult(result);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M5 — US-PAG-CREDITO · Reembolso de crédito (ya en PaymentsController,
    //      aquí endpoint alternativo bajo /clients/{id}/credit/reembolsar)
    // ════════════════════════════════════════════════════════════════════════

    [HttpPost("{id:guid}/credit/reembolsar")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ReembolsarCredito(
        Guid id, [FromBody] TelecomBoliviaNet.Application.DTOs.Payments.ReembolsarCreditoDto dto)
    {
        // Forward a PaymentCreditService via DI
        var result = await _creditSvc.ReembolsarCreditoAsync(
            id, dto.Justificacion, CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkMessage("Crédito reembolsado.") : BadRequestResult(result.ErrorMessage);
    }
}
