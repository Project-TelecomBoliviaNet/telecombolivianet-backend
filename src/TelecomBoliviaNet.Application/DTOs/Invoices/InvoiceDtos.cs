namespace TelecomBoliviaNet.Application.DTOs.Invoices;

// ── Factura individual ────────────────────────────────────────────────────────

public record InvoiceDetailDto(
    Guid      Id,
    Guid      ClientId,
    string    TbnCode,
    string    ClientName,
    string    PlanName,
    string    Type,
    string    Status,
    int       Year,
    int       Month,
    decimal   Amount,
    DateTime  IssuedAt,
    DateTime  DueDate,
    string?   Notes,
    DateTime? UpdatedAt,
    string?   PaymentMethod,
    string?   PaymentBank,
    DateTime? PaidAt,
    string?   PaidByName
);

// ── Estadísticas del mes ──────────────────────────────────────────────────────

public record InvoiceMonthStatsDto(
    int     Year,
    int     Month,
    decimal TotalBilled,
    decimal TotalCollected,
    decimal TotalPending,
    int     CountBilled,
    int     CountCollected,
    int     CountPending,
    int     CountOverdue,
    decimal CollectionRate
);

// ── Estadísticas por rango de fechas personalizado ────────────────────────────

public record InvoiceRangeStatsDto(
    DateTime DateFrom,
    DateTime DateTo,
    decimal  TotalBilled,
    decimal  TotalCollected,
    decimal  TotalPending,
    int      CountBilled,
    int      CountCollected,
    int      CountPending,
    int      CountOverdue,
    decimal  CollectionRate
);

// ── Filtros ───────────────────────────────────────────────────────────────────

public record InvoiceFilterDto(
    int?      Year       = null,
    int?      Month      = null,
    string?   Status     = null,
    string?   Search     = null,
    DateTime? DateFrom   = null,
    DateTime? DateTo     = null,
    int       PageNumber = 1,
    int       PageSize   = 25
);

// ── Resultado del job de facturación ─────────────────────────────────────────

public record BillingJobResultDto(
    int    Generated,
    int    Skipped,
    int    ExcludedBaja,
    int    Errors,
    string Period
);

public record BillingPreviewDto(
    int     ToGenerate,     // clientes que recibirían nueva factura
    int     AlreadyBilled,  // ya tienen factura para ese período
    int     ExcludedBaja,   // clientes dados de baja
    int     WithErrors,     // clientes sin plan asignado
    decimal EstimatedTotal, // suma aproximada de mensualidades
    string  Period
);

// ── Ejecución manual del job (US-26) ─────────────────────────────────────────

public record ManualBillingDto(int Year, int Month);

// ── Ítem de listado de facturas (US-23) ───────────────────────────────────────

public record InvoiceListItemDto(
    Guid     Id,
    string   InvoiceNumber,   // US-FAC-CORRELATIVO
    Guid     ClientId,
    string   TbnCode,
    string   ClientName,
    string   Type,
    string   Status,
    int      Year,
    int      Month,
    decimal  Amount,
    decimal  AmountPaid,      // US-FAC-ESTADOS
    decimal  CreditApplied,   // US-FAC-CREDITO
    DateTime IssuedAt,
    DateTime DueDate,
    DateTime? UpdatedAt,
    string?  Notes,
    bool     IsExtraordinary,          // US-FAC-02
    string?  ExtraordinaryReason       // US-FAC-02
);

// M3 backwards compat alias (usado en queries existentes)
public record InvoiceLegacyListItemDto(
    Guid      Id,
    string    TbnCode,
    string    ClientName,
    string    PlanName,
    decimal   Amount,
    string    Type,
    string    Status,
    int       Year,
    int       Month,
    DateTime  IssuedAt,
    DateTime  DueDate,
    string?   Notes,
    Guid?     PaymentId,
    // FASE 1.4: campos adicionales para exportación completa
    string?   InvoiceNumber,
    string    Zone,
    decimal   AmountPaid,
    DateTime? PaidAt,
    int       DaysOverdue
);

// ── Anulación ─────────────────────────────────────────────────────────────────

public record VoidInvoiceDto(string Justification);

// ── Reporte anual (US-27) ─────────────────────────────────────────────────────

public record AnnualReportRowDto(
    Guid    ClientId,
    string  TbnCode,
    string  ClientName,
    string  Zone,
    string  PlanName,
    IEnumerable<AnnualReportCellDto> Cells
);

public record AnnualReportCellDto(
    int       Month,
    string    Label,
    string    Status,
    decimal   Amount,
    string?   PaymentMethod,
    DateTime? PaidAt,
    Guid?     InvoiceId
);

public record AnnualReportFilterDto(
    int     Year,
    string? Zone       = null,
    Guid?   PlanId     = null,
    string? DebtFilter = null,
    string? SortBy     = null
);

// ── DTO especializado para exportación (Excel/PDF) ────────────────────────────
// Usa PaidDate como string formateado para facilitar la generación de celdas.

public record AnnualReportExportDto(
    int                               Year,
    IEnumerable<AnnualReportExportRowDto> Rows
);

public record AnnualReportExportRowDto(
    string TbnCode,
    string FullName,
    string Zone,
    string PlanName,
    IList<AnnualReportExportCellDto> Cells
);

public record AnnualReportExportCellDto(
    string  Status,
    decimal Amount,
    string? PaidDate,       // formateado "dd/MM/yyyy" o null
    string? PaymentMethod
);

// ── M3: US-FAC-02 · Facturas extraordinarias ─────────────────────────────────

public record CreateExtraordinaryInvoiceDto(
    Guid     ClientId,
    decimal  Amount,
    string   Motivo,   // Reconexion | Equipo | VisitaTecnica | Instalacion | Otro
    DateTime DueDate,
    string?  Notes
);

// ── M3: US-FAC-ESTADOS · Transición de estado ─────────────────────────────────

public record TransicionEstadoDto(string NuevoEstado);

public record MarcarEnviadasDto(int Year, int Month);

public record MarcarEnviadasResultDto(int Count, string Period);

// ── M3: US-FAC-CREDITO ────────────────────────────────────────────────────────

public record AplicarCreditoResultDto(decimal Aplicado, decimal NuevoSaldoCliente);

// ── Cobranza: factura pendiente resumida ──────────────────────────────────────

public record InvoicePendienteDto(
    Guid     Id,
    string   InvoiceNumber,
    string   Status,
    int      Year,
    int      Month,
    decimal  Amount,
    DateTime DueDate,
    string?  Notes);

// ── Cobranza: cliente con deuda pendiente ─────────────────────────────────────

public record DeudorDto(
    Guid     ClientId,
    string   TbnCode,
    string   FullName,
    string   Zone,
    string   PlanName,
    int      MesesPendientes,
    decimal  TotalDeuda,
    DateTime UltimoVencimiento,
    IEnumerable<InvoicePendienteDto> Facturas);

// ── M3: US-FAC-CORRELATIVO · Stats extendidas ─────────────────────────────────

public record InvoiceStatsM3Dto(
    int     TotalEmitidas,
    int     TotalEnviadas,
    int     TotalPendientes,
    int     TotalVencidas,
    int     TotalPagadas,
    int     TotalAnuladas,
    int     TotalParciales,
    decimal TotalMonto,
    decimal TotalRecaudado,
    decimal TotalCreditoAplicado
);
