using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetSlaAlertsQuery : IRequest<SlaAlertsDto>;

public class GetSlaAlertsHandler : IRequestHandler<GetSlaAlertsQuery, SlaAlertsDto>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;

    public GetSlaAlertsHandler(IGenericRepository<SupportTicket> ticketRepo) => _ticketRepo = ticketRepo;

    public async Task<SlaAlertsDto> Handle(GetSlaAlertsQuery _, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var tickets = await _ticketRepo.GetAll()
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Where(t => t.DueDate.HasValue &&
                        t.Status != TicketStatus.Resuelto &&
                        t.Status != TicketStatus.Cerrado  &&
                        t.Status != TicketStatus.PendienteCliente)
            .Select(t => new {
                t.Id, t.TicketNumber, t.Subject, t.Priority, t.Status,
                ClientName     = t.Client != null ? t.Client.FullName : "",
                AssignedToName = t.AssignedTo != null ? t.AssignedTo.FullName : (string?)null,
                t.CreatedAt, DueDate = t.DueDate!.Value,
            })
            .ToListAsync(ct);

        var breached  = new List<SlaAlertItemDto>();
        var warning   = new List<SlaAlertItemDto>();
        var attention = new List<SlaAlertItemDto>();

        foreach (var t in tickets)
        {
            var totalMin   = (t.DueDate - t.CreatedAt).TotalMinutes;
            var elapsedMin = (now - t.CreatedAt).TotalMinutes;
            var pct        = totalMin > 0 ? (int)(elapsedMin / totalMin * 100) : 100;

            var item = new SlaAlertItemDto(
                t.Id, t.TicketNumber ?? "", t.Subject,
                t.Priority.ToString(), t.Status.ToString(),
                t.ClientName, t.AssignedToName,
                t.CreatedAt, t.DueDate, pct,
                pct >= 100 ? "Breached" : pct >= 75 ? "Warning" : "Attention");

            if      (pct >= 100) breached.Add(item);
            else if (pct >= 75)  warning.Add(item);
            else if (pct >= 50)  attention.Add(item);
        }

        return new SlaAlertsDto(
            breached .OrderByDescending(x => x.PctElapsed),
            warning  .OrderByDescending(x => x.PctElapsed),
            attention.OrderByDescending(x => x.PctElapsed));
    }
}
