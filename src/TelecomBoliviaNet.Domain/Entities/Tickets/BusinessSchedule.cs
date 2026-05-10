using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Tickets;

/// <summary>
/// Horario comercial configurable desde la base de datos. US-07.
/// Reemplaza los valores hardcodeados en BusinessHoursOptions (appsettings.json).
/// </summary>
public class BusinessSchedule : Entity
{
    public string Name           { get; set; } = string.Empty; // Ej: "Horario Bolivia (L-V 8-18)"
    public string TimeZoneName   { get; set; } = string.Empty; // Nombre display: "America/La_Paz"
    public int    UtcOffsetHours { get; set; } = -4;           // Offset real usado en cálculos
    public int    WorkingDaysMask{ get; set; } = 31;           // Bitmask: Lun=1,Mar=2,Mié=4,Jue=8,Vie=16,Sáb=32,Dom=64
    public int    StartHour      { get; set; } = 8;            // Hora inicio jornada (0-23)
    public int    EndHour        { get; set; } = 18;           // Hora fin jornada (0-23)
    public bool   Is24x7         { get; set; } = false;        // Si true: ignora días/horas, corre continuo
    public bool   IsDefault      { get; set; } = false;        // Solo uno puede ser default
    public bool   IsActive       { get; set; } = true;
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt   { get; set; }

    public ICollection<ScheduleHoliday> Holidays { get; set; } = new List<ScheduleHoliday>();

    // Verifica si un día de semana está marcado como laborable en el bitmask.
    // DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
    public bool IsWorkingDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday    => (WorkingDaysMask & 1)  != 0,
        DayOfWeek.Tuesday   => (WorkingDaysMask & 2)  != 0,
        DayOfWeek.Wednesday => (WorkingDaysMask & 4)  != 0,
        DayOfWeek.Thursday  => (WorkingDaysMask & 8)  != 0,
        DayOfWeek.Friday    => (WorkingDaysMask & 16) != 0,
        DayOfWeek.Saturday  => (WorkingDaysMask & 32) != 0,
        DayOfWeek.Sunday    => (WorkingDaysMask & 64) != 0,
        _                   => false
    };
}
