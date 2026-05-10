namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// Contrato para envío de notificaciones WhatsApp.
/// La implementación real se inyecta desde Infrastructure.
/// En desarrollo puede usarse un stub que solo loguea.
/// </summary>
public interface IWhatsAppNotifier
{
    /// <summary>Envía un mensaje de texto al número especificado.</summary>
    Task SendTextAsync(string phoneNumber, string message);
}
