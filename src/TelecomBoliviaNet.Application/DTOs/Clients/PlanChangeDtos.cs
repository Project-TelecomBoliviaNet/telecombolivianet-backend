namespace TelecomBoliviaNet.Application.DTOs.Clients;

// ── Cambio de Plan ─────────────────────────────────────────────────────────────

/// <summary>
/// DTO tipado para solicitudes de cambio de plan pendientes.
/// Reemplaza el List&lt;object&gt; anterior en PlanChangeService.GetPendientesAsync().
/// Garantiza type safety en tiempo de compilación y facilita el testing unitario.
/// </summary>
public record PlanChangeItemDto(
    Guid    Id,
    string  ClienteTbn,
    string  ClienteNombre,
    string  PlanAnterior,
    string  PlanNuevo,
    /// <summary>Fecha efectiva en formato ISO-8601 (yyyy-MM-dd).</summary>
    string  FechaEfectiva,
    string? Notes,
    /// <summary>Timestamp de solicitud en formato ISO-8601 round-trip (O).</summary>
    string  SolicitadoAt
);

// BUG FIX: DTOs movidos desde PlanChangeController al lugar correcto (capa Application).
// Permite registrar IValidator<SolicitarCambioDto> en FluentValidation con validación
// de longitud mínima en Motivo.

public record SolicitarCambioDto(
    Guid    NewPlanId,
    string? Notes
);

public record RechazarCambioDto(
    string Motivo
);
