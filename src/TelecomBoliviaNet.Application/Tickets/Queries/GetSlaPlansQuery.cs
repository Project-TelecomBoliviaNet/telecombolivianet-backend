using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Tickets.Queries;

public record GetSlaPlansQuery : IRequest<IEnumerable<SlaPlanDto>>;

public class GetSlaPlansHandler : IRequestHandler<GetSlaPlansQuery, IEnumerable<SlaPlanDto>>
{
    private readonly IGenericRepository<SlaPlan> _slaPlanRepo;

    public GetSlaPlansHandler(IGenericRepository<SlaPlan> slaPlanRepo) => _slaPlanRepo = slaPlanRepo;

    public async Task<IEnumerable<SlaPlanDto>> Handle(GetSlaPlansQuery _, CancellationToken ct)
        => (await _slaPlanRepo.GetAll().OrderBy(p => p.Priority).ToListAsync(ct))
           .Select(TicketHelper.MapSlaPlan);
}
