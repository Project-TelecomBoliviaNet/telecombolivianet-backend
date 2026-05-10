using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Alerts;

public class InternalAlert : Entity
{
    public Guid?    ForUserId { get; set; }
    public string?  ForRole   { get; set; }
    public string   Type      { get; set; } = string.Empty;
    public string   Title     { get; set; } = string.Empty;
    public string   Message   { get; set; } = string.Empty;
    public string   Link      { get; set; } = string.Empty;
    public bool     IsRead    { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
