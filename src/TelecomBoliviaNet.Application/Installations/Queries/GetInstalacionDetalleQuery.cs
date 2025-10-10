using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Installations.Queries;

public record GetInstalacionDetalleQuery(Guid Id)
    : IRequest<InstalacionDetalleDto?>;

public class GetInstalacionDetalleHandler
    : IRequestHandler<GetInstalacionDetalleQuery, InstalacionDetalleDto?>
{
    private readonly IGenericRepository<Installation> _instRepo;

    public GetInstalacionDetalleHandler(IGenericRepository<Installation> instRepo)
        => _instRepo = instRepo;

    public async Task<InstalacionDetalleDto?> Handle(
        GetInstalacionDetalleQuery req, CancellationToken ct)
    {
        var inst = await _instRepo.GetAll()
            .Include(i => i.Client)
            .Include(i => i.Plan)
            .Include(i => i.Tecnico)
            .FirstOrDefaultAsync(i => i.Id == req.Id, ct);

        return inst is null ? null : InstallationMapper.ToDetalle(inst);
    }
}
