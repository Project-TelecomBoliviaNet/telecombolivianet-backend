namespace TelecomBoliviaNet.Application.DTOs.Tickets;

// ── Response ─────────────────────────────────────────────────────────────────

public record BusinessScheduleDto(
    Guid     Id,
    string   Name,
    string   TimeZoneName,
    int      UtcOffsetHours,
    int      WorkingDaysMask,
    string[] WorkingDaysDisplay,   // ["Lunes","Martes",...] para UI
    int      StartHour,
    int      EndHour,
    bool     Is24x7,
    bool     IsDefault,
    bool     IsActive,
    DateTime CreatedAt,
    IReadOnlyList<ScheduleHolidayDto> Holidays
);

public record ScheduleHolidayDto(
    Guid   Id,
    string Date,   // "YYYY-MM-DD"
    string Name
);

// ── Requests ──────────────────────────────────────────────────────────────────

public record CreateBusinessScheduleDto(
    string  Name,
    string  TimeZoneName,
    int     UtcOffsetHours,
    int     WorkingDaysMask,
    int     StartHour,
    int     EndHour,
    bool    Is24x7,
    bool    IsDefault
);

public record UpdateBusinessScheduleDto(
    string? Name,
    string? TimeZoneName,
    int?    UtcOffsetHours,
    int?    WorkingDaysMask,
    int?    StartHour,
    int?    EndHour,
    bool?   Is24x7,
    bool?   IsDefault,
    bool?   IsActive
);

public record CreateHolidayDto(
    string Date,   // "YYYY-MM-DD"
    string Name
);
