using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Tickets;

/// <summary>Visita técnica programada. US-14.</summary>
public class TicketVisit : Entity
{
    public Guid           TicketId        { get; set; }
    public SupportTicket? Ticket          { get; set; }
    public DateTime       ScheduledAt     { get; set; }
    public Guid?          TechnicianId    { get; set; }
    public UserSystem?    Technician      { get; set; }
    public string?        Observations    { get; set; }
    public Guid           CreatedByUserId { get; set; }
    public DateTime       CreatedAt       { get; set; } = DateTime.UtcNow;
}
