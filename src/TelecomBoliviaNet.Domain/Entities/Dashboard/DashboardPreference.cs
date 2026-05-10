using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Dashboard;

public class DashboardPreference : Entity
{
    public Guid     UserId           { get; set; }
    public bool     ShowKpis         { get; set; } = true;
    public bool     ShowTendencia    { get; set; } = true;
    public bool     ShowTickets      { get; set; } = true;
    public bool     ShowWhatsApp     { get; set; } = true;
    public bool     ShowDeudores     { get; set; } = true;
    public bool     ShowZonas        { get; set; } = true;
    public bool     ShowMetodosPago  { get; set; } = true;
    public bool     ShowComprobantes { get; set; } = true;
    public DateTime UpdatedAt        { get; set; } = DateTime.UtcNow;
}
