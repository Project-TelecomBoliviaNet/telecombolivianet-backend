using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Tickets;

/// <summary>CRUD de horarios comerciales configurables. US-07.</summary>
public class BusinessScheduleService
{
    private readonly IGenericRepository<BusinessSchedule> _scheduleRepo;
    private readonly IGenericRepository<ScheduleHoliday> _holidayRepo;

    private static readonly string[] DayNames =
        ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"];
    private static readonly int[]    DayBits  = [1, 2, 4, 8, 16, 32, 64];

    public BusinessScheduleService(
        IGenericRepository<BusinessSchedule> scheduleRepo,
        IGenericRepository<ScheduleHoliday>  holidayRepo)
    {
        _scheduleRepo = scheduleRepo;
        _holidayRepo  = holidayRepo;
    }

    // ── Listar ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BusinessScheduleDto>> GetAllAsync()
    {
        var schedules = await _scheduleRepo.GetAll()
            .Include(s => s.Holidays)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name)
            .ToListAsync();

        return schedules.Select(ToDto).ToList();
    }

    public async Task<BusinessScheduleDto?> GetByIdAsync(Guid id)
    {
        var s = await _scheduleRepo.GetAll()
            .Include(s => s.Holidays)
            .FirstOrDefaultAsync(s => s.Id == id);
        return s is null ? null : ToDto(s);
    }

    // ── Crear ─────────────────────────────────────────────────────────────────

    public async Task<Result<BusinessScheduleDto>> CreateAsync(CreateBusinessScheduleDto dto)
    {
        var validation = ValidateDto(dto.StartHour, dto.EndHour, dto.WorkingDaysMask, dto.Is24x7);
        if (validation is not null) return Result<BusinessScheduleDto>.Failure(validation);

        var exists = await _scheduleRepo.AnyAsync(s => s.Name == dto.Name.Trim());
        if (exists) return Result<BusinessScheduleDto>.Failure("Ya existe un horario con ese nombre.");

        if (dto.IsDefault)
            await ClearDefaultFlagAsync();

        var schedule = new BusinessSchedule
        {
            Name            = dto.Name.Trim(),
            TimeZoneName    = dto.TimeZoneName.Trim(),
            UtcOffsetHours  = dto.UtcOffsetHours,
            WorkingDaysMask = dto.WorkingDaysMask,
            StartHour       = dto.StartHour,
            EndHour         = dto.EndHour,
            Is24x7          = dto.Is24x7,
            IsDefault       = dto.IsDefault,
            IsActive        = true,
            CreatedAt       = DateTime.UtcNow,
        };

        await _scheduleRepo.AddAsync(schedule);
        await _scheduleRepo.SaveChangesAsync();

        return Result<BusinessScheduleDto>.Success(ToDto(schedule));
    }

    // ── Actualizar ────────────────────────────────────────────────────────────

    public async Task<Result<BusinessScheduleDto>> UpdateAsync(Guid id, UpdateBusinessScheduleDto dto)
    {
        var schedule = await _scheduleRepo.GetAll()
            .Include(s => s.Holidays)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (schedule is null) return Result<BusinessScheduleDto>.Failure("Horario no encontrado.");

        if (dto.Name is not null)
        {
            var taken = await _scheduleRepo.AnyAsync(s => s.Name == dto.Name.Trim() && s.Id != id);
            if (taken) return Result<BusinessScheduleDto>.Failure("Ya existe un horario con ese nombre.");
            schedule.Name = dto.Name.Trim();
        }

        if (dto.TimeZoneName is not null) schedule.TimeZoneName    = dto.TimeZoneName.Trim();
        if (dto.UtcOffsetHours.HasValue)  schedule.UtcOffsetHours  = dto.UtcOffsetHours.Value;
        if (dto.WorkingDaysMask.HasValue) schedule.WorkingDaysMask = dto.WorkingDaysMask.Value;
        if (dto.StartHour.HasValue)       schedule.StartHour        = dto.StartHour.Value;
        if (dto.EndHour.HasValue)         schedule.EndHour          = dto.EndHour.Value;
        if (dto.Is24x7.HasValue)          schedule.Is24x7           = dto.Is24x7.Value;
        if (dto.IsActive.HasValue)        schedule.IsActive         = dto.IsActive.Value;

        if (dto.IsDefault == true && !schedule.IsDefault)
        {
            await ClearDefaultFlagAsync();
            schedule.IsDefault = true;
        }

        var startH = dto.StartHour ?? schedule.StartHour;
        var endH   = dto.EndHour   ?? schedule.EndHour;
        var mask   = dto.WorkingDaysMask ?? schedule.WorkingDaysMask;
        var is24   = dto.Is24x7 ?? schedule.Is24x7;

        var validation = ValidateDto(startH, endH, mask, is24);
        if (validation is not null) return Result<BusinessScheduleDto>.Failure(validation);

        schedule.UpdatedAt = DateTime.UtcNow;
        await _scheduleRepo.UpdateAsync(schedule);
        await _scheduleRepo.SaveChangesAsync();

        return Result<BusinessScheduleDto>.Success(ToDto(schedule));
    }

    // ── Feriados ──────────────────────────────────────────────────────────────

    public async Task<Result<ScheduleHolidayDto>> AddHolidayAsync(Guid scheduleId, CreateHolidayDto dto)
    {
        var scheduleExists = await _scheduleRepo.AnyAsync(s => s.Id == scheduleId);
        if (!scheduleExists) return Result<ScheduleHolidayDto>.Failure("Horario no encontrado.");

        if (!DateOnly.TryParseExact(dto.Date, "yyyy-MM-dd", out var date))
            return Result<ScheduleHolidayDto>.Failure("Formato de fecha inválido. Use yyyy-MM-dd.");

        var dup = await _holidayRepo.AnyAsync(h => h.BusinessScheduleId == scheduleId && h.Date == date);
        if (dup) return Result<ScheduleHolidayDto>.Failure("Ya existe un feriado para esa fecha en este horario.");

        var holiday = new ScheduleHoliday
        {
            BusinessScheduleId = scheduleId,
            Date               = date,
            Name               = dto.Name.Trim(),
        };

        await _holidayRepo.AddAsync(holiday);
        await _holidayRepo.SaveChangesAsync();

        return Result<ScheduleHolidayDto>.Success(new ScheduleHolidayDto(
            holiday.Id, holiday.Date.ToString("yyyy-MM-dd"), holiday.Name));
    }

    public async Task<Result> DeleteHolidayAsync(Guid scheduleId, Guid holidayId)
    {
        var holiday = await _holidayRepo.FirstOrDefaultAsync(
            h => h.Id == holidayId && h.BusinessScheduleId == scheduleId);
        if (holiday is null) return Result.Failure("Feriado no encontrado.");

        await _holidayRepo.DeleteAsync(holidayId);
        await _holidayRepo.SaveChangesAsync();
        return Result.Success();
    }

    // ── Helpers internos ──────────────────────────────────────────────────────

    private async Task ClearDefaultFlagAsync()
    {
        var current = await _scheduleRepo.GetAll()
            .Where(s => s.IsDefault)
            .ToListAsync();
        foreach (var s in current) s.IsDefault = false;
        if (current.Count > 0)
            await _scheduleRepo.UpdateRangeAsync(current);
    }

    private static string? ValidateDto(int startHour, int endHour, int mask, bool is24x7)
    {
        if (!is24x7)
        {
            if (startHour < 0 || startHour > 23) return "HoraInicio debe ser entre 0 y 23.";
            if (endHour   < 1 || endHour   > 24) return "HoraFin debe ser entre 1 y 24.";
            if (startHour >= endHour)             return "HoraInicio debe ser menor que HoraFin.";
            if (mask <= 0 || mask > 127)          return "WorkingDaysMask inválido (1-127).";
        }
        return null;
    }

    private static BusinessScheduleDto ToDto(BusinessSchedule s) => new(
        s.Id,
        s.Name,
        s.TimeZoneName,
        s.UtcOffsetHours,
        s.WorkingDaysMask,
        BuildDayNames(s.WorkingDaysMask),
        s.StartHour,
        s.EndHour,
        s.Is24x7,
        s.IsDefault,
        s.IsActive,
        s.CreatedAt,
        s.Holidays
            .OrderBy(h => h.Date)
            .Select(h => new ScheduleHolidayDto(h.Id, h.Date.ToString("yyyy-MM-dd"), h.Name))
            .ToList()
    );

    private static string[] BuildDayNames(int mask)
    {
        var names = new List<string>();
        for (int i = 0; i < DayBits.Length; i++)
            if ((mask & DayBits[i]) != 0)
                names.Add(DayNames[i]);
        return names.ToArray();
    }
}
