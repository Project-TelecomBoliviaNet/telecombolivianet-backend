using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Infrastructure.Services;

/// <summary>
/// Sends WhatsApp confirmation messages directly via the chatbot NestJS endpoint
/// while the user-initiated 24h window is open — no Meta template cost.
///
/// Uses the named HttpClient "ChatbotMonitor" (BaseAddress = http://chatbot:3001,
/// Timeout = 10s) already registered in DependencyInjection.cs.
///
/// Never throws: all errors are caught and logged; returns false so the caller
/// can fallback to INotifPublisher (outbox → Python notifier).
/// </summary>
public class ChatbotDirectNotifier : IChatbotDirectNotifier
{
    private readonly IHttpClientFactory              _httpFactory;
    private readonly IConfiguration                  _config;
    private readonly ILogger<ChatbotDirectNotifier>  _logger;

    public ChatbotDirectNotifier(
        IHttpClientFactory             httpFactory,
        IConfiguration                 config,
        ILogger<ChatbotDirectNotifier> logger)
    {
        _httpFactory = httpFactory;
        _config      = config;
        _logger      = logger;
    }

    public async Task<bool> SendPaymentConfirmationAsync(
        string  phoneNumber,
        string  clientName,
        decimal amount,
        string  coveredPeriod)
    {
        try
        {
            var token = _config["Chatbot:BotToken"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning(
                    "[ChatbotDirect] Chatbot:BotToken no configurado — usando fallback al notifier.");
                return false;
            }

            var phone   = FormatPhone(phoneNumber);
            var message = BuildMessage(clientName, amount, coveredPeriod);

            var http = _httpFactory.CreateClient("ChatbotMonitor");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var response = await http.PostAsJsonAsync(
                $"/monitor/conversations/{phone}/send",
                new { text = message });

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "[ChatbotDirect] Confirmación enviada a {Phone} (ventana 24h, sin costo de template).",
                    phone);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning(
                "[ChatbotDirect] Error al enviar a {Phone}: HTTP {Status} — {Body}",
                phone, (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[ChatbotDirect] Excepción al enviar confirmación a {Phone}.",
                phoneNumber);
            return false;
        }
    }

    private static string BuildMessage(string clientName, decimal amount, string coveredPeriod)
        => $"Estimado/a {clientName},\n" +
           $"Le saluda TelecomBoliviaNet, su proveedor de servicios de internet.\n\n" +
           $"Confirmamos la recepción de su pago por Bs. {amount:F2}, " +
           $"correspondiente al/los período(s): {coveredPeriod}.\n\n" +
           $"Su servicio se mantiene activo con normalidad. " +
           $"Agradecemos su puntualidad y la confianza depositada en nosotros.\n\n" +
           $"Atentamente,\nTelecomBoliviaNet 🙏";

    private static string FormatPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("591")) return digits;
        if (digits.Length == 8)       return $"591{digits}";
        return digits;
    }
}
