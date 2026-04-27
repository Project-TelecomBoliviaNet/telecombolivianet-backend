using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Application.Services.Payments;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Payments;

[Route("api/payments")]
public class PaymentsController : BaseController
{
    private readonly PaymentService          _svc;
    private readonly WhatsAppReceiptService  _receiptSvc;
    private readonly PaymentCreditService    _creditSvc;

    public PaymentsController(
        PaymentService svc,
        WhatsAppReceiptService receiptSvc,
        PaymentCreditService creditSvc)
    {
        _svc        = svc;
        _receiptSvc = receiptSvc;
        _creditSvc  = creditSvc;
    }

    // ── US-28 · Listado centralizado de pagos ─────────────────────────────────
    /// <summary>Lista todos los pagos del sistema con filtros.</summary>
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAll([FromQuery] PaymentFilterDto filter)
        => OkResult(await _svc.GetAllAsync(filter));

    /// <summary>Detalle de un pago con imagen y facturas cubiertas.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var p = await _svc.GetByIdAsync(id);
        if (p is null) return NotFoundResult("Pago no encontrado.");
        return OkResult(p);
    }

    // ── US-29 · Subir/adjuntar imagen de comprobante ──────────────────────────
    /// <summary>
    /// Adjunta o reemplaza la imagen de comprobante de un pago.
    /// Acepta multipart/form-data con campo "file".
    /// </summary>
    [HttpPost("{id:guid}/receipt-image")]
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> UploadReceiptImage(Guid id, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequestResult("Debes adjuntar un archivo.");

        if (file.Length > 5 * 1024 * 1024)
            return BadRequestResult("El archivo no puede superar 5 MB.");

        var allowed = new[] { "image/jpeg", "image/png", "application/pdf" };
        if (!allowed.Contains(file.ContentType.ToLower()))
            return BadRequestResult("Formato no permitido. Use JPG, PNG o PDF.");

        using var stream = file.OpenReadStream();
        var result = await _svc.AttachReceiptImageAsync(
            id, stream, file.FileName, file.ContentType,
            CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkResult(new { Url = result.Value });
    }

    // ── US-31 · Anular pago ───────────────────────────────────────────────────
    /// <summary>
    /// Anula un pago: revierte las facturas a pendiente/vencida y notifica al cliente.
    /// Solo disponible en los 30 días posteriores al registro.
    /// </summary>
    [HttpPut("{id:guid}/void")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> VoidPayment(Guid id, [FromBody] VoidPaymentDto dto)
    {
        var result = await _svc.VoidPaymentAsync(
            id, dto.Justification, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkResult(result.Value);
    }

    // ── US-32 · Reporte de cobranza ───────────────────────────────────────────
    /// <summary>Reporte de cobranza por rango de fechas.</summary>
    [HttpGet("collection-report")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CollectionReport(
        [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (from > to)
            return BadRequestResult("La fecha de inicio no puede ser posterior a la fecha fin.");

        var report = await _svc.GetCollectionReportAsync(from, to);
        return OkResult(report);
    }

    // ── US-35 · Verificar duplicados ──────────────────────────────────────────
    /// <summary>Verifica si puede existir un pago duplicado para el cliente.</summary>
    [HttpGet("check-duplicate")]
    [Authorize(Policy = "AdminOrTecnico")]
    public async Task<IActionResult> CheckDuplicate(
        [FromQuery] Guid clientId, [FromQuery] decimal amount, [FromQuery] DateTime paidAt)
    {
        var result = await _svc.CheckDuplicateAsync(clientId, amount, paidAt);
        return OkResult(result);
    }

    // ── US-34 · Ejecutar job de recordatorios manualmente ────────────────────
    /// <summary>Ejecuta manualmente el envío de recordatorios de pago vencido.</summary>
    [HttpPost("send-reminders")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SendReminders()
    {
        var result = await _svc.SendOverdueRemindersAsync(CurrentUserId, CurrentUserName);
        return OkResult(result);
    }

    // ── US-06 · Recibir comprobante desde el chatbot WhatsApp ────────────────
    /// <summary>
    /// POST /api/payments/whatsapp-receipt
    /// Llamado por el chatbot NestJS cuando un cliente envía una imagen de pago.
    /// Crea un WhatsAppReceipt en estado Pendiente para revisión del admin.
    /// </summary>
    [HttpPost("whatsapp-receipt")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> ReceiveWhatsappReceipt(
        [FromBody] SubmitWhatsappReceiptDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ImageUrl))
            return BadRequestResult("El campo ImageUrl es obligatorio.");

        var result = await _receiptSvc.ReceiveFromBotAsync(dto);
        return result.IsSuccess
            ? OkResult(new { Id = result.Value })
            : BadRequestResult(result.ErrorMessage);
    }

    // ── US-30 · Cola de comprobantes WhatsApp ─────────────────────────────────
    /// <summary>Lista la cola de comprobantes WhatsApp pendientes.</summary>
    [HttpGet("whatsapp-queue")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string status = "Pendiente")
        => OkResult(await _receiptSvc.GetQueueAsync(page, pageSize, status));

    /// <summary>Cantidad de comprobantes pendientes (para badge del sidebar).</summary>
    [HttpGet("whatsapp-queue/count")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetPendingCount()
        => OkResult(new { Count = await _receiptSvc.CountPendingAsync() });

    /// <summary>Aprueba un comprobante y crea el pago asociado.</summary>
    [HttpPost("whatsapp-queue/{id:guid}/approve")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ApproveReceipt(Guid id, [FromBody] ApproveReceiptDto dto)
    {
        var result = await _receiptSvc.ApproveAsync(
            id, dto, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkResult(new { PaymentId = result.Value });
    }

    /// <summary>Rechaza un comprobante y notifica al cliente.</summary>
    [HttpPost("whatsapp-queue/{id:guid}/reject")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RejectReceipt(Guid id, [FromBody] RejectReceiptDto dto)
    {
        var result = await _receiptSvc.RejectAsync(
            id, dto, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkMessage("Comprobante rechazado. Se notificó al cliente.");
    }

    /// <summary>Descarta un comprobante que no corresponde a un pago.</summary>
    [HttpPost("whatsapp-queue/{id:guid}/not-related")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> MarkNotRelated(Guid id)
    {
        var result = await _receiptSvc.MarkNotRelatedAsync(
            id, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkMessage("Comprobante descartado.");
    }

    // ════════════════════════════════════════════════════════════════════
    // M2: US-PAG-CREDITO · US-PAG-CAJA · US-PAG-RECIBO · US-PAG-06
    // ════════════════════════════════════════════════════════════════════

    /// <summary>US-PAG-CREDITO · Registrar pago aplicando crédito existente.</summary>
    [HttpPost("register-with-credit")]
    [Authorize(Policy = "AdminOrOperador")]
    public async Task<IActionResult> RegisterWithCredit([FromBody] RegisterPaymentDto dto)
    {
        var result = await _creditSvc.RegisterPaymentWithCreditAsync(
            dto, CurrentUserId, CurrentUserName, ClientIp);

        return result.IsSuccess ? OkResult(result.Value) : BadRequestResult(result.ErrorMessage);
    }

    /// <summary>US-PAG-CAJA · Obtener turno activo del operador (o abrir uno nuevo).</summary>
    [HttpGet("cash-close/active")]
    [Authorize(Policy = "AdminOrOperador")]
    public async Task<IActionResult> GetActiveTurno()
    {
        var dto = await _creditSvc.GetOrCreateActiveTurnoAsync(CurrentUserId, CurrentUserName);
        return OkResult(dto);
    }

    /// <summary>US-PAG-CAJA · Cerrar turno del operador.</summary>
    [HttpPost("cash-close/close")]
    [Authorize(Policy = "AdminOrOperador")]
    public async Task<IActionResult> CerrarTurno()
    {
        var result = await _creditSvc.CerrarTurnoAsync(CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkResult(result.Value) : BadRequestResult(result.ErrorMessage);
    }

    /// <summary>US-PAG-CAJA · Historial de cierres para supervisor/admin.</summary>
    [HttpGet("cash-close")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetCashCloses(
        [FromQuery] Guid? operatorId,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta)
    {
        var list = await _creditSvc.GetCashClosesAsync(operatorId, desde, hasta);
        return OkResult(list);
    }

    /// <summary>US-PAG-RECIBO · Descargar PDF del recibo de un pago.</summary>
    [HttpGet("{id:guid}/receipt")]
    [Authorize(Policy = "AdminOrOperador")]
    public async Task<IActionResult> DownloadReceipt(Guid id)
    {
        var receipt = await _creditSvc.GetReceiptByPaymentAsync(id);
        if (receipt is null) return NotFoundResult("Recibo no encontrado.");
        if (string.IsNullOrEmpty(receipt.PdfPath)) return NotFoundResult("El PDF aún no fue generado.");

        return OkResult(new { receipt.ReceiptNumber, receipt.PdfPath, receipt.GeneratedAt });
    }

    /// <summary>US-PAG-06 · Reporte de cobranza por operador.</summary>
    [HttpGet("report/by-operator")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetReportByOperator(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? operatorId)
    {
        var result = await _creditSvc.GetCollectionByOperatorAsync(from, to, operatorId);
        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkResult(result.Value);
    }

    /// <summary>US-PAG-06 · Exportar cobranza por operador como CSV.</summary>
    [HttpGet("report/export-csv")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ExportReportCsv(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? operatorId)
    {
        var reportResult = await _creditSvc.GetCollectionByOperatorAsync(from, to, operatorId);
        if (!reportResult.IsSuccess) return BadRequestResult(reportResult.ErrorMessage);
        var report = reportResult.Value!;

        // BUG FIX: CsvEscape previene CSV Injection — campos sin escapar permiten
        // fórmulas maliciosas (=CMD...) y rompen el formato con comas/comillas.
        static string CsvEscape(string? val)
        {
            if (string.IsNullOrEmpty(val)) return "\"\"";
            if (val[0] is '=' or '+' or '-' or '@') val = "'" + val;
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Código,Cliente,Monto,Método,Banco,Fecha Pago,Operador,Recibo,Origen");

        foreach (var p in report.Payments)
        {
            sb.AppendLine(string.Join(",",
                CsvEscape(p.TbnCode),
                CsvEscape(p.ClientName),
                p.Amount.ToString("F2"),
                CsvEscape(p.Method),
                CsvEscape(p.Bank),
                p.PaidAt.ToString("dd/MM/yyyy HH:mm"),
                CsvEscape(p.OperatorName),
                CsvEscape(p.PhysicalReceiptNumber),
                p.FromWhatsApp ? "WhatsApp" : "Manual"));
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"cobros-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }
}