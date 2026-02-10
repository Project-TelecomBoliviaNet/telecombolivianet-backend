using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Dashboard.Queries;
using TelecomBoliviaNet.Application.DTOs.Dashboard;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Infrastructure.Data;
using static TelecomBoliviaNet.Infrastructure.Services.Dashboard.DashboardTimeHelper;

namespace TelecomBoliviaNet.Infrastructure.Services.Dashboard;

// ── GetDashPagosHandler ───────────────────────────────────────────────────

public class GetDashPagosHandler : IRequestHandler<GetDashPagosQuery, DashPagosDto>
{
    private readonly AppDbContext _db;
    private readonly IMediator    _mediator;
    public GetDashPagosHandler(AppDbContext db, IMediator mediator) { _db = db; _mediator = mediator; }

    public async Task<DashPagosDto> Handle(GetDashPagosQuery _, CancellationToken ct)
    {
        var now    = DateTime.UtcNow;
        var hoy    = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var mesUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var pagosMes = await _db.Payments.Include(p => p.RegisteredBy)
            .Where(p => !p.IsVoided && p.PaidAt >= mesUtc).ToListAsync(ct);
        var pagosHoy = pagosMes.Where(p => p.PaidAt >= hoy).ToList();
        var totalMes = pagosMes.Sum(p => p.Amount);

        var porMetodo = pagosMes.GroupBy(p => p.Method.ToString())
            .Select(g => new DashPagosPorMetodoDto(g.Key, g.Count(), g.Sum(p => p.Amount),
                totalMes > 0 ? Math.Round(g.Sum(p => p.Amount) / totalMes * 100, 1) : 0))
            .OrderByDescending(x => x.Monto).ToList();

        var porOperador = pagosMes.GroupBy(p => p.RegisteredBy?.FullName ?? "Sistema")
            .Select(g => new DashPagosPorOperadorDto(g.Key, g.Count(), g.Sum(p => p.Amount)))
            .OrderByDescending(x => x.Monto).ToList();

        var tend = await _mediator.Send(new GetTendenciaCobrosQuery(6), ct);
        return new DashPagosDto(pagosHoy.Sum(p => p.Amount), totalMes, pagosHoy.Count, pagosMes.Count, porMetodo, porOperador, tend.Meses);
    }
}

// ── GetDashTicketsMetricasHandler ─────────────────────────────────────────

public class GetDashTicketsMetricasHandler : IRequestHandler<GetDashTicketsMetricasQuery, DashTicketsMetricasDto>
{
    private readonly AppDbContext _db;
    public GetDashTicketsMetricasHandler(AppDbContext db) => _db = db;

    public async Task<DashTicketsMetricasDto> Handle(GetDashTicketsMetricasQuery _, CancellationToken ct)
    {
        var now    = DateTime.UtcNow;
        var mesUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var tickets = await _db.SupportTickets.Include(t => t.AssignedTo).ToListAsync(ct);
        var abiertos  = tickets.Count(t => t.Status == TicketStatus.Abierto);
        var enProceso = tickets.Count(t => t.Status == TicketStatus.EnProceso || t.Status == TicketStatus.PendienteCliente);
        var resMes    = tickets.Count(t => t.Status == TicketStatus.Resuelto && t.ResolvedAt >= mesUtc);

        var resueltos    = tickets.Where(t => t.ResolvedAt != null && t.ResolvedAt >= mesUtc).ToList();
        var dentroSla    = resueltos.Count(t => t.ResolvedAt <= t.SlaDeadline);
        var slaComp      = resueltos.Count > 0 ? Math.Round((double)dentroSla / resueltos.Count * 100, 1) : 100;
        var resProm      = resueltos.Any() ? resueltos.Average(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalHours) : 0;

        var vencidosSla = tickets
            .Where(t => (t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso) && t.SlaDeadline < now)
            .OrderBy(t => t.SlaDeadline).Take(5)
            .Select(t => new TicketDashItemDto(
                t.Id, t.Client?.FullName ?? "—", t.TicketNumber ?? t.Subject ?? t.Id.ToString()[..8],
                t.Type.ToString(), t.Priority.ToString(), t.Status.ToString(),
                t.AssignedTo?.FullName ?? "Sin asignar", t.CreatedAt, t.SlaDeadline,
                t.SlaDeadline.HasValue && t.SlaDeadline.Value < now))
            .ToList();

        var porTipo = tickets.GroupBy(t => t.Type.ToString())
            .Select(g => new DashTicketPorTipoMetricaDto(
                g.Key, g.Count(),
                g.Count(t => t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso || t.Status == TicketStatus.PendienteCliente),
                g.Count(t => t.Status == TicketStatus.Resuelto || t.Status == TicketStatus.Cerrado),
                g.Where(t => t.ResolvedAt != null).Select(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalHours).DefaultIfEmpty(0).Average()))
            .OrderByDescending(x => x.Total).ToList();

        return new DashTicketsMetricasDto(abiertos, enProceso, resMes, vencidosSla.Count, slaComp, Math.Round(resProm, 1), porTipo, vencidosSla);
    }
}

