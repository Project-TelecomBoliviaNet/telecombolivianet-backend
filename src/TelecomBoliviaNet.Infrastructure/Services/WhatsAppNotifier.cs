using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Admin;

namespace TelecomBoliviaNet.Infrastructure.Services;

/// <summary>
/// Envía mensajes WhatsApp usando la API oficial de Meta (Graph API).
/// La versión de API es configurable en appsettings: WhatsApp:ApiVersion (default v18.0).
/// Solo para mensajes síncronos críticos. Los asíncronos/programados van por el Notifier Python.
///
/// BUG FIX: El token ya no se fija en el constructor — se lee dinámicamente en cada llamada
/// a SendTextAsync desde AdminSettingsService para reflejar cambios de token en runtime.
/// </summary>
public class WhatsAppNotifier : IWhatsAppNotifier
{
    private readonly HttpClient              _http;
    private readonly AdminSettingsService    _adminSettings;
    private readonly string                  _phoneNumberId;
    private readonly ILogger<WhatsAppNotifier> _logger;

    public WhatsAppNotifier(
        HttpClient               http,
        IConfiguration           config,
        AdminSettingsService     adminSettings,
        ILogger<WhatsAppNotifier> logger)
    {
        _http          = http;
        _adminSettings = adminSettings;
        _logger        = logger;

        // PhoneNumberId y ApiVersion son configuración de infraestructura que no cambia en runtime
        _phoneNumberId = config["WhatsApp:PhoneNumberId"] ?? string.Empty;

        var apiVersion = config["WhatsApp:ApiVersion"] ?? "v18.0";
        _http.BaseAddress = new Uri($"https://graph.facebook.com/{apiVersion}/");
    }

    public async Task SendTextAsync(string phoneNumber, string message)
    {
        // BUG FIX: Leer token dinámicamente para reflejar cambios hechos desde el panel admin
        var settings = await _adminSettings.GetCurrentAsync();
        var token    = settings.WhatsAppToken;

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(_phoneNumberId))
        {
            _logger.LogWarning("[WhatsApp STUB] → {Phone}: {Message}", phoneNumber, message);
            return;
        }

        // Actualizar header con el token actual en cada llamada
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var formattedPhone = FormatPhone(phoneNumber);

        var payload = new
        {
            messaging_product = "whatsapp",
            to   = formattedPhone,
            type = "text",
            text = new { body = message }
        };

        try
        {
            var response = await _http.PostAsJsonAsync($"{_phoneNumberId}/messages", payload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error WhatsApp {Status}: {Body}", response.StatusCode, body);
            }
            else
            {
                _logger.LogInformation("WhatsApp enviado a {Phone}", formattedPhone);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción enviando WhatsApp a {Phone}", formattedPhone);
        }
    }

    private static string FormatPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("591")) return digits;
        if (digits.Length == 8)      return $"591{digits}";
        return digits;
    }
}
