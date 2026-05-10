using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Tickets;

/// <summary>Registro de tiempo trabajado. US-13.</summary>
public class TicketWorkLog : Entity
{
    public Guid          TicketId { get; set; }
    public SupportTicket? Ticket  { get; set; }
    public Guid          UserId   { get; set; }
    public UserSystem?   User     { get; set; }
    public int           Minutes  { get; set; }
    public string?       Notes    { get; set; }
    public DateTime      LoggedAt { get; set; } = DateTime.UtcNow;
}
