#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Notifications;

/// <summary>
/// SRP: envío masivo con anti-spam y cancelación de notificaciones.
/// US-NOT-ANTISPAM / US-39.
/// Extraído de NotifConfigService (Problema #6).
/// </summary>
public class NotifEnvioService
{
    private readonly IGenericRepository<NotifConfig>  _configRepo;
    private readonly IGenericRepository<NotifOutbox>  _outboxRepo;
    private readonly IGenericRepository<NotifLog>     _logRepo;
    private readonly IGenericRepository<Client>       _clientRepo;
    private readonly NotifSegmentService              _segmentSvc;
    private readonly AuditService                     _audit;

    private const int AntiSpamWindowHours = 24;

    public NotifEnvioService(
        IGenericRepository<NotifConfig> configRepo,
        IGenericRepository<NotifOutbox> outboxRepo,
        IGenericRepository<NotifLog>    logRepo,
        IGenericRepository<Client>      clientRepo,
        NotifSegmentService             segmentSvc,
        AuditService                    audit)
    {
        _configRepo  = configRepo;
        _outboxRepo  = outboxRepo;
        _logRepo     = logRepo;
        _clientRepo  = clientRepo;
        _segmentSvc  = segmentSvc;
        _audit       = audit;
    }

    // ── US-NOT-ANTISPAM · Envío masivo ────────────────────────────────────────

    public async Task<Result<EnvioMasivoResultDto>> EnvioMasivoAsync(
        EnvioMasivoDto dto, Guid actorId, string actorName, string ip)
    {
        var config = await _configRepo.GetAll()
            .FirstOrDefaultAsync(c => c.Tipo == dto.Tipo);

        if (config is null || !config.Activo)
            return Result<EnvioMasivoResultDto>.Failure(
                $"El tipo {dto.Tipo} está desactivado o no existe.");

        var clientes = await LoadTargetClientsAsync(dto);

        var ventana24h  = DateTime.UtcNow.AddHours(-AntiSpamWindowHours);
        int enviados    = 0;
        int omitidos    = 0;
        int sinTelefono = 0;

        // BUG FIX: acumular outboxes en lista y usar AddRangeAsync al final
        // en lugar de AddAsync individual (que llama SaveChangesAsync por cada cliente).
        var outboxBatch = new List<NotifOutbox>();

        foreach (var cliente in clientes)
        {
            if (string.IsNullOrWhiteSpace(cliente.PhoneMain))
            {
                sinTelefono++;
                continue;
            }

            // Anti-spam: verificar si ya recibió este tipo en 24h
            var yaRecibio = await _logRepo.GetAll()
                .AnyAsync(l => l.ClienteId == cliente.Id
                            && l.Tipo      == dto.Tipo
                            && l.Estado    == NotifLogEstado.ENVIADO
                            && l.RegistradoAt >= ventana24h);

            if (yaRecibio)
            {
                await RegistrarOmitidoAsync(cliente, dto.Tipo);
                omitidos++;
                continue;
            }

            outboxBatch.Add(new NotifOutbox
            {
                Tipo         = dto.Tipo,
                ClienteId    = cliente.Id,
                PhoneNumber  = cliente.PhoneMain,
                Publicado    = false,
                Intentos     = 0,
                EnviarDesde  = DateTime.UtcNow.AddSeconds(config.DelaySegundos),
                CreadoAt     = DateTime.UtcNow,
                ContextoJson = "{}",
            });
            enviados++;
        }

        if (outboxBatch.Count > 0)
        {
            await _outboxRepo.AddRangeAsync(outboxBatch);
            await _outboxRepo.SaveChangesAsync();
        }

        await _audit.LogAsync("Notificaciones", "NOTIF_ENVIO_MASIVO",
            $"Envío masivo tipo={dto.Tipo} enviados={enviados} omitidos={omitidos}",
            userId: actorId, userName: actorName, ip: ip);

        return Result<EnvioMasivoResultDto>.Success(
            new EnvioMasivoResultDto(enviados, omitidos, sinTelefono));
    }

    // ── US-39 · Cancelación ───────────────────────────────────────────────────

