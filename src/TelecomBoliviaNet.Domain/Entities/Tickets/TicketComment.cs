using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Tickets;

public enum CommentType
{
    RespuestaCliente,   // US-09: visible al cliente
    NotaInterna,        // US-10: solo equipo
    CausaRaiz,          // US-16: base de conocimiento
}

/// <summary>Comentario/nota de un ticket. US-09, US-10, US-16.</summary>
public class TicketComment : Entity
{
    public Guid          TicketId  { get; set; }
    public SupportTicket? Ticket   { get; set; }
    public Guid          AuthorId  { get; set; }
    public UserSystem?   Author    { get; set; }
    public CommentType   Type      { get; set; } = CommentType.NotaInterna;
    public string        Body      { get; set; } = string.Empty;
    public DateTime      CreatedAt { get; set; } = DateTime.UtcNow;
}
