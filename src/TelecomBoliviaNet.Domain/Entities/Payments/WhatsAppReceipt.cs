using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Payments;

public enum ReceiptQueueStatus
{
    Pendiente,    // en cola, esperando revisión
    Aprobado,     // ya se registró el pago
    Rechazado,    // se rechazó y notificó al cliente
    NoCorresponde // foto de otra cosa, descartada
}

/// <summary>
/// Comprobante de pago recibido por WhatsApp que espera aprobación del admin (US-30).
/// Cuando el bot recibe una imagen de un cliente, se crea un registro aquí.
/// Al aprobar, se crea un Payment y este registro pasa a Aprobado.
/// </summary>
public class WhatsAppReceipt : Entity
{
    public Guid                ClientId     { get; set; }
    public Client?             Client       { get; set; }

    /// <summary>URL de la imagen del comprobante almacenada en el servidor.</summary>
    public string              ImageUrl     { get; set; } = string.Empty;

    /// <summary>Texto del mensaje enviado junto a la imagen (puede contener el monto).</summary>
    public string?             MessageText  { get; set; }

    /// <summary>Monto que el cliente declaró en el mensaje (si lo mencionó).</summary>
    public decimal?            DeclaredAmount { get; set; }

    public ReceiptQueueStatus  Status       { get; set; } = ReceiptQueueStatus.Pendiente;

    public DateTime            ReceivedAt   { get; set; } = DateTime.UtcNow;
    public DateTime?           ProcessedAt  { get; set; }

    /// <summary>ID del Payment creado al aprobar (null si pendiente o rechazado).</summary>
    public Guid?               PaymentId    { get; set; }

    /// <summary>Motivo del rechazo o descarte.</summary>
    public string?             RejectionNote { get; set; }
}
