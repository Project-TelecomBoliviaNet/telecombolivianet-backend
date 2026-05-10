using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Plans;

/// <summary>
/// Snapshot inmutable de un plan en el momento de cada cambio.
/// Permite auditar exactamente cuándo y cómo evolucionaron precio, velocidad y estado.
/// Se inserta en PlanService.UpdateAsync ANTES de aplicar los nuevos valores.
/// </summary>
public class PlanHistory : Entity
{
    public Guid    PlanId        { get; set; }
    public Plan?   Plan          { get; set; }

    /// <summary>Valores anteriores al cambio (snapshot).</summary>
    public string  Name          { get; set; } = string.Empty;
    public int     SpeedMb       { get; set; }
    public decimal MonthlyPrice  { get; set; }
    public bool    IsActive      { get; set; }

    public DateTime ChangedAt    { get; set; } = DateTime.UtcNow;
    public Guid     ChangedById  { get; set; }
    public string   ChangedByName { get; set; } = string.Empty;

    /// <summary>Motivo opcional del cambio, ingresado por el admin.</summary>
    public string?  ChangeReason { get; set; }
}
