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
///
/// CORRECCIONES aplicadas:
///   - Anti-spam ahora hace un único query batch (no N queries en loop)
///   - Contexto completo construido por NotifContextBuilderService antes de insertar
///   - AddRangeAsync + SaveChangesAsync para persistencia garantizada
///   - Logs OMITIDO usan OutboxId nullable (no Guid.Empty)
///   - CancelMasivaAsync usa UpdateRangeAsync + AddRangeAsync en una sola operación
/// </summary>
public class NotifEnvioService
{
    private readonly IGenericRepository<NotifConfig>         _configRepo;
    private readonly IGenericRepository<NotifOutbox>         _outboxRepo;
    private readonly IGenericRepository<NotifLog>            _logRepo;
    private readonly IGenericRepository<Client>              _clientRepo;
    private readonly NotifSegmentService                     _segmentSvc;
    private readonly NotifContextBuilderService              _contextBuilder;
    private readonly AuditService                            _audit;

    private const int AntiSpamWindowHours = 24;

    public NotifEnvioService(
        IGenericRepository<NotifConfig>  configRepo,
        IGenericRepository<NotifOutbox>  outboxRepo,
        IGenericRepository<NotifLog>     logRepo,
        IGenericRepository<Client>       clientRepo,
        NotifSegmentService              segmentSvc,
        NotifContextBuilderService       contextBuilder,
        AuditService                     audit)
    {
        _configRepo     = configRepo;
        _outboxRepo     = outboxRepo;
        _logRepo        = logRepo;
        _clientRepo     = clientRepo;
        _segmentSvc     = segmentSvc;
        _contextBuilder = contextBuilder;
        _audit          = audit;
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
        if (!clientes.Any())
            return Result<EnvioMasivoResultDto>.Success(new EnvioMasivoResultDto(0, 0, 0));

        // ── Anti-spam: UN solo query batch para todos los clientes ────────────
        var ventana24h   = DateTime.UtcNow.AddHours(-AntiSpamWindowHours);
        var clienteIds   = clientes.Select(c => c.Id).ToList();
        var yaRecibieroList = await _logRepo.GetAll()
            .Where(l => clienteIds.Contains(l.ClienteId)
                     && l.Tipo      == dto.Tipo
                     && l.Estado    == NotifLogEstado.ENVIADO
                     && l.RegistradoAt >= ventana24h)
            .Select(l => l.ClienteId)
            .Distinct()
            .ToListAsync();
        var yaRecibieron = new HashSet<Guid>(yaRecibieroList);

        var outboxBatch  = new List<NotifOutbox>();
        var omitidoLogs  = new List<NotifLog>();
        int sinTelefono  = 0;

        var enviarDesde = DateTime.UtcNow.AddSeconds(config.DelaySegundos);

        foreach (var cliente in clientes)
        {
            if (string.IsNullOrWhiteSpace(cliente.PhoneMain))
            {
                sinTelefono++;
                continue;
            }

            if (yaRecibieron.Contains(cliente.Id))
            {
                omitidoLogs.Add(new NotifLog
                {
                    // OutboxId nullable: sin outbox asociado para registros anti-spam
                    OutboxId     = null,
                    ClienteId    = cliente.Id,
                    Tipo         = dto.Tipo,
                    PhoneNumber  = cliente.PhoneMain,
                    Mensaje      = string.Empty,
                    Estado       = NotifLogEstado.OMITIDO,
                    IntentoNum   = 0,
                    ErrorDetalle = "OMITIDO_ANTISPAM: mensaje enviado en las últimas 24h",
                    RegistradoAt = DateTime.UtcNow,
                });
                continue;
            }

            // Construir contexto completo para que el worker no envíe variables vacías
            var contexto = await _contextBuilder.BuildAsync(cliente.Id);

            outboxBatch.Add(new NotifOutbox
            {
                Tipo         = dto.Tipo,
                ClienteId    = cliente.Id,
                PhoneNumber  = cliente.PhoneMain,
                Publicado    = false,
                Intentos     = 0,
                EnviarDesde  = enviarDesde,
                EstadoFinal  = null,
                CreadoAt     = DateTime.UtcNow,
                ContextoJson = JsonSerializer.Serialize(contexto),
            });
        }

        // ── Persistir en batch (una sola transacción) ─────────────────────────
        if (outboxBatch.Count > 0)
        {
            await _outboxRepo.AddRangeAsync(outboxBatch);
            await _outboxRepo.SaveChangesAsync();
        }

        if (omitidoLogs.Count > 0)
        {
            await _logRepo.AddRangeAsync(omitidoLogs);
            await _logRepo.SaveChangesAsync();
        }

        await _audit.LogAsync("Notificaciones", "NOTIF_ENVIO_MASIVO",
            $"Envío masivo tipo={dto.Tipo} programados={outboxBatch.Count} omitidos={omitidoLogs.Count} sinTelefono={sinTelefono}",
            userId: actorId, userName: actorName, ip: ip);

        return Result<EnvioMasivoResultDto>.Success(
            new EnvioMasivoResultDto(outboxBatch.Count, omitidoLogs.Count, sinTelefono));
    }

    // ── US-39 · Cancelación individual ────────────────────────────────────────

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

    // ── US-39 · Cancelación masiva por tipo ───────────────────────────────────

    public async Task<Result<CancelMasivaResultDto>> CancelMasivaAsync(
        NotifType tipo, string? razon, Guid actorId, string actorName, string ip)
    {
        var pendientes = await _outboxRepo.GetAll()
            .Where(o => o.Tipo == tipo && o.EstadoFinal == null && !o.Publicado)
            .ToListAsync();

        if (!pendientes.Any())
            return Result<CancelMasivaResultDto>.Success(new CancelMasivaResultDto(0));

        var logBatch = new List<NotifLog>(pendientes.Count);
        foreach (var o in pendientes)
        {
            o.EstadoFinal = NotifEstadoFinal.CANCELADO;
            o.Publicado   = true;
            o.ProcesadoAt = DateTime.UtcNow;
            logBatch.Add(new NotifLog
            {
                OutboxId     = o.Id,
                ClienteId    = o.ClienteId,
                Tipo         = o.Tipo,
                PhoneNumber  = o.PhoneNumber,
                Mensaje      = string.Empty,
                Estado       = NotifLogEstado.CANCELADO,
                IntentoNum   = o.Intentos,
                ErrorDetalle = razon,
                RegistradoAt = DateTime.UtcNow,
            });
        }

        await _outboxRepo.UpdateRangeAsync(pendientes);
        await _logRepo.AddRangeAsync(logBatch);
        await _logRepo.SaveChangesAsync();

        await _audit.LogAsync("Notificaciones", "NOTIF_MASIVA_CANCELADA",
            $"Cancelación masiva tipo={tipo} cantidad={pendientes.Count}",
            userId: actorId, userName: actorName, ip: ip);

        return Result<CancelMasivaResultDto>.Success(new CancelMasivaResultDto(pendientes.Count));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<List<Client>> LoadTargetClientsAsync(EnvioMasivoDto dto) =>
        dto.SegmentId.HasValue
            ? _segmentSvc.GetClientesFromSegmentAsync(dto.SegmentId.Value)
            : _clientRepo.GetAll()
                .Where(c => !c.IsDeleted && c.Status == ClientStatus.Activo)
                .ToListAsync();
}
