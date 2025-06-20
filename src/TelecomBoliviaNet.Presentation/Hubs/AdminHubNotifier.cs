using Microsoft.AspNetCore.SignalR;
using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Presentation.Hubs;

/// <summary>
/// Implementación de IAdminHubNotifier que delega a IHubContext&lt;AdminHub&gt;.
/// Vive en Presentation para que Infrastructure no dependa de ella.
/// </summary>
public sealed class AdminHubNotifier : IAdminHubNotifier
{
    private readonly IHubContext<AdminHub> _hub;
    public AdminHubNotifier(IHubContext<AdminHub> hub) => _hub = hub;
    public Task SendToAllAsync(string method, object payload)
        => _hub.Clients.All.SendAsync(method, payload);
}
