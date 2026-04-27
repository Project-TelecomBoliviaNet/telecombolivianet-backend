using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.DTOs.Dashboard;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Dashboard;
using TelecomBoliviaNet.Domain.Entities.Payments;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext                _db;
    private readonly IBotProxyService            _botProxy;
    private readonly ILogger<DashboardService>   _logger;

    public DashboardService(AppDbContext db, IBotProxyService botProxy, ILogger<DashboardService> logger)
    {
        _db       = db;
        _botProxy = botProxy;
        _logger   = logger;
    }

    private static readonly TimeSpan _bo = TimeSpan.FromHours(-4);

    private static DateTime NowBo() =>
        DateTimeOffset.UtcNow.ToOffset(_bo).DateTime;

    private static DateTime StartOfDayUtc(DateTime bo) =>
        new DateTimeOffset(bo.Year, bo.Month, bo.Day, 0, 0, 0, _bo).UtcDateTime;

    private static DateTime StartOfMonthUtc(DateTime bo) =>
        new DateTimeOffset(bo.Year, bo.Month, 1, 0, 0, 0, _bo).UtcDateTime;

    private static string UtcToBoHHmm(DateTime utc) =>
        new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(_bo).ToString("HH:mm");

    private static int UtcHourToBo(int h) => (h - 4 + 24) % 24;

    private static string Cap(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    // ── KPIs ──────────────────────────────────────────────────────────────────
    public async Task<DashboardKpisDto> GetKpisAsync()
    {
        var nowBo     = NowBo();
        var mesUtc    = StartOfMonthUtc(nowBo);
        var mesAntUtc = StartOfMonthUtc(nowBo.AddMonths(-1));
        var diaUtc    = StartOfDayUtc(nowBo);
        var nowUtc    = DateTime.UtcNow;

        // 5 queries en lugar de 13 — EF Core traduce cada GroupBy(_ => 1) a
        // un único SELECT con COUNT(CASE WHEN ...) por columna.
        // Task.WhenAll NO se usa: DbContext no es thread-safe.

        // Query 1: clientes activos, suspendidos y nuevos este mes
        var cs = await _db.Clients
            .GroupBy(_ => 1)
            .Select(g => new {
                Activos     = g.Count(c => c.Status == ClientStatus.Activo),
                Suspendidos = g.Count(c => c.Status == ClientStatus.Suspendido),
                NuevosMes   = g.Count(c => c.CreatedAt >= mesUtc)
            })
            .FirstOrDefaultAsync();

        // Query 2: cobros mes actual y mes anterior
        var ps = await _db.Payments
            .Where(p => !p.IsVoided && p.PaidAt >= mesAntUtc)
            .GroupBy(_ => 1)
            .Select(g => new {
                TotalMes    = (decimal?)g.Where(p => p.PaidAt >= mesUtc).Sum(p => p.Amount),
                TotalMesAnt = (decimal?)g.Where(p => p.PaidAt < mesUtc).Sum(p => p.Amount)
            })
            .FirstOrDefaultAsync();

        // Query 3: clientes con deuda (subquery — no se puede agrupar con las anteriores)
        var r6 = await _db.Clients.CountAsync(c => c.Invoices.Any(
                     i => i.Status == InvoiceStatus.Vencida || i.Status == InvoiceStatus.Pendiente));

        // Query 4: monto total de deuda pendiente/vencida
        var r7 = await _db.Invoices
            .Where(i => i.Status == InvoiceStatus.Vencida || i.Status == InvoiceStatus.Pendiente)
            .SumAsync(i => (decimal?)i.Amount) ?? 0m;

        // Query 5: tickets abiertos, críticos, resueltos hoy y vencidos por SLA
        var ts = await _db.SupportTickets
            .GroupBy(_ => 1)
            .Select(g => new {
                Abiertos = g.Count(t => t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso),
                Criticos = g.Count(t => (t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso)
                                     && t.Priority == TicketPriority.Critica),
                ResHoy   = g.Count(t => t.ResolvedAt != null && t.ResolvedAt >= diaUtc),
                VencSla  = g.Count(t => (t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso)
                                     && t.DueDate != null && t.DueDate < nowUtc)
            })
            .FirstOrDefaultAsync();

        // Query 6 (combinada): comprobantes WhatsApp pendientes y recibidos hoy
        var ws = await _db.WhatsAppReceipts
            .GroupBy(_ => 1)
            .Select(g => new {
                Pendientes = g.Count(r => r.Status == ReceiptQueueStatus.Pendiente),
                Hoy        = g.Count(r => r.ReceivedAt >= diaUtc)
            })
            .FirstOrDefaultAsync();

        return new DashboardKpisDto(
            cs?.Activos     ?? 0,
            cs?.Suspendidos ?? 0,
            cs?.NuevosMes   ?? 0,
            ps?.TotalMes    ?? 0m,
            ps?.TotalMesAnt ?? 0m,
            r6,
            r7,
            ts?.Abiertos ?? 0,
            ts?.Criticos ?? 0,
            ts?.ResHoy   ?? 0,
            ts?.VencSla  ?? 0,
            ws?.Pendientes ?? 0,
            ws?.Hoy        ?? 0);
    }

    // ── Tendencia cobros ──────────────────────────────────────────────────────
    public async Task<TendenciaCobrosDto> GetTendenciaCobrosAsync(int meses = 6)
    {
        var nowBo   = NowBo();
        var culture = new System.Globalization.CultureInfo("es-BO");
        var all     = new List<TendenciaMesDto>();

        for (int i = meses - 1; i >= 0; i--)
        {
            var fecha = nowBo.AddMonths(-i);
            var ini   = StartOfMonthUtc(fecha);
            var fin   = StartOfMonthUtc(new DateTime(fecha.Year, fecha.Month, 1).AddMonths(1));
            var p     = await _db.Payments.Where(x => !x.IsVoided && x.PaidAt >= ini && x.PaidAt < fin)
                            .GroupBy(_ => 1).Select(g => new { T = g.Sum(x => x.Amount), C = g.Count() }).FirstOrDefaultAsync();
            all.Add(new TendenciaMesDto(Cap(fecha.ToString("MMM", culture)), Cap(fecha.ToString("MMMM yyyy", culture)), p?.T ?? 0m, p?.C ?? 0));
        }

        int first = all.FindIndex(m => m.Cantidad > 0);
        if (first == -1) return new TendenciaCobrosDto(new List<TendenciaMesDto>());
        return new TendenciaCobrosDto(first > 0 ? all.Skip(first).ToList() : all);
    }

    // ── Métodos de pago ───────────────────────────────────────────────────────
    public async Task<List<MetodoPagoDto>> GetMetodosPagoAsync()
    {
        var ini = StartOfMonthUtc(NowBo());
        return await _db.Payments.Where(p => !p.IsVoided && p.PaidAt >= ini)
            .GroupBy(p => p.Method)
            .Select(g => new MetodoPagoDto(g.Key.ToString(), g.Count(), g.Sum(p => p.Amount)))
            .ToListAsync();
    }

    // ── Tickets por estado ────────────────────────────────────────────────────
    public async Task<List<TicketEstadoDashDto>> GetTicketsEstadoAsync()
    {
        var raw = await _db.SupportTickets
            .GroupBy(t => t.Status)
            .Select(g => new { Estado = g.Key.ToString(), Total = g.Count(), Criticos = g.Count(t => t.Priority == TicketPriority.Critica) })
            .ToListAsync();
        return raw.Select(r => new TicketEstadoDashDto(r.Estado, r.Total, r.Criticos)).ToList();
    }

    // ── Tickets urgentes (Critica/Alta primero, SLA vencido primero) ──────────
    public async Task<List<TicketDashItemDto>> GetTicketsUrgentesAsync(int top = 8)
    {
        var now = DateTime.UtcNow;
        return await _db.SupportTickets
            .Where(t => t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso)
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Take(top)
            .Select(t => new TicketDashItemDto(
                t.Id,
                t.Client!.FullName,
                t.Subject,
                t.Type.ToString(),
                t.Priority.ToString(),
                t.Status.ToString(),
                t.AssignedTo != null ? t.AssignedTo.FullName : "Sin asignar",
                t.CreatedAt,
                t.DueDate,
                t.DueDate != null && t.DueDate < now))
            .ToListAsync();
    }

    // ── Tickets por tipo ──────────────────────────────────────────────────────
    public async Task<List<TicketPorTipoDto>> GetTicketsPorTipoAsync()
    {
        var raw = await _db.SupportTickets
            .GroupBy(t => t.Type)
            .Select(g => new { Tipo = g.Key.ToString(), Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToListAsync();
        return raw.Select(r => new TicketPorTipoDto(r.Tipo, r.Total)).ToList();
    }

    // ── Tiempo promedio resolución por prioridad ──────────────────────────────
    public async Task<List<ResolucionPromDto>> GetResolucionPromedioAsync()
    {
        var raw = await _db.SupportTickets
            .Where(t => t.ResolvedAt != null)
            .Select(t => new { Prio = t.Priority.ToString(), t.CreatedAt, t.ResolvedAt })
            .ToListAsync();

        return raw.GroupBy(t => t.Prio)
            .Select(g => new ResolucionPromDto(
                g.Key,
                g.Average(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalHours),
                g.Count()))
            .OrderBy(r => r.Prioridad)
            .ToList();
    }

    // ── WhatsApp actividad ────────────────────────────────────────────────────
    public async Task<List<WhatsAppActividadDto>> GetWhatsAppActividadAsync(int top = 10)
    {
        var ini = StartOfDayUtc(NowBo());
        var raw = await _db.WhatsAppReceipts
            .Where(r => r.ReceivedAt >= ini)
            .OrderByDescending(r => r.ReceivedAt)
            .Take(top)
            .Select(r => new { r.ReceivedAt, ClienteNombre = r.Client!.FullName, r.Status, r.DeclaredAmount })
            .ToListAsync();
        return raw.Select(r => new WhatsAppActividadDto(UtcToBoHHmm(r.ReceivedAt), r.ClienteNombre, r.Status.ToString(), r.DeclaredAmount)).ToList();
    }

    // ── Deudores ──────────────────────────────────────────────────────────────
    public async Task<List<DeudorItemDto>> GetDeudoresAsync()
    {
        var hoy = DateTime.UtcNow;
        var raw = await _db.Clients
            .Where(c => c.Invoices.Any(i => i.Status == InvoiceStatus.Vencida || i.Status == InvoiceStatus.Pendiente))
            .Select(c => new {
                c.Id, c.FullName, c.Zone,
                Monto    = c.Invoices.Where(i => i.Status == InvoiceStatus.Vencida || i.Status == InvoiceStatus.Pendiente).Sum(i => i.Amount),
                Vencidas = c.Invoices.Count(i => i.Status == InvoiceStatus.Vencida),
                Earliest = c.Invoices.Where(i => i.Status == InvoiceStatus.Vencida).OrderBy(i => i.DueDate).Select(i => (DateTime?)i.DueDate).FirstOrDefault()
            }).ToListAsync();

        return raw.Select(c => new DeudorItemDto(c.Id, c.FullName, c.Zone, c.Monto,
            c.Earliest.HasValue ? Math.Max(0, (int)(hoy - c.Earliest.Value).TotalDays) : 0, c.Vencidas))
            .OrderByDescending(d => d.DiasVencido).ToList();
    }

    // ── Comprobantes recientes ────────────────────────────────────────────────
    public async Task<List<ComprobanteRecienteDto>> GetComprobantesRecientesAsync(int top = 8)
    {
        return await _db.Payments.Where(p => !p.IsVoided)
            .OrderByDescending(p => p.RegisteredAt).Take(top)
            .Select(p => new ComprobanteRecienteDto(p.Id, p.Client!.FullName, p.Amount, p.PaidAt, "Verificado", p.Method.ToString()))
            .ToListAsync();
    }

    // ── Clientes por zona ─────────────────────────────────────────────────────
    public async Task<List<ClientesPorZonaDto>> GetClientesPorZonaAsync()
    {
        var raw = await _db.Clients.GroupBy(c => c.Zone)
            .Select(g => new {
                Zona     = g.Key,
                Total    = g.Count(),
                Activos  = g.Count(c => c.Status == ClientStatus.Activo),
                ConDeuda = g.Count(c => c.Invoices.Any(i => i.Status == InvoiceStatus.Vencida || i.Status == InvoiceStatus.Pendiente))
            }).OrderByDescending(g => g.Total).Take(8).ToListAsync();
        return raw.Select(r => new ClientesPorZonaDto(r.Zona, r.Total, r.Activos, r.ConDeuda)).ToList();
    }

    // ── Actividad por hora ────────────────────────────────────────────────────
    public async Task<List<ActividadHoraDto>> GetActividadHorasAsync()
    {
        var nowBo = NowBo();
        var ini   = StartOfDayUtc(nowBo);
        var fin   = ini.AddDays(1);

        var pH = await _db.Payments.Where(p => p.PaidAt >= ini && p.PaidAt < fin).Select(p => p.PaidAt.Hour).ToListAsync();
        var tH = await _db.SupportTickets.Where(t => t.CreatedAt >= ini && t.CreatedAt < fin).Select(t => t.CreatedAt.Hour).ToListAsync();
        var wH = await _db.WhatsAppReceipts.Where(r => r.ReceivedAt >= ini && r.ReceivedAt < fin).Select(r => r.ReceivedAt.Hour).ToListAsync();

        var pBo = pH.Select(UtcHourToBo).ToList();
        var tBo = tH.Select(UtcHourToBo).ToList();
        var wBo = wH.Select(UtcHourToBo).ToList();

        return Enumerable.Range(0, 24).Select(h => new ActividadHoraDto(h, pBo.Count(x => x == h), tBo.Count(x => x == h), wBo.Count(x => x == h))).ToList();
    }

    // ── Preferencias ──────────────────────────────────────────────────────────
    public async Task<DashboardPreferencesDto> GetPreferencesAsync(Guid userId)
    {
        var p = await _db.DashboardPreferences.FirstOrDefaultAsync(x => x.UserId == userId);
        if (p is null) return new DashboardPreferencesDto(true, true, true, true, true, true, true, true);
        return new DashboardPreferencesDto(p.ShowKpis, p.ShowTendencia, p.ShowTickets, p.ShowWhatsApp, p.ShowDeudores, p.ShowZonas, p.ShowMetodosPago, p.ShowComprobantes);
    }

    public async Task SavePreferencesAsync(Guid userId, DashboardPreferencesDto dto)
    {
        var p = await _db.DashboardPreferences.FirstOrDefaultAsync(x => x.UserId == userId);
        if (p is null) { p = new DashboardPreference { Id = Guid.NewGuid(), UserId = userId }; _db.DashboardPreferences.Add(p); }
        p.ShowKpis = dto.ShowKpis; p.ShowTendencia = dto.ShowTendencia;
        p.ShowTickets = dto.ShowTickets; p.ShowWhatsApp = dto.ShowWhatsApp;
        p.ShowDeudores = dto.ShowDeudores; p.ShowZonas = dto.ShowZonas;
        p.ShowMetodosPago = dto.ShowMetodosPago; p.ShowComprobantes = dto.ShowComprobantes;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // M4 — US-DASH-PAGOS · Sección cobros enriquecida
    // ════════════════════════════════════════════════════════════════════════

    public async Task<DashPagosDto> GetDashPagosAsync()
    {
        var now    = DateTime.UtcNow;
        var hoy    = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var mesUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var pagosMes = await _db.Payments
            .Include(p => p.RegisteredBy)
            .Where(p => !p.IsVoided && p.PaidAt >= mesUtc)
            .ToListAsync();

        var pagosHoy = pagosMes.Where(p => p.PaidAt >= hoy).ToList();

        var totalMes = pagosMes.Sum(p => p.Amount);

        var porMetodo = pagosMes
            .GroupBy(p => p.Method.ToString())
            .Select(g => new DashPagosPorMetodoDto(
                g.Key, g.Count(), g.Sum(p => p.Amount),
                totalMes > 0 ? Math.Round(g.Sum(p => p.Amount) / totalMes * 100, 1) : 0))
            .OrderByDescending(x => x.Monto)
            .ToList();

        var porOperador = pagosMes
            .GroupBy(p => p.RegisteredBy?.FullName ?? "Sistema")
            .Select(g => new DashPagosPorOperadorDto(g.Key, g.Count(), g.Sum(p => p.Amount)))
            .OrderByDescending(x => x.Monto)
            .ToList();

        // Tendencia 6 meses
        var tend = await GetTendenciaCobrosAsync(6);

        return new DashPagosDto(
            pagosHoy.Sum(p => p.Amount), totalMes,
            pagosHoy.Count, pagosMes.Count,
            porMetodo, porOperador, tend.Meses);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M4 — US-DASH-TICKETS-M · Métricas de tickets
    // ════════════════════════════════════════════════════════════════════════

    public async Task<DashTicketsMetricasDto> GetDashTicketsMetricasAsync()
    {
        var now    = DateTime.UtcNow;
        var mesUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var tickets = await _db.SupportTickets
            .Include(t => t.AssignedTo)
            .ToListAsync();

        var abiertos   = tickets.Count(t => t.Status == TicketStatus.Abierto);
        var enProceso  = tickets.Count(t => t.Status == TicketStatus.EnProceso);
        var resMes     = tickets.Count(t => t.Status == TicketStatus.Resuelto && t.ResolvedAt >= mesUtc);

        // SLA: tickets resueltos este mes vs los que debían resolverse
        var resueltos      = tickets.Where(t => t.ResolvedAt != null && t.ResolvedAt >= mesUtc).ToList();
        var dentroSla      = resueltos.Count(t => t.ResolvedAt <= t.SlaDeadline);
        var slaCompliance  = resueltos.Count > 0
            ? Math.Round((double)dentroSla / resueltos.Count * 100, 1) : 100;

        var vencidosSla    = tickets
            .Where(t => (t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso)
                     && t.SlaDeadline < now)
            .OrderBy(t => t.SlaDeadline)
            .Take(5)
            .Select(t => new TicketDashItemDto(
                t.Id,
                t.Client?.FullName ?? "—",
                t.TicketNumber ?? t.Subject ?? t.Id.ToString()[..8],
                t.Type.ToString(),
                t.Priority.ToString(),
                t.Status.ToString(),
                t.AssignedTo?.FullName ?? "Sin asignar",
                t.CreatedAt,
                t.SlaDeadline,
                t.SlaDeadline.HasValue && t.SlaDeadline.Value < DateTime.UtcNow))
            .ToList();

        var resolucionProm = resueltos.Any()
            ? resueltos.Average(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalHours) : 0;

        var porTipo = tickets
            .GroupBy(t => t.Type.ToString())
            .Select(g => new DashTicketPorTipoMetricaDto(
                g.Key,
                g.Count(),
                g.Count(t => t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso),
                g.Count(t => t.Status == TicketStatus.Resuelto || t.Status == TicketStatus.Cerrado),
                g.Where(t => t.ResolvedAt != null)
                  .Select(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalHours)
                  .DefaultIfEmpty(0).Average()))
            .OrderByDescending(x => x.Total)
            .ToList();

        return new DashTicketsMetricasDto(
            abiertos, enProceso, resMes,
            vencidosSla.Count, slaCompliance,
            Math.Round(resolucionProm, 1),
            porTipo, vencidosSla);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M4 — US-DASH-NOTIF · Estado de notificaciones
    // ════════════════════════════════════════════════════════════════════════

    public async Task<DashNotifDto> GetDashNotifAsync()
    {
        var hace24h = DateTime.UtcNow.AddHours(-24);

        var logs = await _db.NotifLogs
            .Where(l => l.RegistradoAt >= hace24h)
            .ToListAsync();

        var pendientes = await _db.NotifOutbox
            .CountAsync(o => o.EstadoFinal == null && !o.Publicado);

        var omitidos   = logs.Count(l => l.Estado == Domain.Entities.Notifications.NotifLogEstado.OMITIDO);
        var enviadas   = logs.Count(l => l.Estado == Domain.Entities.Notifications.NotifLogEstado.ENVIADO);
        var fallidas   = logs.Count(l => l.Estado == Domain.Entities.Notifications.NotifLogEstado.FALLIDO);
        var total      = enviadas + fallidas;
        var tasaExito  = total > 0 ? Math.Round((double)enviadas / total * 100, 1) : 100;

        var porTipo = logs
            .GroupBy(l => l.Tipo.ToString())
            .Select(g => new DashNotifPorTipoDto(
                g.Key,
                g.Count(l => l.Estado == Domain.Entities.Notifications.NotifLogEstado.ENVIADO),
                g.Count(l => l.Estado == Domain.Entities.Notifications.NotifLogEstado.FALLIDO),
                0))
            .OrderByDescending(x => x.Enviadas + x.Fallidas)
            .ToList();

        return new DashNotifDto(enviadas, fallidas, pendientes, omitidos, tasaExito, porTipo);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M4 — US-DASH-CHATBOT · Métricas del chatbot
    //
    // CORRECCIÓN (Bug #1): El chatbot corre en un microservicio NestJS separado
    // con su propia BD. NO existen DbSets ChatbotSessions/ChatbotMessages en EF Core.
    // Los datos se obtienen vía BotProxyService (HTTP) inyectado en el constructor.
    // ════════════════════════════════════════════════════════════════════════

    public async Task<DashChatbotDto> GetDashChatbotAsync()
    {
        try
        {
            var stats = await _botProxy.GetStatsAsync();

            var total     = stats.TotalConversaciones;
            var escaladas = stats.Escaladas;

            var tasaEscalado   = total > 0
                ? Math.Round((double)escaladas / total * 100, 1) : 0;
            var tasaResolucion = total > 0
                ? Math.Round((double)(total - escaladas) / total * 100, 1) : 100;

            return new DashChatbotDto(
                ConversacionesActivas:  0,   // requeriría endpoint /monitor/active del chatbot
                ConversacionesHoy:      stats.HoyConversaciones,
                ConversacionesMes:      stats.TotalConversaciones,
                TasaResolucionBot:      tasaResolucion,
                TasaEscaladoHumano:     tasaEscalado,
                IntencionesFrecuentes:  new List<DashChatbotIntencionDto>()
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chatbot no disponible — retornando métricas vacías");
            return new DashChatbotDto(0, 0, 0, 0, 0, new List<DashChatbotIntencionDto>());
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // M4 — US-DASH-AUTO · Acciones automáticas del día
    // ════════════════════════════════════════════════════════════════════════

    public async Task<DashAutoActionsDto> GetDashAutoActionsAsync()
    {
        var hoy = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month,
                               DateTime.UtcNow.Day, 0, 0, 0, DateTimeKind.Utc);

        // Contar por acción en SQL — evita cargar todos los registros a memoria
        var counts = await _db.AuditLogs
            .Where(a => a.CreatedAt >= hoy && a.UserName == "Sistema")
            .GroupBy(a => a.Action)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);

        // Solo los 10 más recientes para la lista de UI
        var recientes = await _db.AuditLogs
            .Where(a => a.CreatedAt >= hoy && a.UserName == "Sistema")
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .Select(a => new DashAutoActionItemDto(
                a.Action, a.Description, a.CreatedAt,
                DeterminarModulo(a.Action)))
            .ToListAsync();

        return new DashAutoActionsDto(
            counts.GetValueOrDefault("CLIENT_SUSPENDED"),
            counts.GetValueOrDefault("CLIENT_REACTIVATED"),
            counts.GetValueOrDefault("BILLING_JOB_EXECUTED"),
            counts.GetValueOrDefault("REMINDER_SENT"),
            counts.GetValueOrDefault("OVERDUE_JOB_EXECUTED"),
            recientes);
    }

    private static string DeterminarModulo(string action) => action switch
    {
        var a when a.StartsWith("CLIENT_")   => "Clientes",
        var a when a.StartsWith("BILLING_")  => "Facturación",
        var a when a.StartsWith("NOTIF_")    => "Notificaciones",
        var a when a.StartsWith("REMINDER_") => "Notificaciones",
        var a when a.StartsWith("OVERDUE_")  => "Facturación",
        _ => "Sistema"
    };

}