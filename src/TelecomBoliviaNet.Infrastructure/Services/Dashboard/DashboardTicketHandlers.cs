using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Dashboard.Queries;
using TelecomBoliviaNet.Application.DTOs.Dashboard;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Services.Dashboard;

// ── GetTicketsEstadoHandler ───────────────────────────────────────────────

public class GetTicketsEstadoHandler : IRequestHandler<GetTicketsEstadoQuery, List<TicketEstadoDashDto>>
{
    private readonly AppDbContext _db;
    public GetTicketsEstadoHandler(AppDbContext db) => _db = db;

    public async Task<List<TicketEstadoDashDto>> Handle(GetTicketsEstadoQuery _, CancellationToken ct)
    {
        var raw = await _db.SupportTickets
            .GroupBy(t => t.Status)
            .Select(g => new { Estado = g.Key.ToString(), Total = g.Count(), Criticos = g.Count(t => t.Priority == TicketPriority.Critica) })
            .ToListAsync(ct);
        return raw.Select(r => new TicketEstadoDashDto(r.Estado, r.Total, r.Criticos)).ToList();
    }
}

// ── GetTicketsUrgentesHandler ─────────────────────────────────────────────

public class GetTicketsUrgentesHandler : IRequestHandler<GetTicketsUrgentesQuery, List<TicketDashItemDto>>
{
    private readonly AppDbContext _db;
    public GetTicketsUrgentesHandler(AppDbContext db) => _db = db;

    public async Task<List<TicketDashItemDto>> Handle(GetTicketsUrgentesQuery q, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.SupportTickets
            .Where(t => t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso || t.Status == TicketStatus.PendienteCliente)
            .OrderBy(t => t.Priority).ThenBy(t => t.DueDate).Take(q.Top)
            .Select(t => new TicketDashItemDto(
                t.Id, t.Client!.FullName, t.Subject, t.Type.ToString(), t.Priority.ToString(),
                t.Status.ToString(), t.AssignedTo != null ? t.AssignedTo.FullName : "Sin asignar",
                t.CreatedAt, t.DueDate, t.DueDate != null && t.DueDate < now))
            .ToListAsync(ct);
    }
}

// ── GetTicketsPorTipoHandler ──────────────────────────────────────────────

public class GetTicketsPorTipoHandler : IRequestHandler<GetTicketsPorTipoQuery, List<TicketPorTipoDto>>
{
    private readonly AppDbContext _db;
    public GetTicketsPorTipoHandler(AppDbContext db) => _db = db;

    public async Task<List<TicketPorTipoDto>> Handle(GetTicketsPorTipoQuery _, CancellationToken ct)
    {
        var raw = await _db.SupportTickets
            .GroupBy(t => t.Type)
            .Select(g => new { Tipo = g.Key.ToString(), Total = g.Count() })
            .OrderByDescending(g => g.Total).ToListAsync(ct);
        return raw.Select(r => new TicketPorTipoDto(r.Tipo, r.Total)).ToList();
    }
}

// ── GetResolucionPromedioHandler ──────────────────────────────────────────

public class GetResolucionPromedioHandler : IRequestHandler<GetResolucionPromedioQuery, List<ResolucionPromDto>>
{
    private readonly AppDbContext _db;
    public GetResolucionPromedioHandler(AppDbContext db) => _db = db;

    public async Task<List<ResolucionPromDto>> Handle(GetResolucionPromedioQuery _, CancellationToken ct)
    {
        var raw = await _db.SupportTickets
            .Where(t => t.ResolvedAt != null)
            .Select(t => new { Prio = t.Priority.ToString(), t.CreatedAt, t.ResolvedAt })
            .ToListAsync(ct);

        return raw.GroupBy(t => t.Prio)
            .Select(g => new ResolucionPromDto(
                g.Key,
                g.Average(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalHours),
                g.Count()))
            .OrderBy(r => r.Prioridad).ToList();
    }
}
