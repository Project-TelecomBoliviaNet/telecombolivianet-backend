using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Notifications;

/// <summary>
/// SRP: solo consulta el historial de notificaciones enviadas por cliente.
/// US-36.
/// Extraído de NotifConfigService (Problema #6).
/// </summary>
public class NotifHistorialService
{
    private readonly IGenericRepository<NotifLog>    _logRepo;
    private readonly IGenericRepository<NotifOutbox> _outboxRepo;

    public NotifHistorialService(
        IGenericRepository<NotifLog>    logRepo,
        IGenericRepository<NotifOutbox> outboxRepo)
    {
        _logRepo    = logRepo;
        _outboxRepo = outboxRepo;
    }

    public async Task<NotifLogPageDto> GetHistorialClienteAsync(
        Guid clienteId, int page, int pageSize,
        string? tipoFilter, DateTime? desde, DateTime? hasta)
    {
        var query = _logRepo.GetAll().Where(l => l.ClienteId == clienteId);

        if (!string.IsNullOrWhiteSpace(tipoFilter)
            && Enum.TryParse<NotifType>(tipoFilter, out var tipo))
            query = query.Where(l => l.Tipo == tipo);

        if (desde.HasValue) query = query.Where(l => l.RegistradoAt >= desde.Value);
        if (hasta.HasValue) query = query.Where(l => l.RegistradoAt <= hasta.Value);

        var total = await query.CountAsync();
        var logs  = await query
            .OrderByDescending(l => l.RegistradoAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var outboxIds = logs
            .Select(l => l.OutboxId)
            .Where(id => id != Guid.Empty)
            .ToList();

        var outboxMap = await _outboxRepo.GetAll()
            .Where(o => outboxIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.EnviarDesde);

        var items = logs.Select(l => new NotifLogItemDto(
            l.Id, l.OutboxId,
            l.Tipo, l.Tipo.ToString(),
            l.Estado.ToString(),
            l.PhoneNumber, l.Mensaje,
            l.IntentoNum, l.ErrorDetalle,
            l.RegistradoAt,
            outboxMap.TryGetValue(l.OutboxId, out var env) ? env : null
        )).ToList();

        return new NotifLogPageDto(items, total, page, pageSize);
    }
}
