using MediatR;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Installations.Queries;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Installations.Commands;

public record CrearInstalacionAdminCommand(
    CrearInstalacionAdminDto Dto,
    Guid   ActorId,
    string ActorName,
    string ClientIp) : IRequest<Result<InstalacionDetalleDto>>;

public class CrearInstalacionAdminHandler
    : IRequestHandler<CrearInstalacionAdminCommand, Result<InstalacionDetalleDto>>
{
    private readonly ISender _sender;

    public CrearInstalacionAdminHandler(ISender sender) => _sender = sender;

    public async Task<Result<InstalacionDetalleDto>> Handle(
        CrearInstalacionAdminCommand cmd, CancellationToken ct)
    {
        var dto     = cmd.Dto;
        var dtoBase = new CrearInstalacionDto
        {
            ClienteId  = dto.ClienteId,
            PlanId     = dto.PlanId,
            Fecha      = dto.Fecha,
            HoraInicio = dto.HoraInicio,
            Direccion  = dto.Direccion,
            Notas      = dto.Notas,
        };

        var crearResult = await _sender.Send(
            new CrearInstalacionCommand(dtoBase, cmd.ActorId, cmd.ActorName, cmd.ClientIp), ct);

        if (!crearResult.IsSuccess)
            return Result<InstalacionDetalleDto>.Failure(crearResult.ErrorMessage!);

        var instId = Guid.Parse(crearResult.Value!.InstalacionId);

        if (dto.TecnicoId.HasValue)
        {
            await _sender.Send(
                new AsignarTecnicoCommand(
                    instId,
                    new AsignarTecnicoDto { TecnicoId = dto.TecnicoId.Value },
                    cmd.ActorId, cmd.ActorName, cmd.ClientIp), ct);
        }

        var detalle = await _sender.Send(new GetInstalacionDetalleQuery(instId), ct);
        return detalle is not null
            ? Result<InstalacionDetalleDto>.Success(detalle)
            : Result<InstalacionDetalleDto>.Failure("Error al obtener detalle.");
    }
}
