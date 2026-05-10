using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Clients;

/// <summary>
/// QR de pago asociado a un cliente.
/// El administrador sube la imagen del QR; el chatbot la sirve al cliente.
/// Se registra fecha de expiración y se alerta al admin cuando está próximo a vencer.
/// </summary>
public class ClientQr : Entity
{
    public Guid     ClientId    { get; set; }
    public Client?  Client      { get; set; }

    /// <summary>URL de la imagen del QR (almacenada en wwwroot/uploads/qr/).</summary>
    public string   ImageUrl    { get; set; } = string.Empty;

    /// <summary>Fecha de expiración del QR. Null = no expira.</summary>
    public DateTime? ExpiresAt  { get; set; }

    /// <summary>true si el QR está activo y vigente.</summary>
    public bool     IsActive    { get; set; } = true;

    /// <summary>Indica si ya se envió la alerta de vencimiento próximo al admin.</summary>
    public bool     AlertSent   { get; set; } = false;

    public DateTime UploadedAt  { get; set; } = DateTime.UtcNow;
    public Guid     UploadedById { get; set; }

    /// <summary>Motivo de desactivación (reemplazado, expirado, etc.).</summary>
    public string?  DeactivatedReason { get; set; }
}
