using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TelecomBoliviaNet.Application.DTOs.Bot;
using TelecomBoliviaNet.Presentation.Controllers;
using TelecomBoliviaNet.Presentation.Hubs;

namespace TelecomBoliviaNet.Presentation.Controllers.Bot;

// ══════════════════════════════════════════════════════════════════════════════
// BOT EVENTS CONTROLLER
//
// Endpoint que el chatbot (NestJS) llama para notificar al panel admin.
// El chatbot NO se conecta al Hub SignalR directamente — llama este
// endpoint REST y el servidor reenvía el evento al Hub.
//
// Ruta:   POST /api/bot-events
// Auth:   Bearer JWT del usuario bot (AllRoles)
// Flujo:  Chatbot → POST /api/bot-events → IHubContext<AdminHub>
//                 → "BotEvent" → Panel Admin React
// ══════════════════════════════════════════════════════════════════════════════

[Route("api/bot-events")]
[ApiController]
public class BotEventsController : BaseController
{
    private readonly IHubContext<AdminHub> _hub;
    private readonly ILogger<BotEventsController> _logger;

    public BotEventsController(
        IHubContext<AdminHub> hub,
        ILogger<BotEventsController> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/bot-events
    /// Recibe un evento del chatbot y lo reenvía a todos los clientes
    /// del panel admin conectados al Hub SignalR.
    ///
    /// Autenticación: el chatbot envía el JWT del usuario bot
    /// (SISTEMA_BOT_STATIC_TOKEN en .env del chatbot).
    /// El usuario bot debe existir en UserSystems con cualquier rol.
    ///
    /// Si no hay admins conectados el evento se pierde silenciosamente
    /// (fire-and-forget por diseño — el chatbot no debe esperar).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> ReceiveBotEvent([FromBody] BotEventDto dto)
    {
        if (dto is null)
            return BadRequestResult("El cuerpo del evento es requerido.");

        _logger.LogInformation(
            "BotEvent recibido: {Type} — {Phone} — {Client}",
            dto.EventType, dto.PhoneNumber, dto.ClientName ?? "—");

        // Construir payload enriquecido para el frontend
        var payload = new AdminBotEventPayload
        {
            EventType   = dto.EventType.ToString(),
            PhoneNumber = dto.PhoneNumber,
            ClientName  = dto.ClientName,
            TicketId    = dto.TicketId,
            Priority    = dto.Priority,
            Reason      = dto.Reason,
            Timestamp   = dto.Timestamp,
            ReceivedAt  = DateTimeOffset.UtcNow.ToString("O"),
        };

        // Enviar a TODOS los clientes admin conectados
        // "BotEvent" es el nombre del método que el frontend escucha
        await _hub.Clients.All.SendAsync("BotEvent", payload);

        _logger.LogInformation(
            "BotEvent reenviado vía SignalR: {Type}", dto.EventType);

        return OkMessage("Evento procesado y reenviado al panel.");
    }
}
