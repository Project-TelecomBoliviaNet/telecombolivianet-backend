using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Tickets;

/// <summary>Feriado asociado a un horario comercial. US-07.</summary>
public class ScheduleHoliday : Entity
{
    public Guid               BusinessScheduleId { get; set; }
    public BusinessSchedule?  BusinessSchedule   { get; set; }
    public DateOnly           Date               { get; set; }
    public string             Name               { get; set; } = string.Empty; // Ej: "Día de la Independencia"
}
