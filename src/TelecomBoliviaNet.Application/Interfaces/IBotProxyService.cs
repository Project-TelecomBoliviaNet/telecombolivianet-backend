using TelecomBoliviaNet.Application.DTOs.Bot;
using Microsoft.AspNetCore.Http;

namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// CORRECCIÓN Problema #7: interfaz para BotProxyService.
/// Permite mockear el proxy HTTP en tests unitarios.
/// </summary>
public interface IBotProxyService
{
    Task<(List<ConversationListItemDto> Items, int Total)> GetConversationsAsync(
        int page = 1, int limit = 20, bool? soloEscaladas = null, string tab = "todas");
    Task<ConversationDetailDto?>  GetConversationByPhoneAsync(string phone, int msgLimit = 50);
    Task<ClientConversationHistoryDto> GetClientHistoryAsync(string phone);
    Task<ConversationStatsDto>    GetStatsAsync();

    // RAG
    Task<object?>  RagListDocumentsAsync();
    Task<object?>  RagUploadDocumentAsync(IFormFile file, string? title);
    Task<bool>     RagDeleteDocumentAsync(string id);

    // Intervención de agente
    Task<bool> TakeoverConversationAsync(string phone, string agentName);
    Task<bool> ReleaseConversationAsync(string phone);
    Task<bool> SendAdminMessageAsync(string phone, string text);
}