    public async Task<r> CancelNotifAsync(
        Guid notifId, Guid actorId, string actorName, string ip)
    {
        var outbox = await _outboxRepo.GetByIdAsync(notifId);
        if (outbox is null) return Result.Failure("Notificación no encontrada.");
        if (outbox.EstadoFinal.HasValue)
            return Result.Failure($"La notificación ya tiene estado final: {outbox.EstadoFinal}.");

        outbox.EstadoFinal = NotifEstadoFinal.CANCELADO;
        outbox.Publicado   = true;
        outbox.ProcesadoAt = DateTime.UtcNow;
        await _outboxRepo.UpdateAsync(outbox);

        await _logRepo.AddAsync(new NotifLog
        {
            OutboxId     = outbox.Id,
            ClienteId    = outbox.ClienteId,
            Tipo         = outbox.Tipo,
            PhoneNumber  = outbox.PhoneNumber,
            Mensaje      = string.Empty,
            Estado       = NotifLogEstado.CANCELADO,
            IntentoNum   = outbox.Intentos,
            RegistradoAt = DateTime.UtcNow,
        });

        await _audit.LogAsync("Notificaciones", "NOTIF_CANCELLED",
            $"Notificación cancelada: {outbox.Id} tipo={outbox.Tipo}",
            userId: actorId, userName: actorName, ip: ip);

        return Result.Success();
    }

    public async Task<Result<CancelMasivaResultDto>> CancelMasivaAsync(
        NotifType tipo, string? razon, Guid actorId, string actorName, string ip)
    {
        var pendientes = await _outboxRepo.GetAll()
            .Where(o => o.Tipo == tipo && o.EstadoFinal == null)
            .ToListAsync();

        // BUG FIX: acumular logs y usar UpdateRangeAsync + AddRangeAsync
        // en lugar de N llamadas individuales a UpdateAsync/AddAsync.
        var logBatch = new List<NotifLog>();
        foreach (var o in pendientes)
        {
            o.EstadoFinal = NotifEstadoFinal.CANCELADO;
            o.Publicado   = true;
            o.ProcesadoAt = DateTime.UtcNow;
            logBatch.Add(new NotifLog
            {
                OutboxId = o.Id, ClienteId = o.ClienteId, Tipo = o.Tipo,
                PhoneNumber = o.PhoneNumber, Mensaje = string.Empty,
                Estado = NotifLogEstado.CANCELADO, IntentoNum = o.Intentos,
                ErrorDetalle = razon, RegistradoAt = DateTime.UtcNow,
            });
        }
        if (pendientes.Count > 0)
        {
            await _outboxRepo.UpdateRangeAsync(pendientes);
            await _logRepo.AddRangeAsync(logBatch);
            await _logRepo.SaveChangesAsync();
        }

        await _audit.LogAsync("Notificaciones", "NOTIF_MASIVA_CANCELADA",
            $"Cancelación masiva tipo={tipo} cantidad={pendientes.Count}",
            userId: actorId, userName: actorName, ip: ip);

        return Result<CancelMasivaResultDto>.Success(
            new CancelMasivaResultDto(pendientes.Count));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<List<Client>> LoadTargetClientsAsync(EnvioMasivoDto dto) =>
        dto.SegmentId.HasValue
            ? _segmentSvc.GetClientesFromSegmentAsync(dto.SegmentId.Value)
            : _clientRepo.GetAll()
                .Where(c => !c.IsDeleted && c.Status == ClientStatus.Activo)
                .ToListAsync();

    private async Task RegistrarOmitidoAsync(Client cliente, NotifType tipo)
    {
        await _logRepo.AddAsync(new NotifLog
        {
            OutboxId     = Guid.Empty,
            ClienteId    = cliente.Id,
            Tipo         = tipo,
            PhoneNumber  = cliente.PhoneMain,
            Mensaje      = string.Empty,
            Estado       = NotifLogEstado.OMITIDO,
            IntentoNum   = 0,
            ErrorDetalle = "OMITIDO_ANTISPAM: mensaje enviado en las últimas 24h",
            RegistradoAt = DateTime.UtcNow,
        });
    }
}
