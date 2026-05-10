using TelecomBoliviaNet.Domain.Entities.Tickets;

namespace TelecomBoliviaNet.Domain.Services;

/// <summary>
/// Pure domain service for SLA deadline calculations.
/// All methods are stateless and depend only on domain entities — no repositories.
/// </summary>
public static class SlaCalculator
{
    /// <summary>
    /// Adds <paramref name="minutes"/> of business time to <paramref name="utcStart"/>,
    /// respecting the working hours, working days, and holidays in <paramref name="schedule"/>.
    /// Returns a UTC DateTime.
    /// </summary>
    public static DateTime AddBusinessMinutes(
        DateTime utcStart, int minutes, BusinessSchedule schedule, IReadOnlyList<DateOnly> holidays)
    {
        var offset   = TimeSpan.FromHours(schedule.UtcOffsetHours);
        var bizStart = TimeSpan.FromHours(schedule.StartHour);
        var bizEnd   = TimeSpan.FromHours(schedule.EndHour);

        var local = utcStart + offset;
        local = SnapToNextBusinessMoment(local, schedule, holidays, bizStart, bizEnd);

        while (minutes > 0)
        {
            var minutesToEndOfDay = (int)((local.Date + bizEnd) - local).TotalMinutes;
            if (minutes <= minutesToEndOfDay)
            {
                local = local.AddMinutes(minutes);
                break;
            }
            minutes -= minutesToEndOfDay;
            local    = NextBusinessDayStart(local.Date.AddDays(1), schedule, holidays, bizStart);
        }
        return local - offset;
    }

    private static DateTime SnapToNextBusinessMoment(
        DateTime local, BusinessSchedule schedule, IReadOnlyList<DateOnly> holidays,
        TimeSpan bizStart, TimeSpan bizEnd)
    {
        for (var guard = 0; guard < 365; guard++)
        {
            var today = DateOnly.FromDateTime(local);
            if (!schedule.IsWorkingDay(local.DayOfWeek) || holidays.Contains(today))
            {
                local = local.Date.AddDays(1) + bizStart;
                continue;
            }
            if (local.TimeOfDay < bizStart) return local.Date + bizStart;
            if (local.TimeOfDay >= bizEnd)
                return NextBusinessDayStart(local.Date.AddDays(1), schedule, holidays, bizStart);
            return local;
        }
        return local;
    }

    private static DateTime NextBusinessDayStart(
        DateTime date, BusinessSchedule schedule, IReadOnlyList<DateOnly> holidays, TimeSpan bizStart)
    {
        var d = date.Date + bizStart;
        for (var guard = 0; guard < 365; guard++)
        {
            var today = DateOnly.FromDateTime(d);
            if (schedule.IsWorkingDay(d.DayOfWeek) && !holidays.Contains(today))
                return d;
            d = d.AddDays(1);
        }
        return d;
    }
}
