using MediatR;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Installations.Commands;

public record AsignarTecnicoCommand(
    Guid              InstalacionId,
    AsignarTecnicoDto Dto,
    Guid              ActorId,
    string            ActorName,
    string            ClientIp) : IRequest<Result>;

public class AsignarTecnicoHandler : IRequestHandler<AsignarTecnicoCommand, Result>
{
    private readonly IGenericRepository<Installation> _instRepo;
    private readonly IGenericRepository<UserSystem>   _userRepo;
    private readonly AuditService                     _audit;

    public AsignarTecnicoHandler(
        IGenericRepository<Installation> instRepo,
        IGenericRepository<UserSystem>   userRepo,
        AuditService                     audit)
    {
        _instRepo = instRepo;
        _userRepo = userRepo;
        _audit    = audit;
    }

    public async Task<Result> Handle(AsignarTecnicoCommand cmd, CancellationToken ct)
    {
        var inst = await _instRepo.GetByIdAsync(cmd.InstalacionId);
        if (inst is null) return Result.Failure("Instalación no encontrada.");

        var tecnico = await _userRepo.GetByIdAsync(cmd.Dto.TecnicoId);
        if (tecnico is null) return Result.Failure("El técnico indicado no existe.");
        if (tecnico.Role != UserRole.Tecnico && tecnico.Role != UserRole.Admin)
            return Result.Failure("Solo se puede asignar a un usuario con rol Técnico o Admin.");

        inst.TecnicoId     = cmd.Dto.TecnicoId;
        inst.Status        = InstallationStatus.EnProceso;
        inst.ActualizadoAt = DateTime.UtcNow;
        await _instRepo.UpdateAsync(inst);

        await _audit.LogAsync("Instalaciones", "INSTALACION_TECNICO_ASIGNADO",
            $"Técnico {tecnico.FullName} asignado a instalación {cmd.InstalacionId}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp);

        return Result.Success();
    }
}