// ── GetDashNotifHandler ───────────────────────────────────────────────────

public class GetDashNotifHandler : IRequestHandler<GetDashNotifQuery, DashNotifDto>
{
    private readonly AppDbContext _db;
    public GetDashNotifHandler(AppDbContext db) => _db = db;

    public async Task<DashNotifDto> Handle(GetDashNotifQuery _, CancellationToken ct)
    {
        var hace24h    = DateTime.UtcNow.AddHours(-24);
        var logs       = await _db.NotifLogs.Where(l => l.RegistradoAt >= hace24h).ToListAsync(ct);
        var pendientes = await _db.NotifOutbox.CountAsync(o => o.EstadoFinal == null && !o.Publicado, ct);

        var omitidos  = logs.Count(l => l.Estado == NotifLogEstado.OMITIDO);
        var enviadas  = logs.Count(l => l.Estado == NotifLogEstado.ENVIADO);
        var fallidas  = logs.Count(l => l.Estado == NotifLogEstado.FALLIDO);
        var total     = enviadas + fallidas;
        var tasaExito = total > 0 ? Math.Round((double)enviadas / total * 100, 1) : 100;

        var porTipo = logs.GroupBy(l => l.Tipo.ToString())
            .Select(g => new DashNotifPorTipoDto(
                g.Key,
                g.Count(l => l.Estado == NotifLogEstado.ENVIADO),
                g.Count(l => l.Estado == NotifLogEstado.FALLIDO),
                0))
            .OrderByDescending(x => x.Enviadas + x.Fallidas).ToList();

        return new DashNotifDto(enviadas, fallidas, pendientes, omitidos, tasaExito, porTipo);
    }
}

// ── GetDashChatbotHandler ─────────────────────────────────────────────────

public class GetDashChatbotHandler : IRequestHandler<GetDashChatbotQuery, DashChatbotDto>
{
    private readonly IBotProxyService            _botProxy;
    private readonly ILogger<GetDashChatbotHandler> _logger;
    public GetDashChatbotHandler(IBotProxyService botProxy, ILogger<GetDashChatbotHandler> logger) { _botProxy = botProxy; _logger = logger; }

    public async Task<DashChatbotDto> Handle(GetDashChatbotQuery _, CancellationToken ct)
    {
        try
        {
            var stats    = await _botProxy.GetStatsAsync();
            var total    = stats.TotalConversaciones;
            var escaladas = stats.Escaladas;
            return new DashChatbotDto(
                0, stats.HoyConversaciones, stats.TotalConversaciones,
                total > 0 ? Math.Round((double)(total - escaladas) / total * 100, 1) : 100,
                total > 0 ? Math.Round((double)escaladas / total * 100, 1) : 0,
                new List<DashChatbotIntencionDto>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chatbot no disponible — retornando métricas vacías");
            return new DashChatbotDto(0, 0, 0, 0, 0, new List<DashChatbotIntencionDto>(), IsAvailable: false);
        }
    }
}

// ── GetDashAutoActionsHandler ─────────────────────────────────────────────

public class GetDashAutoActionsHandler : IRequestHandler<GetDashAutoActionsQuery, DashAutoActionsDto>
{
    private readonly AppDbContext _db;
    public GetDashAutoActionsHandler(AppDbContext db) => _db = db;

