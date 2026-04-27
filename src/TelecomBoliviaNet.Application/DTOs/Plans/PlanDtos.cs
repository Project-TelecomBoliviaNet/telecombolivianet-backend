namespace TelecomBoliviaNet.Application.DTOs.Plans;

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
    bool    IsActive
);
