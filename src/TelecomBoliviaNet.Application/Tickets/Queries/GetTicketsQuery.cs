using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetTicketsQuery(TicketFilterDto Filter) : IRequest<PagedResult<TicketListItemDto>>;

public class GetTicketsHandler : IRequestHandler<GetTicketsQuery, PagedResult<TicketListItemDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;

    public GetTicketsHandler(IGenericRepository<SupportTicket> ticketRepo) => _ticketRepo = ticketRepo;

    public async Task<PagedResult<TicketListItemDto>> Handle(GetTicketsQuery q, CancellationToken ct)
    {
        var f     = q.Filter;
        var query = _ticketRepo.GetAllReadOnly()
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.WorkLogs)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim().ToLower();
            query = query.Where(t =>
                (t.Client != null && t.Client.FullName.ToLower().Contains(s)) ||
                (t.Client != null && t.Client.TbnCode.ToLower().Contains(s))  ||
                t.Subject.ToLower().Contains(s)                               ||
                t.Description.ToLower().Contains(s));
        }
        if (!string.IsNullOrEmpty(f.Status) && f.Status != "all"
            && Enum.TryParse<TicketStatus>(f.Status, out var st))
            query = query.Where(t => t.Status == st);
        if (!string.IsNullOrEmpty(f.Priority) && f.Priority != "all"
            && Enum.TryParse<TicketPriority>(f.Priority, out var pr))
            query = query.Where(t => t.Priority == pr);
        if (!string.IsNullOrEmpty(f.Type) && f.Type != "all"
            && Enum.TryParse<TicketType>(f.Type, out var tp))
            query = query.Where(t => t.Type == tp);
        if (f.AssignedToId.HasValue)
            query = query.Where(t => t.AssignedToUserId == f.AssignedToId.Value);
        if (f.OverdueSla == true)
        {
            var now = DateTime.UtcNow;
            query = query.Where(t =>
                t.DueDate.HasValue && t.DueDate.Value < now &&
                t.Status != TicketStatus.Resuelto &&
                t.Status != TicketStatus.Cerrado  &&
                t.Status != TicketStatus.PendienteCliente);
        }
        if (f.DateFrom.HasValue)
            query = query.Where(t => t.CreatedAt >= f.DateFrom.Value);
        if (f.DateTo.HasValue)
            query = query.Where(t => t.CreatedAt <= f.DateTo.Value.AddDays(1).AddTicks(-1));
        if (f.SlaCompliant.HasValue)
            query = query.Where(t => t.SlaCompliant == f.SlaCompliant.Value);

        query = query
            .OrderBy(t => t.Status == TicketStatus.Resuelto || t.Status == TicketStatus.Cerrado ? 1 : 0)
            .ThenBy(t => t.Priority == TicketPriority.Critica ? 0
                       : t.Priority == TicketPriority.Alta    ? 1
                       : t.Priority == TicketPriority.Media   ? 2 : 3)
            .ThenBy(t => t.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((f.PageNumber - 1) * f.PageSize)
            .Take(f.PageSize)
            .ToListAsync(ct);

        return new PagedResult<TicketListItemDto>(
            items.Select(TicketHelper.MapToListItem), total, f.PageNumber, f.PageSize);
    }
}
