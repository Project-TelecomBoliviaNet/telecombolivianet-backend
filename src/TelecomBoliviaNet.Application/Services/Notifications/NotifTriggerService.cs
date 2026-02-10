#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Notifications;

/// <summary>
/// SRP: solo gestiona configuración de triggers.
/// US-35 / US-38 / US-NOT-04.
/// Extraído de NotifConfigService (Problema #6).
/// </summary>
public class NotifTriggerService
{
    private readonly IGenericRepository<NotifConfig> _repo;
    private readonly AuditService                    _audit;

    public NotifTriggerService(
        IGenericRepository<NotifConfig> repo,
        AuditService                   audit)
    {
        _repo  = repo;
        _audit = audit;
    }

    public async Task<NotifConfigListDto> GetConfigsAsync()
    {
        var configs   = await _repo.GetAll().OrderBy(c => c.Tipo).ToListAsync();
        var horaLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, NotifShared.BoliviaZone).ToString("HH:mm");
        return new NotifConfigListDto(configs.Select(NotifShared.ToConfigDto).ToList(), horaLocal);
    }

    public async Task<Result> UpdateConfigsAsync(
        UpdateNotifConfigsDto dto, Guid actorId, string actorName, string ip)
    {
        foreach (var upd in dto.Configs)
        {
            var config = await _repo.GetAll().FirstOrDefaultAsync(c => c.Tipo == upd.Tipo);
            if (config is null) continue;

            var prev = JsonSerializer.Serialize(NotifShared.ToConfigDto(config));

            config.Activo            = upd.Activo;
            config.DelaySegundos     = upd.DelaySegundos;
            config.HoraInicio        = TimeOnly.Parse(upd.HoraInicio);
            config.HoraFin           = TimeOnly.Parse(upd.HoraFin);
            config.Inmediato         = upd.Inmediato;
            config.DiasAntes         = upd.DiasAntes;
            config.PlantillaId       = upd.PlantillaId;
            config.ActualizadoAt     = DateTime.UtcNow;
            config.ActualizadoPorId  = actorId;
            await _repo.UpdateAsync(config);

            await _audit.LogAsync("Notificaciones", "NOTIF_CONFIG_UPDATED",
                $"Config actualizada: {upd.Tipo}",
                userId: actorId, userName: actorName, ip: ip,
                prevData: prev, newData: JsonSerializer.Serialize(upd));
        }
        return Result.Success();
    }
}
