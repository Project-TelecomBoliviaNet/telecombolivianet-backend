namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// Contrato para envío de emails transaccionales.
/// La implementación real (Resend) se inyecta desde Infrastructure.
/// Si la API Key no está configurada, la implementación entra en modo STUB (solo loguea).
/// </summary>
public interface IEmailService
{
    Task SendPasswordResetAsync(string toEmail, string toName, string resetLink);
    Task SendPasswordChangedNotificationAsync(string toEmail, string toName);
    Task SendAccountActivationAsync(string toEmail, string toName, string activationLink);
}
