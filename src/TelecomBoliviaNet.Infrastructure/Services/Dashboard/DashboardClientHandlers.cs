using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Dashboard.Queries;
using TelecomBoliviaNet.Application.DTOs.Dashboard;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Services.Dashboard;

// ── F6: GetProximosCortHandler ────────────────────────────────────────────

public class GetProximosCortHandler : IRequestHandler<GetProximosCortQuery, ProximosCortDto>
{
    private readonly AppDbContext _db;
    public GetProximosCortHandler(AppDbContext db) => _db = db;

    public async Task<ProximosCortDto> Handle(GetProximosCortQuery q, CancellationToken ct)
    {
        var ahora      = DateTime.UtcNow;
        var limiteVista = ahora.AddDays(q.DiasVista);

        // Buscar desde Invoice hacia Client (más eficiente que cargar todos los clientes)
        var facturas = await _db.Invoices
            .Where(i => !i.IsDeleted
                && i.Status == InvoiceStatus.Pendiente
                && i.DueDate >= ahora
                && i.DueDate <= limiteVista
                && i.Client != null
                && i.Client.Status == ClientStatus.Activo
                && !i.Client.IsDeleted)
            .Select(i => new {
                i.ClientId,
                ClienteNombre = i.Client!.FullName,
                Zona          = i.Client.Zone,
                i.Amount,
                i.DueDate,
                Estado        = i.Status.ToString()
            })
            .ToListAsync(ct);

        // Un cliente puede tener varias facturas: tomamos la de vencimiento más próximo
        var porCliente = facturas
            .GroupBy(f => f.ClientId)
            .Select(g => g.OrderBy(f => f.DueDate).First())
            .Select(f => new ClienteProximoCortDto(
                f.ClientId,
                f.ClienteNombre,
                f.Zona,
                f.Amount,
                (int)(f.DueDate - ahora).TotalDays,
                f.Estado))
            .OrderBy(x => x.DiasParaCorte)
            .ToList();

        return new ProximosCortDto(porCliente.Count, porCliente);
    }
}

// ── F7: GetWorkloadTecnicosHandler ────────────────────────────────────────

public class GetWorkloadTecnicosHandler : IRequestHandler<GetWorkloadTecnicosQuery, WorkloadTecnicosDto>
{
    private readonly AppDbContext _db;
    public GetWorkloadTecnicosHandler(AppDbContext db) => _db = db;

    public async Task<WorkloadTecnicosDto> Handle(GetWorkloadTecnicosQuery _, CancellationToken ct)
    {
        var now     = DateTime.UtcNow;
        var activos = new[] {
            TicketStatus.Abierto, TicketStatus.EnProceso,
            TicketStatus.PendienteCliente, TicketStatus.EnVisita
        };

        var tickets = await _db.SupportTickets
            .Include(t => t.AssignedTo)
            .Where(t => activos.Contains(t.Status))
            .Select(t => new {
                t.AssignedToUserId,
                TecnicoNombre = t.AssignedTo != null ? t.AssignedTo.FullName : null,
                t.Priority,
                t.Status,
                t.CreatedAt
            })
            .ToListAsync(ct);

        var sinAsignar = tickets.Count(t => t.AssignedToUserId == null);

        var porTecnico = tickets
            .Where(t => t.AssignedToUserId != null)
            .GroupBy(t => t.TecnicoNombre ?? "Desconocido")
            .Select(g => new WorkloadTecnicoDto(
                g.Key,
                g.Count(),
                g.Count(t => t.Priority == TicketPriority.Critica),
                g.Count(t => t.Status == TicketStatus.EnVisita),
                Math.Round(g.Average(t => (now - t.CreatedAt).TotalHours), 1)))
            .OrderByDescending(x => x.TicketsActivos)
            .ToList();

        return new WorkloadTecnicosDto(sinAsignar, porTecnico);
    }
}
