namespace TelecomBoliviaNet.Application.DTOs.Payments;

// ── Listado centralizado (US-28) ──────────────────────────────────────────────

public record PaymentListItemDto(
    Guid      Id,
    Guid      ClientId,
    string    TbnCode,
    string    ClientName,
    decimal   Amount,
    string    Method,
    string?   Bank,
    DateTime  PaidAt,
    DateTime  RegisteredAt,
    string    RegisteredByName,
    bool      FromWhatsApp,
    string?   ReceiptImageUrl,
    string?   PhysicalReceiptNumber,
    IEnumerable<string> CoveredMonths
);

public record PaymentDetailDto(
    Guid      Id,
    Guid      ClientId,
    string    TbnCode,
    string    ClientName,
    string    ClientPhone,
    decimal   Amount,
    string    Method,
    string?   Bank,
    DateTime  PaidAt,
    DateTime  RegisteredAt,
    string    RegisteredByName,
    bool      FromWhatsApp,
    string?   ReceiptImageUrl,
    string?   PhysicalReceiptNumber,
    IEnumerable<string> CoveredMonths,
    bool      CanVoid,      // dentro de los 30 días
    bool      IsVoided
);

public record PaymentFilterDto(
    string?   Search      = null,
    string?   Method      = null,
    string?   Origin      = null,  // "Manual" | "WhatsApp"
    DateTime? From        = null,
    DateTime? To          = null,
    int       PageNumber  = 1,
    int       PageSize    = 25
);

// ── Anulación (US-31) ─────────────────────────────────────────────────────────

public record VoidPaymentDto(string Justification);

public record VoidPaymentResultDto(
    int     InvoicesReverted,
    string  Message
);

// ── Subida de imagen (US-29) ──────────────────────────────────────────────────

public record AttachReceiptDto(string ReceiptImageUrl);

// ── Cola de comprobantes WhatsApp (US-30) ─────────────────────────────────────

public record WhatsAppReceiptDto(
    Guid      Id,
    Guid      ClientId,
    string    TbnCode,
    string    ClientName,
    string    ClientPhone,
    string    ImageUrl,
    string?   MessageText,
    decimal?  DeclaredAmount,
    string    Status,
    DateTime  ReceivedAt
);

public record ApproveReceiptDto(
    decimal   Amount,
    string    Method,
    string?   Bank,
    DateTime  PaidAt,
    string?   PhysicalReceiptNumber,
    List<Guid> InvoiceIds
);

public record RejectReceiptDto(string Reason);

// ── Recepción de comprobante desde el chatbot (US-06 bot) ─────────────────────
// Enviado por el NestJS al recibir una imagen de pago por WhatsApp.
public record SubmitWhatsappReceiptDto(
    Guid?   ClientId,
    string  ImageUrl,
    string? MessageText,
    decimal? DeclaredAmount,
    string? OcrBank,
    string? OcrDate,
    string? OcrRawText,
    string  PhoneNumber
);

// ── Reporte de cobranza (US-32) ───────────────────────────────────────────────

public record CollectionReportDto(
    DateTime  From,
    DateTime  To,
    decimal   TotalCollected,
    int       TotalPayments,
    decimal   AveragePayment,
    decimal   CollectedCash,
    decimal   CollectedDeposit,
    decimal   CollectedQr,
    IEnumerable<CollectionByUserDto>    ByUser,
    IEnumerable<PaymentListItemDto>     Payments
);

public record CollectionByUserDto(
    string  UserName,
    int     Count,
    decimal Total
);

// ── Verificación de duplicados (US-35) ────────────────────────────────────────

public record DuplicateCheckResultDto(
    bool      IsPossibleDuplicate,
    Guid?     ExistingPaymentId,
    DateTime? ExistingPaidAt,
    decimal?  ExistingAmount,
    string?   ExistingMethod,
    string?   RegisteredByName
);

// ── Recordatorio día 7 (US-34) ────────────────────────────────────────────────

public record ReminderJobResultDto(
    int     Sent,
    int     Skipped,
    int     Errors,
    string  ExecutedAt
);

// ── M2: US-PAG-CREDITO ────────────────────────────────────────────────────────

public record RegisterPaymentDto(
    Guid       ClientId,
    decimal    Amount,
    string     Method,
    string?    Bank,
    DateTime   PaidAt,
    List<Guid> InvoiceIds,
    string?    PhysicalReceiptNumber
);

public record PaymentRegisteredDto(
    Guid    PaymentId,
    string  ReceiptNumber,
    decimal AmountPaid,
    decimal CreditGenerated,
    decimal CreditUsed,
    string  Message
);

public record ReembolsarCreditoDto(string Justificacion);

// ── M2: US-PAG-CAJA ───────────────────────────────────────────────────────────

public record CashCloseDto(
    Guid     Id,
    Guid     UserId,
    string   OperatorName,
    DateTime StartedAt,
    DateTime? ClosedAt,
    decimal  TotalAmount,
    int      PagosValidados,
    int      PagosRechazados,
    List<TelecomBoliviaNet.Domain.Entities.Payments.CashCloseChannelDetail> Detalle,
    bool     IsClosed,
    string?  PdfPath
);

// ── M2: US-PAG-06 · Reporte por operador ─────────────────────────────────────

public record CollectionReportByOperatorDto(
    DateTime  From,
    DateTime  To,
    decimal   TotalAmount,
    int       Count,
    string?   OperatorName,
    List<PaymentReportRowDto> Payments,
    List<OperatorDropdownDto> Operators
);

public record PaymentReportRowDto(
    Guid     Id,
    string   TbnCode,
    string   ClientName,
    decimal  Amount,
    string   Method,
    string?  Bank,
    DateTime PaidAt,
    string   OperatorName,
    string?  PhysicalReceiptNumber,
    bool     FromWhatsApp
);

public record OperatorDropdownDto(Guid Id, string Name);

// Add OperatorId to PaymentFilterDto via extended record
public record PaymentFilterWithOperatorDto(
    string?   Search      = null,
    string?   Method      = null,
    string?   Origin      = null,
    DateTime? From        = null,
    DateTime? To          = null,
    Guid?     OperatorId  = null,  // US-PAG-06
    int       PageNumber  = 1,
    int       PageSize    = 25
);
