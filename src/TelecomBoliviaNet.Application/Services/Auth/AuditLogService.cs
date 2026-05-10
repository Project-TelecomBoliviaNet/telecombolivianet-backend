using System.Text;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Auth;

public class AuditLogService
{
    private readonly IGenericRepository<AuditLog> _repo;

    public AuditLogService(IGenericRepository<AuditLog> repo)
    {
        _repo = repo;
    }

    // ── US-09 · Consultar audit log con filtros ───────────────────────────────

    public async Task<PagedResult<AuditLogDto>> GetAsync(AuditLogFilterDto filter)
    {
        var query = _repo.GetAllReadOnly().AsQueryable();

        if (filter.UserId.HasValue)
            query = query.Where(a => a.UserId == filter.UserId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(a => a.Action == filter.Action);

        if (!string.IsNullOrWhiteSpace(filter.Module))
            query = query.Where(a => a.Module == filter.Module);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(a =>
                a.Description.ToLower().Contains(s) ||
                a.UserName.ToLower().Contains(s));
        }

        if (filter.From.HasValue)
            query = query.Where(a => a.CreatedAt >= filter.From.Value);

        // Fin de día inclusivo: filtra hasta las 23:59:59 del día seleccionado
        if (filter.To.HasValue)
            query = query.Where(a => a.CreatedAt < filter.To.Value.Date.AddDays(1));

        query = query.OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PagedResult<AuditLogDto>(
            items.Select(MapToDto),
            total,
            filter.PageNumber,
            filter.PageSize);
    }

    // ── Exportar a CSV ────────────────────────────────────────────────────────

    public async Task<byte[]> ExportCsvAsync(AuditLogFilterDto filter)
    {
        var all = filter with { PageNumber = 1, PageSize = 10_000 };
        var result = await GetAsync(all);

        var sb = new StringBuilder();
        sb.AppendLine("Fecha,Usuario,Módulo,Acción,Descripción,IP");
        foreach (var log in result.Items)
        {
            var desc = log.Description?.Replace("\"", "\"\"") ?? string.Empty;
            sb.AppendLine($"\"{log.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{log.UserName}\",\"{log.Module}\",\"{log.Action}\",\"{desc}\",\"{log.IpAddress}\"");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static AuditLogDto MapToDto(AuditLog a) => new(
        Id:             a.Id,
        UserId:         a.UserId,
        UserName:       a.UserName,
        Module:         a.Module,
        Action:         a.Action,
        Description:    a.Description,
        IpAddress:      a.IpAddress,
        UserAgent:      a.UserAgent,
        PreviousData:   a.PreviousData,
        NewData:        a.NewData,
        IntegrityHash:  a.IntegrityHash,
        CreatedAt:      a.CreatedAt
    );
}