    public async Task<DashAutoActionsDto> Handle(GetDashAutoActionsQuery _, CancellationToken ct)
    {
        var hoy = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0, DateTimeKind.Utc);
        var counts = await _db.AuditLogs
            .Where(a => a.CreatedAt >= hoy && a.UserName == "Sistema")
            .GroupBy(a => a.Action).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        var recientes = await _db.AuditLogs
            .Where(a => a.CreatedAt >= hoy && a.UserName == "Sistema")
            .OrderByDescending(a => a.CreatedAt).Take(10)
            .Select(a => new DashAutoActionItemDto(a.Action, a.Description, a.CreatedAt, ModuloOf(a.Action)))
            .ToListAsync(ct);

        return new DashAutoActionsDto(
            counts.GetValueOrDefault("CLIENT_SUSPENDED"), counts.GetValueOrDefault("CLIENT_REACTIVATED"),
            counts.GetValueOrDefault("BILLING_JOB_EXECUTED"), counts.GetValueOrDefault("REMINDER_SENT"),
            counts.GetValueOrDefault("OVERDUE_JOB_EXECUTED"), recientes);
    }

    private static string ModuloOf(string action) => action switch
    {
        var a when a.StartsWith("CLIENT_")   => "Clientes",
        var a when a.StartsWith("BILLING_")  => "Facturación",
        var a when a.StartsWith("NOTIF_")    => "Notificaciones",
        var a when a.StartsWith("REMINDER_") => "Notificaciones",
        var a when a.StartsWith("OVERDUE_")  => "Facturación",
        _ => "Sistema"
    };
}

// ── F5: GetAgingCarteraHandler ────────────────────────────────────────────

public class GetAgingCarteraHandler : IRequestHandler<GetAgingCarteraQuery, AgingCarteraDto>
{
    private readonly AppDbContext _db;
    public GetAgingCarteraHandler(AppDbContext db) => _db = db;

    public async Task<AgingCarteraDto> Handle(GetAgingCarteraQuery _, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Traer solo las facturas vencidas/pendientes con DueDate ya pasado (conjunto pequeño)
        var vencidas = await _db.Invoices
            .Where(i => !i.IsDeleted
                && (i.Status == InvoiceStatus.Vencida || i.Status == InvoiceStatus.Pendiente)
                && i.DueDate < now
                && i.ClientId != default)
            .Select(i => new { i.ClientId, i.Amount, i.DueDate })
            .ToListAsync(ct);

        // Calcular días en memoria (evita función SQL específica de proveedor)
        var items = vencidas.Select(i => new
        {
            i.ClientId,
            i.Amount,
            Dias = (int)(now - i.DueDate).TotalDays
        }).ToList();

        static AgingBucketDto Bucket(string rango, int min, int max,
            IEnumerable<(Guid ClientId, decimal Amount, int Dias)> all)
        {
            var grupo = all.Where(x => x.Dias >= min && (max == int.MaxValue || x.Dias <= max)).ToList();
            return new AgingBucketDto(
                rango,
                grupo.Count,
                grupo.Sum(x => x.Amount),
                grupo.Select(x => x.ClientId).Distinct().Count());
        }

        var typed = items.Select(x => (x.ClientId, x.Amount, x.Dias)).ToList();
        var buckets = new List<AgingBucketDto>
        {
            Bucket("0-30",  0,  30,          typed),
            Bucket("31-60", 31, 60,          typed),
            Bucket("61-90", 61, 90,          typed),
            Bucket("+90",   91, int.MaxValue, typed),
        };

        return new AgingCarteraDto(
            vencidas.Sum(i => i.Amount),
            vencidas.Count,
            buckets);
    }
}
