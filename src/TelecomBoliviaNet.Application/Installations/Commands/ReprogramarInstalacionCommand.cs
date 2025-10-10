using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Installations.Commands;

public record ReprogramarInstalacionCommand(
    Guid                       InstalacionId,
    ReprogramarInstalacionDto  Dto,
    Guid                       ActorId,
    string                     ActorName,
    string                     ClientIp) : IRequest<Result>;

public class ReprogramarInstalacionHandler : IRequestHandler<ReprogramarInstalacionCommand, Result>
{
    private readonly IGenericRepository<Installation> _instRepo;
    private readonly IGenericRepository<UserSystem>   _userRepo;
    private readonly AuditService                     _audit;

    public ReprogramarInstalacionHandler(
        IGenericRepository<Installation> instRepo,
        IGenericRepository<UserSystem>   userRepo,
        AuditService                     audit)
    {
        _instRepo = instRepo;
        _userRepo = userRepo;
        _audit    = audit;
    }

    public async Task<Result> Handle(ReprogramarInstalacionCommand cmd, CancellationToken ct)
    {
        var inst = await _instRepo.GetByIdAsync(cmd.InstalacionId);
        if (inst is null) return Result.Failure("Instalación no encontrada.");

        if (inst.Status is InstallationStatus.Completada or InstallationStatus.Cancelada)
            return Result.Failure("No se puede reprogramar una instalación completada o cancelada.");

        if (!DateTime.TryParse(cmd.Dto.Fecha, out var nuevaFechaParsed))
            return Result.Failure("Formato de fecha inválido.");

        var nuevaFecha = DateTime.SpecifyKind(nuevaFechaParsed, DateTimeKind.Utc);

        if (!TimeOnly.TryParse(cmd.Dto.HoraInicio, out var nuevaHora))
            return Result.Failure("Formato de hora inválido.");

        var ocupados = await _instRepo.GetAll()
            .CountAsync(i =>
                i.Id != cmd.InstalacionId &&
                i.Fecha.Date == nuevaFecha.Date &&
                i.HoraInicio == nuevaHora &&
                (i.Status == InstallationStatus.Pendiente ||
                 i.Status == InstallationStatus.EnProceso), ct);

        if (ocupados >= await CountTecnicosAsync(ct))
            return Result.Failure("El nuevo horario no tiene disponibilidad.");

        inst.Fecha         = DateTime.SpecifyKind(nuevaFecha.Date, DateTimeKind.Utc);
        inst.HoraInicio    = nuevaHora;
        inst.Status        = InstallationStatus.Reprogramada;
        inst.ActualizadoAt = DateTime.UtcNow;
        await _instRepo.UpdateAsync(inst);

        await _audit.LogAsync("Instalaciones", "INSTALACION_REPROGRAMADA",
            $"Instalación {cmd.InstalacionId} reprogramada a {cmd.Dto.Fecha} {cmd.Dto.HoraInicio}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp);

        return Result.Success();
    }

    private async Task<int> CountTecnicosAsync(CancellationToken ct)
    {
        var count = await _userRepo.GetAll()
            .CountAsync(u => u.Role == UserRole.Tecnico && u.Status == UserStatus.Activo, ct);
        return Math.Max(count, 1);
    }
}
