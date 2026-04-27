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
        var query = _repo.GetAll().AsQueryable();

        if (filter.UserId.HasValue)
            query = query.Where(a => a.UserId == filter.UserId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(a => a.Action.Contains(filter.Action));

        if (filter.From.HasValue)
            query = query.Where(a => a.CreatedAt >= filter.From.Value);

        if (filter.To.HasValue)
            query = query.Where(a => a.CreatedAt <= filter.To.Value);

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

    private static AuditLogDto MapToDto(AuditLog a) => new(
        Id:           a.Id,
        UserId:       a.UserId,
        UserName:     a.UserName,
        Module:       a.Module,
        Action:       a.Action,
        Description:  a.Description,
        IpAddress:    a.IpAddress,
        PreviousData: a.PreviousData,
        NewData:      a.NewData,
        CreatedAt:    a.CreatedAt
    );
}
