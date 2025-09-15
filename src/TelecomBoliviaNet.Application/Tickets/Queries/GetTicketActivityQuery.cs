using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetTicketActivityQuery(Guid TicketId) : IRequest<IEnumerable<TicketActivityDto>>;

public class GetTicketActivityHandler : IRequestHandler<GetTicketActivityQuery, IEnumerable<TicketActivityDto>>
{
    private readonly IGenericRepository<AuditLog> _auditRepo;

    public GetTicketActivityHandler(IGenericRepository<AuditLog> auditRepo)
        => _auditRepo = auditRepo;

    public async Task<IEnumerable<TicketActivityDto>> Handle(GetTicketActivityQuery query, CancellationToken ct)
    {
        var logs = await _auditRepo.GetAllReadOnly()
            .Where(a => a.EntityId == query.TicketId && a.Module == "Tickets")
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return logs.Select(a => new TicketActivityDto(
            a.Id, a.Action, a.Description, a.UserName, a.UserId, a.CreatedAt));
    }
}
