using TelecomBoliviaNet.Domain.Entities.Notifications;

namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// Contrato para insertar registros en notif_outbox dentro de la misma transacción
/// del evento de negocio. Garantiza consistencia transaccional (US-30, US-31, US-33, US-34).
/// </summary>
public interface INotifPublisher
{
    /// <summary>
    /// Publica una notificación al outbox.
    /// Si el tipo está desactivado en notif_config, no inserta ningún registro.
    /// Si el cliente no tiene teléfono, registra OMITIDO en notif_log.
    /// </summary>
    Task PublishAsync(
        NotifType tipo,
        Guid      clienteId,
        string?   phoneNumber,
        Dictionary<string, string> contexto,
        Guid?     referenciaId = null);
}
