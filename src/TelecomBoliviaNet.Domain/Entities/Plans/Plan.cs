using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Plans;

/// <summary>
/// Plan de internet contratado (Cobre 30Mb, Plata 50Mb, Oro 80Mb, etc.)
/// Los planes no se eliminan, solo se desactivan.
/// </summary>
public class Plan : Entity
{
    public string Name           { get; set; } = string.Empty; // "Plan Plata"
    public int    SpeedMb        { get; set; }                 // 50
    public decimal MonthlyPrice  { get; set; }                 // 149.00
    public bool   IsActive       { get; set; } = true;
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt   { get; set; }

    /// <summary>Texto para mostrar en selectores: "Plan Plata — 50 Mb — Bs. 149/mes"</summary>
    public string DisplayLabel =>
        $"{Name} — {SpeedMb} Mb — Bs. {MonthlyPrice:N0}/mes";
}
