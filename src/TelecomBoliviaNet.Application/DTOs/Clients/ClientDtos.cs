using TelecomBoliviaNet.Application.DTOs.Plans;

namespace TelecomBoliviaNet.Application.DTOs.Clients;

// ── Registro ──────────────────────────────────────────────────────────────────

public record RegisterClientDto(
    // Datos personales
    string  FullName,
    string  IdentityCard,
    string  PhoneMain,
    string? PhoneSecondary,

    // Ubicación
    string  Zone,
    string? Street,
    string? LocationRef,
    decimal? GpsLatitude,
    decimal? GpsLongitude,

    // Instalación
    string   WinboxNumber,
    DateTime InstallationDate,
    Guid     InstalledByUserId,
    Guid     PlanId,
    bool     HasTvCable,
    string?  OnuSerialNumber,

    // Pago inicial
    decimal InstallationCost,
    bool    PaidInstallation,
    bool    PaidFirstMonth,
    string? PaymentMethod,
    string? Bank,
    string? PhysicalReceiptNumber
);

// ── Edición ───────────────────────────────────────────────────────────────────

public record UpdateClientDto(
    string  FullName,
    string  IdentityCard,
    string  PhoneMain,
    string? PhoneSecondary,
    string  Zone,
    string? Street,
    string? LocationRef,
    decimal? GpsLatitude,
    decimal? GpsLongitude,
    string  WinboxNumber,
    Guid    PlanId,
    bool    HasTvCable,
    string? OnuSerialNumber
);

// ── Respuestas ────────────────────────────────────────────────────────────────

public record ClientListItemDto(
    Guid    Id,
    string  TbnCode,
    string  FullName,
    string  Zone,
    string  PhoneMain,
    string  PlanName,
    bool    HasTvCable,
    string  Status,
    decimal TotalDebt,
    int     PendingMonths
);

public record ClientDetailDto(
    Guid     Id,
    string   TbnCode,
    string   FullName,
    string   IdentityCard,
    string   PhoneMain,
    string?  PhoneSecondary,
    string   Zone,
    string?  Street,
    string?  LocationRef,
    decimal? GpsLatitude,
    decimal? GpsLongitude,
    string   WinboxNumber,
    DateTime InstallationDate,
    Guid     InstalledByUserId,
    string   InstalledByName,
    PlanDto  Plan,
    bool     HasTvCable,
    string?  OnuSerialNumber,
    string   Status,
    DateTime? SuspendedAt,
    DateTime? CancelledAt,
    decimal  TotalDebt,
    int      PendingMonths,
    DateTime? LastPaymentDate,
    bool     InstallationPaid,
    DateTime CreatedAt,
    // M5 / M2
    string?  Email,           // US-CLI-01
    decimal  CreditBalance,   // US-PAG-CREDITO
    int      AttachmentCount  // US-CLI-ADJUNTOS: cantidad de docs adjuntos
);

// ── Facturas ──────────────────────────────────────────────────────────────────

public record InvoiceDto(
    Guid     Id,
    string   Type,
    string   Status,
    int      Year,
    int      Month,
    decimal  Amount,
    DateTime IssuedAt,
    DateTime DueDate,
    string?  Notes
);

public record InvoiceGridDto(
    IEnumerable<InvoiceDto> Invoices,
    IEnumerable<PaymentDto> Payments,
    decimal TotalDebt,
    int PendingMonths,
    DateTime? LastPaymentDate
);

// ── Pagos ─────────────────────────────────────────────────────────────────────

public record RegisterPaymentDto(
    Guid       ClientId,
    decimal    Amount,
    string     Method,
    string?    Bank,
    DateTime   PaidAt,
    string?    PhysicalReceiptNumber,
    List<Guid> InvoiceIds,
    bool?      ConfirmedDuplicate = null   // US-35: si true, se anota en audit log
);

public record PaymentDto(
    Guid     Id,
    decimal  Amount,
    string   Method,
    string?  Bank,
    DateTime PaidAt,
    DateTime RegisteredAt,
    string   RegisteredByName,
    bool     FromWhatsApp,
    string?  ReceiptImageUrl,
    bool     CanVoid,
    bool     IsVoided,
    IEnumerable<string> CoveredMonths
);

// ── Filtros ───────────────────────────────────────────────────────────────────

public record ClientFilterDto(
    string?  Search      = null,
    string?  Status      = null,
    Guid?    PlanId      = null,
    string?  DebtFilter  = null,   // "all" | "paid" | "debt"
    string?  SortBy      = null,   // "name" | "code" | "zone" | "debt"
    int      PageNumber  = 1,
    int      PageSize    = 20
);

// ════════════════════════════════════════════════════════════════════════════
// M5: US-CLI-ADJUNTOS · Adjuntos del cliente
// ════════════════════════════════════════════════════════════════════════════

public record ClientAttachmentDto(
    Guid     Id,
    string   FileName,
    string   TipoDoc,
    string   ContentType,
    long     FileSizeBytes,
    string?  Descripcion,
    string   StoragePath,       // URL pública o ruta relativa para descargar
    string   SubidoPorNombre,
    DateTime SubidoAt
);

public record UploadAttachmentDto(
    string   TipoDoc,
    string?  Descripcion
    // El archivo viene como IFormFile en el controller
);

// ════════════════════════════════════════════════════════════════════════════
// M5: US-CLI-HISTORIAL · Historial de actividad del cliente
// ════════════════════════════════════════════════════════════════════════════

public record ClientActivityItemDto(
    Guid      Id,
    string    Tipo,          // Pago | Factura | Ticket | Estado | Notif | Admin
    string    Descripcion,
    string    Actor,         // "Sistema" o nombre del usuario
    DateTime  OcurridoAt,
    string?   Referencia,    // ID de pago, ticket, factura según tipo
    string?   Detalle        // JSON de datos adicionales (opcional)
);

public record ClientHistorialDto(
    List<ClientActivityItemDto> Items,
    int Total,
    int Page,
    int PageSize
);

// ════════════════════════════════════════════════════════════════════════════
// M5: US-CLI-01 · Email en ClientDetailDto (ya agregado el campo)
// US-CLI-BUSQUEDA · Búsqueda avanzada
// ════════════════════════════════════════════════════════════════════════════

public record ClientSearchDto(
    string? Query,           // busca en nombre, TBN, CI, teléfono, email
    string? Zone,
    string? Status,
    string? PlanId,
    bool?   HasDebt,
    bool?   HasEmail,        // US-CLI-01: filtrar por tiene email
    int     Page     = 1,
    int     PageSize = 25
);

public record ClientSearchResultDto(
    List<ClientListItemDto> Items,
    int    Total,
    int    Page,
    int    PageSize,
    string? AppliedQuery
);
