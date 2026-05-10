namespace TelecomBoliviaNet.Application.DTOs.Bot;

// ══════════════════════════════════════════════════════════════════════════════
// DTOs de eventos enviados por el chatbot al sistema central.
//
// El chatbot (NestJS) llama POST /api/bot-events con un payload
// que sigue esta estructura. El BotEventsController lo valida
// y lo reenvía al panel admin vía SignalR.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tipos de evento que el chatbot puede enviar al panel admin.
/// Deben coincidir con AdminEventType en el chatbot (NestJS).
/// </summary>
public enum BotEventType
{
    TICKET_ALTA,             // Ticket creado con prioridad Alta (US-12)
    TICKET_CREATED,          // Ticket creado (cualquier prioridad)
    CONVERSATION_ESCALATED,  // Conversación escalada al agente (US-17)
}

/// <summary>
/// Payload recibido del chatbot en POST /api/bot-events.
/// PascalCase — el chatbot envía en PascalCase para .NET.
/// </summary>
public class BotEventDto
{
    public BotEventType EventType   { get; set; }
    public string       PhoneNumber { get; set; } = string.Empty;
    public string?      ClientName  { get; set; }
    public string?      TicketId    { get; set; }
    public string?      Priority    { get; set; }
    public string?      Reason      { get; set; }
    /// <summary>ISO-8601, generado por el chatbot.</summary>
    public string       Timestamp   { get; set; } = string.Empty;
}

/// <summary>
/// Payload que el hub reenvía al panel admin React.
/// Extiende el DTO original con metadatos del servidor.
/// </summary>
public class AdminBotEventPayload
{
    public string  EventType   { get; set; } = string.Empty;
    public string  PhoneNumber { get; set; } = string.Empty;
    public string? ClientName  { get; set; }
    public string? TicketId    { get; set; }
    public string? Priority    { get; set; }
    public string? Reason      { get; set; }
    public string  Timestamp   { get; set; } = string.Empty;
    /// <summary>Añadido por el servidor al momento de recibir el evento.</summary>
    public string  ReceivedAt  { get; set; } = string.Empty;
}
