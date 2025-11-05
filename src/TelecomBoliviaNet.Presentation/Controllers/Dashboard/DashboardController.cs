using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.Dashboard.Queries;
using TelecomBoliviaNet.Application.DTOs.Dashboard;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Dashboard;

[Route("api/dashboard")]
[Authorize(Policy = "AllRoles")]
public class DashboardController : BaseController
{
    private readonly IMediator _mediator;
    public DashboardController(IMediator mediator) => _mediator = mediator;

    [HttpGet("kpis")]
    public async Task<IActionResult> GetKpis()
        => OkResult(await _mediator.Send(new GetDashKpisQuery()));

    [HttpGet("tendencia-cobros")]
    public async Task<IActionResult> GetTendencia([FromQuery] int meses = 6)
    {
        if (meses < 1 || meses > 24) return BadRequestResult("meses debe ser entre 1 y 24.");
        return OkResult(await _mediator.Send(new GetTendenciaCobrosQuery(meses)));
    }

    [HttpGet("metodos-pago")]
    public async Task<IActionResult> GetMetodosPago()
        => OkResult(await _mediator.Send(new GetMetodosPagoQuery()));

    [HttpGet("tickets-estado")]
    public async Task<IActionResult> GetTicketsEstado()
        => OkResult(await _mediator.Send(new GetTicketsEstadoQuery()));

    [HttpGet("tickets-urgentes")]
    public async Task<IActionResult> GetTicketsUrgentes([FromQuery] int top = 8)
        => OkResult(await _mediator.Send(new GetTicketsUrgentesQuery(top)));

    [HttpGet("tickets-por-tipo")]
    public async Task<IActionResult> GetTicketsPorTipo()
        => OkResult(await _mediator.Send(new GetTicketsPorTipoQuery()));

    [HttpGet("resolucion-promedio")]
    public async Task<IActionResult> GetResolucionPromedio()
        => OkResult(await _mediator.Send(new GetResolucionPromedioQuery()));

    [HttpGet("whatsapp-actividad")]
    public async Task<IActionResult> GetWhatsAppActividad([FromQuery] int top = 10)
        => OkResult(await _mediator.Send(new GetWhatsAppActividadQuery(top)));

    [HttpGet("deudores")]
    public async Task<IActionResult> GetDeudores()
        => OkResult(await _mediator.Send(new GetDeudoresQuery()));

    [HttpGet("comprobantes-recientes")]
    public async Task<IActionResult> GetComprobantes()
        => OkResult(await _mediator.Send(new GetComprobantesRecientesQuery()));

    [HttpGet("clientes-por-zona")]
    public async Task<IActionResult> GetClientesPorZona()
        => OkResult(await _mediator.Send(new GetClientesPorZonaQuery()));

    [HttpGet("actividad-horas")]
    public async Task<IActionResult> GetActividadHoras()
        => OkResult(await _mediator.Send(new GetActividadHorasQuery()));

    [HttpGet("preferences/{userId:guid}")]
    public async Task<IActionResult> GetPreferences(Guid userId)
    {
        if (userId != CurrentUserId) return Forbid();
        return OkResult(await _mediator.Send(new GetDashPreferencesQuery(userId)));
    }

    [HttpPut("preferences/{userId:guid}")]
    public async Task<IActionResult> SavePreferences(Guid userId, [FromBody] DashboardPreferencesDto dto)
    {
        if (userId != CurrentUserId) return Forbid();
        var any = dto.ShowKpis || dto.ShowTendencia || dto.ShowTickets || dto.ShowWhatsApp ||
                  dto.ShowDeudores || dto.ShowZonas || dto.ShowMetodosPago || dto.ShowComprobantes;
        if (!any) return BadRequestResult("Debe mantener al menos una sección visible.");
        await _mediator.Send(new SaveDashPreferencesCommand(userId, dto));
        return OkMessage("Preferencias guardadas.");
    }

    [HttpGet("pagos")]
    public async Task<IActionResult> GetDashPagos()
        => OkResult(await _mediator.Send(new GetDashPagosQuery()));

    [HttpGet("tickets-metricas")]
    public async Task<IActionResult> GetTicketsMetricas()
        => OkResult(await _mediator.Send(new GetDashTicketsMetricasQuery()));

    [HttpGet("notif-stats")]
    public async Task<IActionResult> GetNotifStats()
        => OkResult(await _mediator.Send(new GetDashNotifQuery()));

    [HttpGet("chatbot-kpis")]
    public async Task<IActionResult> GetChatbotKpis()
        => OkResult(await _mediator.Send(new GetDashChatbotQuery()));

    [HttpGet("auto-actions")]
    public async Task<IActionResult> GetAutoActions()
        => OkResult(await _mediator.Send(new GetDashAutoActionsQuery()));

    // ── F5, F6, F7 — mejoras dashboard ───────────────────────────────────

    [HttpGet("aging-cartera")]
    public async Task<IActionResult> GetAgingCartera()
        => OkResult(await _mediator.Send(new GetAgingCarteraQuery()));

    [HttpGet("proximos-corte")]
    public async Task<IActionResult> GetProximosCorte([FromQuery] int dias = 7)
    {
        if (dias < 1 || dias > 30) return BadRequestResult("dias debe ser entre 1 y 30.");
        return OkResult(await _mediator.Send(new GetProximosCortQuery(dias)));
    }

    [HttpGet("workload-tecnicos")]
    public async Task<IActionResult> GetWorkloadTecnicos()
        => OkResult(await _mediator.Send(new GetWorkloadTecnicosQuery()));
}
