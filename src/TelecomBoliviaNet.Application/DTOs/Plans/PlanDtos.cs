namespace TelecomBoliviaNet.Application.DTOs.Plans;

/// <summary>FIX-H: Snapshot de un plan antes de cada cambio.</summary>
public record PlanHistoryDto(
    Guid    Id,
    Guid    PlanId,
    string  Name,
    int     SpeedMb,
    decimal MonthlyPrice,
    bool    IsActive,
    string  DisplayLabel,
    string  ChangedAt,
    string  ChangedByName,
    string? ChangeReason
);

public record PlanDto(
    Guid    Id,
    string  Name,
    int     SpeedMb,
    decimal MonthlyPrice,
    bool    IsActive,
    string  DisplayLabel
);

public record CreatePlanDto(
    string  Name,
    int     SpeedMb,
    decimal MonthlyPrice
);

public record UpdatePlanDto(
    string  Name,
    int     SpeedMb,
    decimal MonthlyPrice,
    bool    IsActive,
    string? ChangeReason = null
);
