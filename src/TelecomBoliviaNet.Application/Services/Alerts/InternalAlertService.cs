using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Alerts;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Alerts;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Alerts;

public class InternalAlertService : IInternalAlertService
{
    private readonly IGenericRepository<InternalAlert> _repo;

    public InternalAlertService(IGenericRepository<InternalAlert> repo)
        => _repo = repo;

    public async Task CreateForRoleAsync(
        string role, string type, string title, string message, string link)
    {
        await _repo.AddAsync(new InternalAlert
        {
            ForRole   = role,
            Type      = type,
            Title     = title,
            Message   = message,
            Link      = link,
            CreatedAt = DateTime.UtcNow,
        });
        await _repo.SaveChangesAsync();
    }

    public async Task<IEnumerable<InternalAlertDto>> GetUnreadAsync(Guid userId, string role)
    {
        return await _repo.GetAllReadOnly()
            .Where(a => !a.IsRead && (a.ForRole == role || a.ForUserId == userId))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new InternalAlertDto(a.Id, a.Type, a.Title, a.Message, a.Link, a.CreatedAt))
            .ToListAsync();
    }

    public async Task MarkReadAsync(Guid alertId)
    {
        var alert = await _repo.GetByIdAsync(alertId);
        if (alert is null) return;
        alert.IsRead = true;
        await _repo.UpdateAsync(alert);
    }

    public async Task MarkAllReadAsync(Guid userId, string role)
    {
        var alerts = await _repo.GetAll()
            .Where(a => !a.IsRead && (a.ForRole == role || a.ForUserId == userId))
            .ToListAsync();
        foreach (var a in alerts) a.IsRead = true;
        if (alerts.Count > 0)
            await _repo.SaveChangesAsync();
    }
}
