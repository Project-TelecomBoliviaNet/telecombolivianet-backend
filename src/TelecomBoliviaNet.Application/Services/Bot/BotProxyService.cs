using TelecomBoliviaNet.Application.Interfaces;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.DTOs.Bot;
using Microsoft.AspNetCore.Http;

namespace TelecomBoliviaNet.Application.Services.Bot;

/// <summary>
/// M10 — Proxy HTTP entre el panel C# y el microservicio chatbot (NestJS).
/// Consume los endpoints del MonitorController del chatbot.
/// US-BOT-01: conversaciones paginadas, escaladas, detalle por teléfono.
/// US-BOT-07: historial de conversaciones de un cliente.
/// </summary>
public class BotProxyService : IBotProxyService
{
    private readonly HttpClient               _http;
    private readonly ILogger<BotProxyService> _log;
    // BUG FIX: _chatbotBaseUrl eliminado — se usa el BaseAddress configurado en DI (ChatbotMonitor)
    // para evitar que URL local y BaseAddress diverjan.
    private readonly string                   _internalToken;

    public BotProxyService(
        IHttpClientFactory       httpFactory,
        IConfiguration           cfg,
        ILogger<BotProxyService> log)
    {
        _http          = httpFactory.CreateClient("ChatbotMonitor");
        _log           = log;
        _internalToken = cfg["Chatbot:InternalToken"] ?? "";
    }

    private HttpRequestMessage Auth(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Authorization", $"Bearer {_internalToken}");
        return req;
    }

    // ── US-BOT-01 · Listar conversaciones ────────────────────────────────────

