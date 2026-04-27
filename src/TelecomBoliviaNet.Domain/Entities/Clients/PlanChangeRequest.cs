using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Clients;

public enum PlanChangeStatus { Pendiente, Aprobado, Rechazado }

/// <summary>
/// Solicitud de cambio de plan generada por ticket o por el admin.
///
/// Reglas de facturación:
///  - Fin de mes (días 25+): efectivo el 1ro del mes siguiente, factura completa nuevo plan.
///  - A mitad de mes (aprobado manualmente): factura proporcional del plan anterior
///    por los días ya transcurridos + factura proporcional del plan nuevo por los días restantes.
/// </summary>
public class PlanChangeRequest : Entity
{
    public Guid     ClientId      { get; set; }
    public Client?  Client        { get; set; }

    public Guid     OldPlanId     { get; set; }
    public Plan?    OldPlan       { get; set; }

    public Guid     NewPlanId     { get; set; }
    public Plan?    NewPlan       { get; set; }

    public Guid?    TicketId      { get; set; }
    public SupportTicket? Ticket  { get; set; }

    public PlanChangeStatus Status { get; set; } = PlanChangeStatus.Pendiente;

    /// <summary>Fecha efectiva del cambio (1ro del mes siguiente o fecha aprobada).</summary>
    public DateTime EffectiveDate  { get; set; }

    /// <summary>Si true, el cambio fue aprobado a mitad de mes y requiere factura proporcional.</summary>
    public bool     MidMonthChange { get; set; } = false;

    public string?  RejectionReason { get; set; }
    public string?  Notes           { get; set; }

    public DateTime RequestedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt  { get; set; }
    public Guid     RequestedById { get; set; }
    public Guid?    ProcessedById { get; set; }
}
