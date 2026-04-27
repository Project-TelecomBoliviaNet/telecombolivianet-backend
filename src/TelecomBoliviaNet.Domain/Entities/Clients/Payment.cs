using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Clients;

public enum PaymentMethod
{
    Efectivo,
    DepositoBancario,
    QR
}

/// <summary>
/// Comprobante de pago registrado por el admin o técnico.
/// Un pago puede cubrir una o varias facturas.
/// </summary>
public class Payment : Entity
{
    public Guid    ClientId        { get; set; }
    public Client? Client          { get; set; }

    public decimal       Amount         { get; set; }
    public PaymentMethod Method         { get; set; }
    public string?       Bank           { get; set; }
    public string?       ReceiptImageUrl { get; set; }
    public string?       PhysicalReceiptNumber { get; set; }

    public DateTime      PaidAt         { get; set; }
    public DateTime      RegisteredAt   { get; set; } = DateTime.UtcNow;

    public Guid          RegisteredByUserId { get; set; }
    public UserSystem?   RegisteredBy       { get; set; }

    /// <summary>true si vino desde la bandeja de comprobantes de WhatsApp</summary>
    public bool          FromWhatsApp   { get; set; }

    public ICollection<PaymentInvoice> PaymentInvoices { get; set; } = new List<PaymentInvoice>();

    // US-31: campos de anulación — preserva los datos originales intactos
    public bool      IsVoided         { get; set; } = false;
    public string?   VoidJustification { get; set; }
    public DateTime? VoidedAt          { get; set; }
    public Guid?     VoidedByUserId    { get; set; }

    // ── Soft Delete ─────────────────────────────────────────────────────────
    /// <summary>
    /// Eliminación lógica de pagos. Los pagos nunca se borran físicamente
    /// para mantener integridad contable e historial de transacciones.
    /// </summary>
    public bool      IsDeleted   { get; set; } = false;
    public DateTime? DeletedAt   { get; set; }
    public Guid?     DeletedById { get; set; }
}

/// <summary>
/// Tabla de unión entre Payment e Invoice (un pago puede cubrir múltiples facturas).
/// </summary>
public class PaymentInvoice : Entity
{
    public Guid     PaymentId { get; set; }
    public Payment? Payment   { get; set; }

    public Guid     InvoiceId { get; set; }
    public Invoice? Invoice   { get; set; }
}
