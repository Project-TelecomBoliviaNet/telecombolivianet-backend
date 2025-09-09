using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Tickets;

/// <summary>
/// US-TKT-ADJ — Adjuntos de un ticket (fotos, PDFs, logs).
/// Límite: 10 archivos por ticket. Máx 15 MB por archivo.
/// Tipos: image/jpeg, image/png, image/webp, application/pdf, text/plain.
/// </summary>
public class TicketAttachment : Entity
{
    public Guid        TicketId      { get; set; }
    public SupportTicket? Ticket    { get; set; }

    public string      FileName      { get; set; } = string.Empty;
    public string      StoragePath   { get; set; } = string.Empty;
    public string      ContentType   { get; set; } = string.Empty;
    public long        FileSizeBytes { get; set; }
    public string?     Descripcion   { get; set; }

    public Guid        SubidoPorId   { get; set; }
    public UserSystem? SubidoPor     { get; set; }
    public DateTime    SubidoAt      { get; set; } = DateTime.UtcNow;

    public bool        IsDeleted     { get; set; } = false;
    public DateTime?   DeletedAt     { get; set; }
}
