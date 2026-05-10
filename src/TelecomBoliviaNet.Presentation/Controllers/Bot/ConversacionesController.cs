using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Bot;
using TelecomBoliviaNet.Application.Services.Bot;
using TelecomBoliviaNet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Domain.Entities.Admin;

namespace TelecomBoliviaNet.Presentation.Controllers.Bot;

/// <summary>
/// M10 — Bandeja unificada de WhatsApp y config del bot.
/// US-BOT-01: bandeja unificada para operadores.
/// US-BOT-06/02: configuración del bot desde UI.
/// US-BOT-07: historial de conversaciones por cliente.
/// </summary>
[Route("api/conversaciones")]
[Authorize(Policy = "AdminOrOperadorOrTecnico")]
public class ConversacionesController : BaseController
{
    private readonly BotProxyService  _proxy;
    private readonly BotConfigService _config;
    private readonly AppDbContext     _db;
    private readonly IWebHostEnvironment _env;

    public ConversacionesController(
        BotProxyService proxy, BotConfigService config,
        AppDbContext db, IWebHostEnvironment env)
    {
        _proxy  = proxy;
        _config = config;
        _db     = db;
        _env    = env;
    }

    // ── US-BOT-01 · Bandeja unificada ─────────────────────────────────────────

    /// <summary>Lista paginada de conversaciones.</summary>
    [HttpGet]
    public async Task<IActionResult> GetConversaciones(
        [FromQuery] int    page          = 1,
        [FromQuery] int    limit         = 20,
        [FromQuery] bool   soloEscaladas = false,
        [FromQuery] string tab           = "todas")
    {
        var (items, total) = await _proxy.GetConversationsAsync(
            page, limit, soloEscaladas ? true : null, tab);
        return OkResult(new { Items = items, Total = total, Page = page, Limit = limit });
    }

    /// <summary>Detalle de una conversación por número de teléfono.</summary>
    [HttpGet("{phone}")]
    public async Task<IActionResult> GetConversacion(string phone)
    {
        var detail = await _proxy.GetConversationByPhoneAsync(
            Uri.UnescapeDataString(phone), 100);
        return detail is null
            ? NotFoundResult("Conversación no encontrada.")
            : OkResult(detail);
    }

    /// <summary>Stats de conversaciones del día.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
        => OkResult(await _proxy.GetStatsAsync());

    // ── US-BOT-07 · Historial por cliente ─────────────────────────────────────

    /// <summary>Historial de conversaciones por teléfono del cliente.</summary>
    [HttpGet("cliente/{phone}")]
    public async Task<IActionResult> GetClientHistory(string phone)
        => OkResult(await _proxy.GetClientHistoryAsync(
            Uri.UnescapeDataString(phone)));

    // ── Intervención de agente: tomar / devolver / responder ─────────────────

    /// <summary>
    /// POST /api/conversaciones/{phone}/tomar
    /// El agente autenticado toma control de la conversación:
    /// silencia el bot y habilita respuestas manuales.
    /// </summary>
    [HttpPost("{phone}/tomar")]
    public async Task<IActionResult> TakeoverConversacion(string phone)
    {
        var ok = await _proxy.TakeoverConversationAsync(
            Uri.UnescapeDataString(phone), CurrentUserName);
        return ok
            ? OkMessage("Conversación tomada correctamente.")
            : StatusCode(502, new { error = "Error al tomar la conversación.", code = "TAKEOVER_ERROR" });
    }

    /// <summary>
    /// POST /api/conversaciones/{phone}/devolver
    /// El agente devuelve la conversación al bot.
    /// </summary>
    [HttpPost("{phone}/devolver")]
    public async Task<IActionResult> ReleaseConversacion(string phone)
    {
        var ok = await _proxy.ReleaseConversationAsync(Uri.UnescapeDataString(phone));
        return ok
            ? OkMessage("Conversación devuelta al bot.")
            : StatusCode(502, new { error = "Error al devolver la conversación.", code = "RELEASE_ERROR" });
    }

