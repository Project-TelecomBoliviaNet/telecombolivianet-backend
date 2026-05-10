using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TelecomBoliviaNet.Presentation.Hubs;

// ══════════════════════════════════════════════════════════════════════════════
// ADMIN HUB — SignalR
//
// Hub al que se conecta el panel admin React (frontend).
// Solo los usuarios autenticados (Admin / Tecnico) pueden conectarse.
//
// El flujo completo es:
//   Chatbot → POST /api/bot-events → BotEventsController
//           → IHubContext<AdminHub> → AdminHub → Panel Admin React
//
// Métodos que el servidor envía al cliente (frontend):
//   - "BotEvent"       → evento genérico del bot (tickets, escalaciones)
//   - "QrExpiringSoon" → aviso de QR de cliente próximo a vencer
//   - "Ping"           → keepalive opcional
//
// El hub en sí no expone métodos que el cliente invoque directamente
// (es un canal de push unidireccional servidor → cliente).
// ══════════════════════════════════════════════════════════════════════════════

[Authorize(Policy = "AdminOrTecnico")]
public class AdminHub : Hub
{
    private readonly ILogger<AdminHub> _logger;

    public AdminHub(ILogger<AdminHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var user = Context.User?.Identity?.Name ?? "desconocido";
        _logger.LogInformation("AdminHub: {User} conectado — ConnectionId: {Id}",
            user, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var user = Context.User?.Identity?.Name ?? "desconocido";
        _logger.LogInformation("AdminHub: {User} desconectado — {Id}",
            user, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // ── Ping / keepalive (el cliente puede llamar esto para verificar conexión)
    public async Task Ping()
    {
        await Clients.Caller.SendAsync("Pong", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
}
