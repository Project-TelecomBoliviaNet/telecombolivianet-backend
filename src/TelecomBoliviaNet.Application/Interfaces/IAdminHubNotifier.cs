namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// CORRECCIÓN: abstracción del hub SignalR para que Infrastructure no dependa de Presentation.
/// QrExpiryAlertJob (Infrastructure) lo inyecta sin referenciar AdminHub directamente.
/// La implementación concreta vive en Presentation y se registra en DI.
/// </summary>
public interface IAdminHubNotifier
{
    Task SendToAllAsync(string method, object payload);
}