    public async Task<(List<ConversationListItemDto> Items, int Total)> GetConversationsAsync(
        int page = 1, int limit = 20, bool? soloEscaladas = null)
    {
        try
        {
            var url = soloEscaladas == true
                ? "monitor/conversations/escalated"
                : $"monitor/conversations?page={page}&limit={limit}";

            var res = await _http.SendAsync(Auth(HttpMethod.Get, url));
            res.EnsureSuccessStatusCode();
            var raw   = await res.Content.ReadFromJsonAsync<JsonElement>();
            var total = raw.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            return (ParseConversationList(raw), total);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "BotProxy: error al obtener conversaciones");
            return (new List<ConversationListItemDto>(), 0);
        }
    }

    public async Task<ConversationDetailDto?> GetConversationByPhoneAsync(
        string phone, int msgLimit = 50)
    {
        try
        {
            var url = $"monitor/conversations/{Uri.EscapeDataString(phone)}?limit={msgLimit}";
            var res = await _http.SendAsync(Auth(HttpMethod.Get, url));
            res.EnsureSuccessStatusCode();
            var raw = await res.Content.ReadFromJsonAsync<JsonElement>();

            if (!raw.TryGetProperty("found", out var found) || !found.GetBoolean())
                return null;

            var conv = raw.GetProperty("conversation");
            var msgs = raw.TryGetProperty("messages", out var m) ? m : default;

            return new ConversationDetailDto(
                conv.GetProperty("id").GetString()!,
                conv.GetProperty("phoneNumber").GetString()!,
                conv.TryGetProperty("clientId",    out var cid)  ? cid.GetString()  : null,
                conv.TryGetProperty("clientName",  out var cname)? cname.GetString(): null,
                conv.TryGetProperty("isEscalated", out var esc)  ? esc.GetBoolean() : false,
                conv.TryGetProperty("agentName",   out var ag)   ? ag.GetString()   : null,
                ParseMessages(msgs));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "BotProxy: error al obtener conversación {Phone}", phone);
            return null;
        }
    }

    // ── US-BOT-07 · Historial por número de teléfono del cliente ─────────────

    public async Task<ClientConversationHistoryDto> GetClientHistoryAsync(string phone)
    {
        var detail = await GetConversationByPhoneAsync(phone, 200);
        if (detail is null)
            return new ClientConversationHistoryDto(phone, new List<ConversationListItemDto>());

        var item = new ConversationListItemDto(
            detail.Id, detail.PhoneNumber, detail.ClientId, detail.ClientName,
            detail.IsEscalated, detail.AgentName, null,
            DateTime.UtcNow.ToString("o"), DateTime.UtcNow.ToString("o"),
            detail.Messages.LastOrDefault()?.Content, detail.Messages.Count);

        return new ClientConversationHistoryDto(phone,
            new List<ConversationListItemDto> { item });
    }

    // ── Stats ────────────────────────────────────────────────────────────────

    public async Task<ConversationStatsDto> GetStatsAsync()
    {
        try
        {
            var res = await _http.SendAsync(
                Auth(HttpMethod.Get, "monitor/stats"));
            res.EnsureSuccessStatusCode();
            var raw   = await res.Content.ReadFromJsonAsync<JsonElement>();
            var total = raw.GetProperty("total");
            var today = raw.GetProperty("today");
            return new ConversationStatsDto(
                total.GetProperty("conversations").GetInt32(),
                total.GetProperty("escalated").GetInt32(),
                today.GetProperty("conversations").GetInt32(),
                today.GetProperty("messages").GetInt32());
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "BotProxy: error al obtener stats del chatbot");
            return new ConversationStatsDto(0, 0, 0, 0);
        }
    }

    // ── RAG proxy ────────────────────────────────────────────────────────────

    public async Task<object?> RagListDocumentsAsync()
    {
        try
        {
            var res = await _http.SendAsync(Auth(HttpMethod.Get, "rag/documents"));
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<object>();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "BotProxy: error al listar documentos RAG");
            return null;
        }
    }

    public async Task<object?> RagUploadDocumentAsync(IFormFile file, string? title)
    {
        try
        {
            using var content    = new MultipartFormDataContent();
            using var fileStream = file.OpenReadStream();
            var fileContent      = new StreamContent(fileStream);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "file", file.FileName);
            if (!string.IsNullOrWhiteSpace(title))
                content.Add(new StringContent(title), "title");

            var req = Auth(HttpMethod.Post, "rag/documents");
            req.Content = content;
            var res = await _http.SendAsync(req);
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<object>();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "BotProxy: error al subir documento RAG");
            return null;
        }
    }

    public async Task<bool> RagDeleteDocumentAsync(string id)
    {
        try
        {
            var res = await _http.SendAsync(Auth(HttpMethod.Delete, $"rag/documents/{id}"));
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "BotProxy: error al eliminar documento RAG {Id}", id);
            return false;
        }
    }

    // ── Helpers privados ─────────────────────────────────────────────────────

    private static List<ConversationListItemDto> ParseConversationList(JsonElement raw)
    {
        if (!raw.TryGetProperty("conversations", out var arr)
            || arr.ValueKind != JsonValueKind.Array)
            return new();
        return arr.EnumerateArray().Select(ParseConvItem).ToList();
    }

    private static ConversationListItemDto ParseConvItem(JsonElement c) => new(
        c.GetProperty("id").GetString()!,
        c.GetProperty("phoneNumber").GetString()!,
        c.TryGetProperty("clientId",    out var ci)  ? ci.GetString()   : null,
        c.TryGetProperty("clientName",  out var cn)  ? cn.GetString()   : null,
        c.TryGetProperty("isEscalated", out var es)  ? es.GetBoolean()  : false,
        c.TryGetProperty("agentName",   out var ag)  ? ag.GetString()   : null,
        c.TryGetProperty("escalatedAt", out var eat) ? eat.GetString()  : null,
        c.TryGetProperty("updatedAt",   out var up)  ? up.GetString()!  : "",
        c.TryGetProperty("createdAt",   out var ca)  ? ca.GetString()!  : "",
        null, 0);

    private static List<ConversationMessageDto> ParseMessages(JsonElement msgs)
    {
        if (msgs.ValueKind != JsonValueKind.Array) return new();
        return msgs.EnumerateArray().Select(m => new ConversationMessageDto(
            m.GetProperty("id").GetString()!,
            m.GetProperty("role").GetString()!,
            m.TryGetProperty("source", out var src) ? src.GetString() : null,
            m.GetProperty("content").GetString()!,
            m.GetProperty("createdAt").GetString()!
        )).ToList();
    }
}
