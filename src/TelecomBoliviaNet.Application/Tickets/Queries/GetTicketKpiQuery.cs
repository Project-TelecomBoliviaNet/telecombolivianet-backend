using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetTicketKpiQuery : IRequest<TicketKpiDto>;

public class GetTicketKpiHandler : IRequestHandler<GetTicketKpiQuery, TicketKpiDto>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;

    public GetTicketKpiHandler(IGenericRepository<SupportTicket> ticketRepo) => _ticketRepo = ticketRepo;

    public async Task<TicketKpiDto> Handle(GetTicketKpiQuery _, CancellationToken ct)
    {
        var now   = DateTime.UtcNow;
        var today = now.Date;
        var q     = _ticketRepo.GetAll();

        var abierto   = await q.CountAsync(t => t.Status == TicketStatus.Abierto, ct);
        var enProceso = await q.CountAsync(t => t.Status == TicketStatus.EnProceso || t.Status == TicketStatus.PendienteCliente, ct);
        var resuelto  = await q.CountAsync(t => t.Status == TicketStatus.Resuelto, ct);
        var cerrado   = await q.CountAsync(t => t.Status == TicketStatus.Cerrado, ct);
        var vencidos  = await q.CountAsync(t =>
            t.DueDate.HasValue && t.DueDate.Value < now &&
            t.Status != TicketStatus.Resuelto &&
            t.Status != TicketStatus.Cerrado  &&
            t.Status != TicketStatus.PendienteCliente, ct);
        var hoy = await q.CountAsync(t => t.CreatedAt.Date == today, ct);
        var slaCump   = await q.CountAsync(t => t.SlaCompliant == true, ct);
        var slaIncump = await q.CountAsync(t => t.SlaCompliant == false, ct);
        var csatProm  = await q
            .Where(t => t.CsatScore.HasValue)
            .AverageAsync(t => (double?)t.CsatScore!.Value, ct);

        var slaRaw = await q
            .Where(t => t.SlaCompliant.HasValue)
            .GroupBy(t => t.Priority)
            .Select(g => new {
                Priority  = g.Key.ToString(),
                Compliant = g.Count(t => t.SlaCompliant == true),
                Breached  = g.Count(t => t.SlaCompliant == false),
            })
            .ToListAsync(ct);

        var priorityOrder = new[] { "Critica", "Alta", "Media", "Baja" };
        var slaByPriority = slaRaw
            .OrderBy(x => Array.IndexOf(priorityOrder, x.Priority))
            .Select(x => new SlaByPriorityDto(x.Priority, x.Compliant, x.Breached));

        var resolvedPairs = await q
            .Where(t => t.ResolvedAt.HasValue)
            .Select(t => new { t.CreatedAt, t.ResolvedAt })
            .ToListAsync(ct);
        double? mttr = resolvedPairs.Any()
            ? resolvedPairs.Average(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes)
            : null;

        var firstResponsePairs = await q
            .Where(t => t.FirstRespondedAt.HasValue)
            .Select(t => new { t.CreatedAt, t.FirstRespondedAt })
            .ToListAsync(ct);
        double? mtta = firstResponsePairs.Any()
            ? firstResponsePairs.Average(t => (t.FirstRespondedAt!.Value - t.CreatedAt).TotalMinutes)
            : null;

        return new TicketKpiDto(abierto, enProceso, resuelto, cerrado,
            vencidos, hoy, slaCump, slaIncump, csatProm,
            slaByPriority, mttr, mtta);
    }
}