    /// <summary>
    /// POST /api/conversaciones/{phone}/responder
    /// Envía un mensaje del agente al usuario por WhatsApp.
    /// Solo disponible cuando la conversación está tomada por un agente.
    /// </summary>
    [HttpPost("{phone}/responder")]
    public async Task<IActionResult> ResponderConversacion(
        string phone, [FromBody] SendMessageRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Texto))
            return BadRequestResult("El texto del mensaje es requerido.");

        var ok = await _proxy.SendAdminMessageAsync(
            Uri.UnescapeDataString(phone), dto.Texto);
        return ok
            ? OkMessage("Mensaje enviado correctamente.")
            : StatusCode(502, new { error = "Error al enviar el mensaje.", code = "SEND_ERROR" });
    }

    // ── US-BOT-06 / US-BOT-02 · Configuración del bot ────────────────────────

    /// <summary>Obtener configuración actual del bot.</summary>
    [HttpGet("~/api/bot-config")]
    public async Task<IActionResult> GetBotConfig()
        => OkResult(await _config.GetAsync());

    /// <summary>Actualizar configuración del bot (solo Admin).</summary>
    [HttpPut("~/api/bot-config")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateBotConfig([FromBody] UpdateBotConfigDto dto)
    {
        var result = await _config.UpdateAsync(
            dto.Config, CurrentUserId, CurrentUserName, ClientIp);
        return OkResult(result);
    }

    /// <summary>Restaurar configuración por defecto del bot.</summary>
    [HttpPost("~/api/bot-config/reset")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ResetBotConfig()
    {
        var result = await _config.UpdateAsync(
            _config.GetDefault(), CurrentUserId, CurrentUserName, ClientIp);
        return OkResult(result);
    }

    /// <summary>GET público para que el chatbot NestJS consuma la config.</summary>
    [HttpGet("~/api/bot-config/public")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetBotConfigPublic()
        => OkResult(await _config.GetAsync());

    // ── QR global de la empresa ───────────────────────────────────────────────

    private const string CompanyQrConfigKey = "Bot:CompanyQrPath";
    private static readonly string[] AllowedQrTypes = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>
    /// POST /api/bot-config/company-qr
    /// Sube el QR global de pago de la empresa. Reemplaza el anterior.
    /// ExpiresAt es obligatorio — fecha en que vence el QR (formato ISO 8601 o yyyy-MM-dd).
    /// </summary>
    [HttpPost("~/api/bot-config/company-qr")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UploadCompanyQr(IFormFile file, [FromForm] string expiresAt)
    {
        if (file is null || file.Length == 0)
            return BadRequestResult("Archivo requerido.");
        if (!AllowedQrTypes.Contains(file.ContentType.ToLower()))
            return BadRequestResult("Formato no soportado. Use JPG, PNG o WebP.");
        if (file.Length > 5 * 1024 * 1024)
            return BadRequestResult("El archivo no puede superar 5 MB.");
        if (string.IsNullOrWhiteSpace(expiresAt))
            return BadRequestResult("La fecha de vencimiento del QR es obligatoria.");
        if (!DateOnly.TryParse(expiresAt, out var expiryDate) || expiryDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequestResult("La fecha de vencimiento debe ser una fecha futura válida (yyyy-MM-dd).");

        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads", "company-qr");
        Directory.CreateDirectory(uploadsDir);

        var ext      = Path.GetExtension(file.FileName).ToLower();
        var fileName = $"company-qr{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        var relativePath    = $"/uploads/company-qr/{fileName}";
        var expiresAtIso    = expiryDate.ToString("yyyy-MM-dd");

        // Guardar ruta del archivo
        await UpsertSystemConfig(CompanyQrConfigKey, relativePath);
        // Guardar fecha de vencimiento
        await UpsertSystemConfig(SystemConfigKeys.CompanyQrExpiresAt, expiresAtIso);
        // Reiniciar anti-duplicado para que el job envíe alerta en el próximo ciclo si ya vence pronto
        await UpsertSystemConfig(SystemConfigKeys.CompanyQrAlertSentDate, string.Empty);

        await _db.SaveChangesAsync();

        return OkResult(new { path = relativePath, expiresAt = expiresAtIso });
    }

    private async Task UpsertSystemConfig(string key, string value)
    {
        var cfg = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
        if (cfg is null)
            _db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        else
        {
            cfg.Value     = value;
            cfg.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// GET /api/bot-config/company-qr
    /// Devuelve la imagen binaria del QR global. Lo consume el chatbot NestJS.
    /// </summary>
    [HttpGet("~/api/bot-config/company-qr")]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetCompanyQr()
    {
        var cfg = await _db.SystemConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == CompanyQrConfigKey);

        if (cfg is null || string.IsNullOrEmpty(cfg.Value))
            return NotFoundResult("No hay QR global configurado.");

        var filePath = Path.Combine(_env.ContentRootPath, cfg.Value.TrimStart('/'));
        if (!System.IO.File.Exists(filePath))
            return NotFoundResult("Archivo QR no encontrado.");

        var ext         = Path.GetExtension(filePath).ToLower();
        var contentType = ext == ".png" ? "image/png" : ext == ".webp" ? "image/webp" : "image/jpeg";
        var bytes       = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, contentType);
    }

    /// <summary>
    /// GET /api/bot-config/company-qr/info
    /// Devuelve metadatos del QR global: path, fecha de actualización, vencimiento y días restantes.
    /// </summary>
    [HttpGet("~/api/bot-config/company-qr/info")]
    [Authorize(Policy = "AdminOrOperador")]
    public async Task<IActionResult> GetCompanyQrInfo()
    {
        var configs = await _db.SystemConfigs
            .AsNoTracking()
            .Where(c => c.Key == CompanyQrConfigKey || c.Key == SystemConfigKeys.CompanyQrExpiresAt)
            .ToListAsync();

        var pathCfg   = configs.FirstOrDefault(c => c.Key == CompanyQrConfigKey);
        var expiryCfg = configs.FirstOrDefault(c => c.Key == SystemConfigKeys.CompanyQrExpiresAt);

        var hasQr = pathCfg is not null && !string.IsNullOrEmpty(pathCfg.Value)
                    && System.IO.File.Exists(
                        Path.Combine(_env.ContentRootPath, pathCfg.Value.TrimStart('/')));

        DateOnly? expiresAt = DateOnly.TryParse(expiryCfg?.Value, out var d) ? d : null;
        int? daysRemaining  = expiresAt.HasValue
            ? (int)(expiresAt.Value.ToDateTime(TimeOnly.MinValue) - DateTime.UtcNow.Date).TotalDays
            : null;

        return OkResult(new
        {
            HasQr        = hasQr,
            Path         = pathCfg?.Value,
            UpdatedAt    = pathCfg?.UpdatedAt,
            ExpiresAt    = expiresAt?.ToString("yyyy-MM-dd"),
            DaysRemaining = daysRemaining,
        });
    }

    // ── Proxy RAG → chatbot ───────────────────────────────────────────────────

    /// <summary>
    /// GET /api/rag/documents
    /// Lista los documentos RAG del chatbot.
    /// </summary>
    [HttpGet("~/api/rag/documents")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetRagDocuments()
        => OkResult(await _proxy.RagListDocumentsAsync());

    /// <summary>
    /// POST /api/rag/documents
    /// Sube un documento (PDF/TXT) al motor RAG del chatbot para vectorizarlo.
    /// </summary>
    [HttpPost("~/api/rag/documents")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UploadRagDocument(IFormFile file, [FromForm] string? title)
    {
        if (file is null || file.Length == 0)
            return BadRequestResult("Archivo requerido.");
        if (file.Length > 20 * 1024 * 1024)
            return BadRequestResult("El archivo no puede superar 20 MB.");

        var result = await _proxy.RagUploadDocumentAsync(file, title);
        return result is null
            ? StatusCode(502, new { error = "Error al subir documento al chatbot.", code = "RAG_UPLOAD_ERROR" })
            : StatusCode(201, new { success = true, data = result });
    }

    /// <summary>
    /// DELETE /api/rag/documents/{id}
    /// Elimina un documento RAG del chatbot.
    /// </summary>
    [HttpDelete("~/api/rag/documents/{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteRagDocument(string id)
    {
        var ok = await _proxy.RagDeleteDocumentAsync(id);
        return ok ? NoContent() : StatusCode(502, new { error = "Error al eliminar documento.", code = "RAG_DELETE_ERROR" });
    }
}
