using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Installations.Queries;

public record GetInstalacionesListQuery(InstalacionFilterDto Filter)
    : IRequest<PagedResult<InstalacionListItemDto>>;

public class GetInstalacionesListHandler
    : IRequestHandler<GetInstalacionesListQuery, PagedResult<InstalacionListItemDto>>
{
    private readonly IGenericRepository<Installation> _instRepo;

    public GetInstalacionesListHandler(IGenericRepository<Installation> instRepo)
        => _instRepo = instRepo;

    public async Task<PagedResult<InstalacionListItemDto>> Handle(
        GetInstalacionesListQuery req, CancellationToken ct)
    {
        var f     = req.Filter;
        var query = _instRepo.GetAll()
            .Include(i => i.Client)
            .Include(i => i.Plan)
            .Include(i => i.Tecnico)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.Status) &&
            Enum.TryParse<InstallationStatus>(f.Status, out var st))
            query = query.Where(i => i.Status == st);

        if (f.TecnicoId.HasValue)
            query = query.Where(i => i.TecnicoId == f.TecnicoId);

        if (f.ClienteId.HasValue)
            query = query.Where(i => i.ClientId == f.ClienteId);

        if (!string.IsNullOrWhiteSpace(f.Fecha) && DateTime.TryParse(f.Fecha, out var fd))
            query = query.Where(i => i.Fecha.Date == fd.Date);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(i => i.Fecha).ThenBy(i => i.HoraInicio)
            .Skip((f.Page - 1) * f.PageSize)
            .Take(f.PageSize)
            .ToListAsync(ct);

        return new PagedResult<InstalacionListItemDto>(
            items.Select(InstallationMapper.ToListItem),
            total, f.Page, f.PageSize);
    }
}
