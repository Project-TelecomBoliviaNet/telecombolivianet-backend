using TelecomBoliviaNet.Application.DTOs.Alerts;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface IInternalAlertService
{
    Task CreateForRoleAsync(string role, string type, string title, string message, string link);
    Task<IEnumerable<InternalAlertDto>> GetUnreadAsync(Guid userId, string role);
    Task MarkReadAsync(Guid alertId);
    Task MarkAllReadAsync(Guid userId, string role);
}
