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
/// SRP: gestiona segmentos de destinatarios para envíos masivos.
/// US-NOT-02.
/// Extraído de NotifConfigService (Problema #6).
/// </summary>
public class NotifSegmentService
{
    private readonly IGenericRepository<NotifSegment> _repo;
    private readonly IGenericRepository<Client>       _clientRepo;
    private readonly IGenericRepository<Invoice>      _invoiceRepo;
    private readonly AuditService                     _audit;

    public NotifSegmentService(
        IGenericRepository<NotifSegment> repo,
        IGenericRepository<Client>       clientRepo,
        IGenericRepository<Invoice>      invoiceRepo,
        AuditService                     audit)
    {
        _repo        = repo;
        _clientRepo  = clientRepo;
        _invoiceRepo = invoiceRepo;
        _audit       = audit;
    }

    public async Task<List<NotifSegmentDto>> GetSegmentsAsync()
    {
        var list = await _repo.GetAll().OrderBy(s => s.Nombre).ToListAsync();
        return list.Select(s => NotifShared.ToSegmentDto(s, null)).ToList();
    }

    public async Task<Result<NotifSegmentDto>> GetSegmentByIdAsync(Guid id)
    {
        var seg = await _repo.GetByIdAsync(id);
        if (seg is null) return Result<NotifSegmentDto>.Failure("Segmento no encontrado.");
        var preview = await EvaluateCountAsync(seg);
        return Result<NotifSegmentDto>.Success(NotifShared.ToSegmentDto(seg, preview));
    }

    public async Task<Result<NotifSegmentDto>> CreateAsync(
        CreateOrUpdateSegmentDto dto, Guid actorId, string actorName, string ip)
    {
        if (!dto.Reglas.Any() || dto.Reglas.All(g => !g.Condiciones.Any()))
            return Result<NotifSegmentDto>.Failure("El segmento debe tener al menos 1 condición.");

        if (await _repo.GetAll().AnyAsync(s => s.Nombre == dto.Nombre))
            return Result<NotifSegmentDto>.Failure($"Ya existe un segmento con el nombre '{dto.Nombre}'.");

        var seg = new NotifSegment
        {
            Nombre      = dto.Nombre,
            Descripcion = dto.Descripcion,
            ReglasJson  = JsonSerializer.Serialize(dto.Reglas),
            CreadoAt    = DateTime.UtcNow,
            CreadoPorId = actorId,
        };
        await _repo.AddAsync(seg);

        await _audit.LogAsync("Notificaciones", "NOTIF_SEGMENT_CREATED",
            $"Segmento creado: {dto.Nombre}",
            userId: actorId, userName: actorName, ip: ip);

        var preview = await EvaluateCountAsync(seg);
        return Result<NotifSegmentDto>.Success(NotifShared.ToSegmentDto(seg, preview));
    }

    public async Task<Result<NotifSegmentDto>> UpdateAsync(
        Guid id, CreateOrUpdateSegmentDto dto, Guid actorId, string actorName, string ip)
    {
        var seg = await _repo.GetByIdAsync(id);
        if (seg is null) return Result<NotifSegmentDto>.Failure("Segmento no encontrado.");

        if (!dto.Reglas.Any() || dto.Reglas.All(g => !g.Condiciones.Any()))
            return Result<NotifSegmentDto>.Failure("El segmento debe tener al menos 1 condición.");

        if (await _repo.GetAll().AnyAsync(s => s.Nombre == dto.Nombre && s.Id != id))
            return Result<NotifSegmentDto>.Failure($"Ya existe un segmento con el nombre '{dto.Nombre}'.");

        var prev        = seg.ReglasJson;
        seg.Nombre      = dto.Nombre;
        seg.Descripcion = dto.Descripcion;
        seg.ReglasJson  = JsonSerializer.Serialize(dto.Reglas);
        seg.ActualizadoAt    = DateTime.UtcNow;
        seg.ActualizadoPorId = actorId;
        await _repo.UpdateAsync(seg);

        await _audit.LogAsync("Notificaciones", "NOTIF_SEGMENT_UPDATED",
            $"Segmento actualizado: {dto.Nombre}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: prev, newData: seg.ReglasJson);

        var preview = await EvaluateCountAsync(seg);
        return Result<NotifSegmentDto>.Success(NotifShared.ToSegmentDto(seg, preview));
    }

    public async Task<r> DeleteAsync(Guid id, Guid actorId, string actorName, string ip)
    {
        var seg = await _repo.GetByIdAsync(id);
        if (seg is null) return Result.Failure("Segmento no encontrado.");

        await _repo.DeleteAsync(seg.Id);
        await _audit.LogAsync("Notificaciones", "NOTIF_SEGMENT_DELETED",
            $"Segmento eliminado: {seg.Nombre}",
            userId: actorId, userName: actorName, ip: ip);

        return Result.Success();
    }

    public async Task<SegmentPreviewDto> PreviewAsync(CreateOrUpdateSegmentDto dto)
    {
        var temp  = new NotifSegment { ReglasJson = JsonSerializer.Serialize(dto.Reglas) };
        var count = await EvaluateCountAsync(temp);
        return new SegmentPreviewDto(count);
    }

    // ── Expuesto internamente para NotifEnvioService ──────────────────────────

    public async Task<List<Client>> GetClientesFromSegmentAsync(Guid segmentId)
    {
        var seg = await _repo.GetByIdAsync(segmentId);
        return seg is null ? new() : await GetClientesAsync(seg);
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private async Task<int> EvaluateCountAsync(NotifSegment seg)
    {
        var clientes = await LoadClientesConFacturasAsync();
        return clientes.Count(pair =>
        {
            var grupos = DeserializeReglas(seg.ReglasJson);
            return grupos.Any(g => g.Condiciones.All(c =>
                NotifShared.EvaluaCondicion(pair.Client, pair.Invoices, c)));
        });
    }

    private async Task<List<Client>> GetClientesAsync(NotifSegment seg)
    {
        var grupos   = DeserializeReglas(seg.ReglasJson);
        var clientes = await LoadClientesConFacturasAsync();

        return clientes
            .Where(pair => grupos.Any(g =>
                g.Condiciones.All(c => NotifShared.EvaluaCondicion(pair.Client, pair.Invoices, c))))
            .Select(pair => pair.Client)
            .ToList();
    }

    private async Task<List<(Client Client, List<Invoice> Invoices)>> LoadClientesConFacturasAsync()
    {
        var clientes = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Where(c => !c.IsDeleted)
            .ToListAsync();

        var clientIds = clientes.Select(c => c.Id).ToList();
        var invoices  = await _invoiceRepo.GetAll()
            .Where(i => clientIds.Contains(i.ClientId)
                     && (i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida))
            .ToListAsync();

        var invMap = invoices
            .GroupBy(i => i.ClientId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return clientes
            .Select(c => (c, invMap.TryGetValue(c.Id, out var inv) ? inv : new List<Invoice>()))
            .ToList();
    }

    private static List<SegmentConditionGroup> DeserializeReglas(string json)
        => string.IsNullOrEmpty(json)
            ? new()
            : JsonSerializer.Deserialize<List<SegmentConditionGroup>>(json) ?? new();
}
