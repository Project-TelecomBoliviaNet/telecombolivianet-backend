using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetOverdueSlaQuery : IRequest<IEnumerable<TicketListItemDto>>;

public class GetOverdueSlaHandler : IRequestHandler<GetOverdueSlaQuery, IEnumerable<TicketListItemDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;

    public GetOverdueSlaHandler(IGenericRepository<SupportTicket> ticketRepo) => _ticketRepo = ticketRepo;

    public async Task<IEnumerable<TicketListItemDto>> Handle(GetOverdueSlaQuery _, CancellationToken ct)
    {
        var now   = DateTime.UtcNow;
        var items = await _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.WorkLogs)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value < now
                     && t.Status != TicketStatus.Resuelto
                     && t.Status != TicketStatus.Cerrado
                     && t.Status != TicketStatus.PendienteCliente)
            .OrderBy(t => t.DueDate)
            .ToListAsync(ct);
        return items.Select(TicketHelper.MapToListItem);
    }
}

public record GetSlaReportQuery(DateTime? Desde, DateTime? Hasta, string? Tecnico)
    : IRequest<IEnumerable<TicketListItemDto>>;

public class GetSlaReportHandler : IRequestHandler<GetSlaReportQuery, IEnumerable<TicketListItemDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;

    public GetSlaReportHandler(IGenericRepository<SupportTicket> ticketRepo) => _ticketRepo = ticketRepo;

    public async Task<IEnumerable<TicketListItemDto>> Handle(GetSlaReportQuery q, CancellationToken ct)
    {
        var now   = DateTime.UtcNow;
        var query = _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.WorkLogs)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value < now
                     && t.Status != TicketStatus.Resuelto
                     && t.Status != TicketStatus.Cerrado
                     && t.Status != TicketStatus.PendienteCliente);

        if (q.Desde.HasValue)  query = query.Where(t => t.CreatedAt >= q.Desde.Value);
        if (q.Hasta.HasValue)  query = query.Where(t => t.CreatedAt <= q.Hasta.Value);
        if (!string.IsNullOrWhiteSpace(q.Tecnico))
            query = query.Where(t => t.AssignedTo != null && t.AssignedTo.FullName.Contains(q.Tecnico));

        var items = await query.OrderBy(t => t.DueDate).ToListAsync(ct);
        return items.Select(TicketHelper.MapToListItem);
    }
}
