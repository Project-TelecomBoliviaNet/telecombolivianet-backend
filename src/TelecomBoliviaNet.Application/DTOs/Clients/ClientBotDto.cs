namespace TelecomBoliviaNet.Application.DTOs.Clients;

/// <summary>
/// DTO devuelto al chatbot cuando busca un cliente por número de teléfono.
/// Todos los campos en PascalCase — requerido por el chatbot (NestJS axios).
/// Solo expone los datos que el bot necesita para operar; no incluye datos
/// sensibles de instalación ni credenciales internas.
/// </summary>
public record ClientBotDto(
    string  Id,
    string  TbnCode,
    string  FullName,
    string  PhoneMain,
    string  Status,           // "Activo" | "Suspendido" | "Cancelado" | "DadoDeBaja"
    string  PlanId,
    string  PlanName,
    int     PlanSpeedMbps,    // SpeedMb del plan (simétrico)
    decimal TotalDebt,
    int     PendingMonths,
    string  Zone
);
