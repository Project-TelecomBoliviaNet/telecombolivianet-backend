using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Clients;

public enum InvoiceType
{
    Mensualidad,
    Instalacion,
    Extraordinaria    // US-FAC-02
}

public enum InvoiceStatus
{
    // Existentes
    Pendiente,
    Pagada,
    Vencida,
    Anulada,
    // US-FAC-ESTADOS: nuevos estados
    Emitida,               // recién creada (antes de enviar recordatorio)
    Enviada,               // notificación WhatsApp enviada al cliente
    ParcialmentePagada     // pago parcial registrado
}

/// <summary>
/// Factura generada automáticamente cada mes o al registrar un cliente.
/// Una fila por mes por cliente + una fila de instalación.
/// </summary>
public class Invoice : Entity
{
    public Guid          ClientId  { get; set; }
    public Client?       Client    { get; set; }

    public InvoiceType   Type      { get; set; }
    public InvoiceStatus Status    { get; set; } = InvoiceStatus.Pendiente;

    public int           Year      { get; set; }
    public int           Month     { get; set; }  // 0 para instalación
    public decimal       Amount    { get; set; }

    public DateTime      IssuedAt  { get; set; } = DateTime.UtcNow;
    public DateTime      DueDate   { get; set; }

    public string?       Notes     { get; set; }
    public DateTime?     UpdatedAt { get; set; }

    // US-FAC-CORRELATIVO — número correlativo F-AAAA-NNNN
    public string?  InvoiceNumber { get; set; }

    // US-FAC-CREDITO — monto de crédito descontado al generar esta factura
    public decimal  CreditApplied { get; set; } = 0m;

    // US-FAC-ESTADOS — monto ya cobrado (para ParcialmentePagada)
    public decimal  AmountPaid    { get; set; } = 0m;

    // US-FAC-02 — facturas extraordinarias
    public bool     IsExtraordinary     { get; set; } = false;
    public string?  ExtraordinaryReason { get; set; }  // Reconexion, Equipo, VisitaTecnica, Otro

    // ── Soft Delete ─────────────────────────────────────────────────────────
    /// <summary>
    /// Facturas anuladas administrativamente. Se conservan para auditoría fiscal.
    /// Distinto de Status=Anulada: IsDeleted elimina lógicamente el registro completo.
    /// </summary>
    public bool      IsDeleted   { get; set; } = false;
    public DateTime? DeletedAt   { get; set; }
    public Guid?     DeletedById { get; set; }

    // Relación con pagos (una factura puede ser cubierta por un pago)
    public ICollection<PaymentInvoice> PaymentInvoices { get; set; } = new List<PaymentInvoice>();

    /// <summary>
    /// Verdadero si la factura tiene saldo pendiente de cobro.
    /// Centraliza la regla de negocio para evitar duplicar la condición en servicios.
    /// Nota: no mapear a columna de BD — EF Core ignora propiedades sin setter público,
    /// pero para queries LINQ traducidas a SQL usar la expresión directa.
    /// </summary>
    public bool IsUnpaid =>
        Status is InvoiceStatus.Pendiente or InvoiceStatus.Vencida or InvoiceStatus.ParcialmentePagada;
}
