using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Dashboard;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Dashboard;

[Route("api/dashboard")]
// BUG FIX: usar Policy centralizada en lugar de Roles hardcodeados
[Authorize(Policy = "AllRoles")]
public class DashboardController : BaseController
{
    private readonly IDashboardService _svc;
    public DashboardController(IDashboardService svc) => _svc = svc;

    [HttpGet("kpis")]
    public async Task<IActionResult> GetKpis() => OkResult(await _svc.GetKpisAsync());

    [HttpGet("tendencia-cobros")]
    public async Task<IActionResult> GetTendencia([FromQuery] int meses = 6)
    {
        if (meses < 1 || meses > 24) return BadRequestResult("meses debe ser entre 1 y 24.");
        return OkResult(await _svc.GetTendenciaCobrosAsync(meses));
    }

    [HttpGet("metodos-pago")]
    public async Task<IActionResult> GetMetodosPago() => OkResult(await _svc.GetMetodosPagoAsync());

    [HttpGet("tickets-estado")]
    public async Task<IActionResult> GetTicketsEstado() => OkResult(await _svc.GetTicketsEstadoAsync());

    [HttpGet("tickets-urgentes")]
    public async Task<IActionResult> GetTicketsUrgentes([FromQuery] int top = 8) => OkResult(await _svc.GetTicketsUrgentesAsync(top));

    [HttpGet("tickets-por-tipo")]
    public async Task<IActionResult> GetTicketsPorTipo() => OkResult(await _svc.GetTicketsPorTipoAsync());

    [HttpGet("resolucion-promedio")]
    public async Task<IActionResult> GetResolucionPromedio() => OkResult(await _svc.GetResolucionPromedioAsync());

    [HttpGet("whatsapp-actividad")]
    public async Task<IActionResult> GetWhatsAppActividad([FromQuery] int top = 10) => OkResult(await _svc.GetWhatsAppActividadAsync(top));

    [HttpGet("deudores")]
    public async Task<IActionResult> GetDeudores() => OkResult(await _svc.GetDeudoresAsync());

    [HttpGet("comprobantes-recientes")]
    public async Task<IActionResult> GetComprobantes() => OkResult(await _svc.GetComprobantesRecientesAsync());

    [HttpGet("clientes-por-zona")]
    public async Task<IActionResult> GetClientesPorZona() => OkResult(await _svc.GetClientesPorZonaAsync());

    [HttpGet("actividad-horas")]
    public async Task<IActionResult> GetActividadHoras() => OkResult(await _svc.GetActividadHorasAsync());

    [HttpGet("preferences/{userId:guid}")]
    public async Task<IActionResult> GetPreferences(Guid userId)
    {
        if (userId != CurrentUserId) return Forbid();
        return OkResult(await _svc.GetPreferencesAsync(userId));
    }

    [HttpPut("preferences/{userId:guid}")]
    public async Task<IActionResult> SavePreferences(Guid userId, [FromBody] DashboardPreferencesDto dto)
    {
        if (userId != CurrentUserId) return Forbid();
        var any = dto.ShowKpis || dto.ShowTendencia || dto.ShowTickets || dto.ShowWhatsApp ||
                  dto.ShowDeudores || dto.ShowZonas || dto.ShowMetodosPago || dto.ShowComprobantes;
        if (!any) return BadRequestResult("Debe mantener al menos una sección visible.");
        await _svc.SavePreferencesAsync(userId, dto);
        return OkMessage("Preferencias guardadas.");
    }

    // ── M4 endpoints ──────────────────────────────────────────────────────────

    /// <summary>US-DASH-PAGOS · Sección cobros enriquecida con desglose por método y operador.</summary>
    [HttpGet("pagos")]
    public async Task<IActionResult> GetDashPagos()
        => OkResult(await _svc.GetDashPagosAsync());

    /// <summary>US-DASH-TICKETS-M · Métricas de tickets: SLA, resolución, vencidos.</summary>
    [HttpGet("tickets-metricas")]
    public async Task<IActionResult> GetTicketsMetricas()
        => OkResult(await _svc.GetDashTicketsMetricasAsync());

    /// <summary>US-DASH-NOTIF · Estado de notificaciones en las últimas 24h.</summary>
    [HttpGet("notif-stats")]
    public async Task<IActionResult> GetNotifStats()
        => OkResult(await _svc.GetDashNotifAsync());

    /// <summary>US-DASH-CHATBOT · Métricas del chatbot: conversaciones, intenciones, tasa de resolución.</summary>
    [HttpGet("chatbot-kpis")]
    public async Task<IActionResult> GetChatbotKpis()
        => OkResult(await _svc.GetDashChatbotAsync());

    /// <summary>US-DASH-AUTO · Acciones automáticas del día (suspensiones, facturas, recordatorios).</summary>
    [HttpGet("auto-actions")]
    public async Task<IActionResult> GetAutoActions()
        => OkResult(await _svc.GetDashAutoActionsAsync());

}