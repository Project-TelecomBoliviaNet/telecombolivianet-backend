using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Tickets;

public enum SlaSchedule { Veinticuatro7, Laboral }

/// <summary>Plan SLA configurable por prioridad. US-05, US-07.</summary>
public class SlaPlan : Entity
{
    public string      Name                 { get; set; } = string.Empty;
    public string      Priority             { get; set; } = string.Empty; // Critica|Alta|Media|Baja
    public int         FirstResponseMinutes { get; set; }
    public int         ResolutionMinutes    { get; set; }
    public SlaSchedule Schedule             { get; set; } = SlaSchedule.Veinticuatro7;
    public bool        IsActive             { get; set; } = true;
    public DateTime    CreatedAt            { get; set; } = DateTime.UtcNow;

    // US-07: horario comercial asociado. null = usar el horario default del sistema.
    public Guid?             BusinessScheduleId { get; set; }
    public BusinessSchedule? BusinessSchedule   { get; set; }
}
