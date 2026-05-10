using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Infrastructure.Services;

/// <summary>
/// Envía emails transaccionales usando la API de Resend (resend.com).
/// Si Email:ApiKey no está configurada, entra en modo STUB: loguea el contenido
/// sin enviar nada, para que el entorno de desarrollo no quede bloqueado.
/// </summary>
public class ResendEmailService : IEmailService
{
    private const string ResendApiUrl = "https://api.resend.com/emails";

    private readonly HttpClient              _http;
    private readonly ILogger<ResendEmailService> _logger;
    private readonly string _apiKey;
    private readonly string _fromAddress;
    private readonly string _fromName;

    public ResendEmailService(
        HttpClient                   http,
        IConfiguration               config,
        ILogger<ResendEmailService>  logger)
    {
        _http        = http;
        _logger      = logger;
        _apiKey      = config["Email:ApiKey"]      ?? string.Empty;
        _fromAddress = config["Email:FromAddress"] ?? "onboarding@resend.dev";
        _fromName    = config["Email:FromName"]    ?? "TelecomBoliviaNet";
    }

    public async Task SendPasswordResetAsync(string toEmail, string toName, string resetLink)
    {
        var subject = "Recuperación de contraseña — TelecomBoliviaNet";
        var html    = BuildPasswordResetHtml(toName, resetLink);

        await SendAsync(toEmail, subject, html,
            stub: $"[EMAIL_STUB] Reset password para {toEmail} — enlace: {resetLink}");
    }

    public async Task SendPasswordChangedNotificationAsync(string toEmail, string toName)
    {
        var subject = "Tu contraseña fue actualizada — TelecomBoliviaNet";
        var html    = BuildPasswordChangedHtml(toName);

        await SendAsync(toEmail, subject, html,
            stub: $"[EMAIL_STUB] Notificación de cambio de contraseña para {toEmail}");
    }

    public async Task SendAccountActivationAsync(string toEmail, string toName, string activationLink)
    {
        var subject = "Activa tu cuenta — TelecomBoliviaNet";
        var html    = BuildAccountActivationHtml(toName, activationLink);

        await SendAsync(toEmail, subject, html,
            stub: $"[EMAIL_STUB] Activación de cuenta para {toEmail} — enlace: {activationLink}");
    }

    // ── Envío real o STUB ─────────────────────────────────────────────────────

    private async Task SendAsync(string toEmail, string subject, string html, string stub)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            // Sin API Key: modo desarrollo — loguea el contenido para pruebas manuales.
            // No lanza excepción para que el flujo de negocio continúe normalmente.
            _logger.LogWarning("{Stub}", stub);
            return;
        }

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = new
        {
            from    = $"{_fromName} <{_fromAddress}>",
            to      = new[] { toEmail },
            subject = subject,
            html    = html,
        };

        try
        {
            var response = await _http.PostAsJsonAsync(ResendApiUrl, payload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Resend error {Status}: {Body}", response.StatusCode, body);
            }
            else
            {
                _logger.LogInformation("Email enviado a {Email} — asunto: {Subject}", toEmail, subject);
            }
        }
        catch (Exception ex)
        {
            // No propagar: un fallo de email no debe retornar 500 al usuario.
            _logger.LogError(ex, "Excepción enviando email a {Email}", toEmail);
        }
    }

    // ── Templates HTML ────────────────────────────────────────────────────────

    private static string BuildPasswordResetHtml(string name, string resetLink) => $"""
        <!DOCTYPE html>
        <html lang="es">
        <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#f4f6f9;font-family:Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f9;padding:40px 0;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,0.08);">
                <!-- Header -->
                <tr><td style="background:#1e40af;padding:32px 40px;text-align:center;">
                  <h1 style="margin:0;color:#ffffff;font-size:22px;font-weight:700;letter-spacing:-0.3px;">TelecomBoliviaNet</h1>
                  <p style="margin:6px 0 0;color:#93c5fd;font-size:13px;">Sistema de Gestión</p>
                </td></tr>
                <!-- Cuerpo -->
                <tr><td style="padding:40px;">
                  <h2 style="margin:0 0 12px;color:#111827;font-size:20px;font-weight:700;">Recuperación de contraseña</h2>
                  <p style="margin:0 0 20px;color:#4b5563;font-size:15px;line-height:1.6;">
                    Hola <strong>{name}</strong>, recibimos una solicitud para restablecer la contraseña de tu cuenta.
                    Haz clic en el botón para continuar:
                  </p>
                  <table cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
                    <tr><td style="background:#1e40af;border-radius:8px;padding:14px 32px;text-align:center;">
                      <a href="{resetLink}" style="color:#ffffff;text-decoration:none;font-size:15px;font-weight:600;">
                        Restablecer contraseña
                      </a>
                    </td></tr>
                  </table>
                  <p style="margin:0 0 8px;color:#6b7280;font-size:13px;line-height:1.6;">
                    Si no solicitaste este cambio, puedes ignorar este correo — tu contraseña seguirá siendo la misma.
                  </p>
                  <p style="margin:0;color:#9ca3af;font-size:12px;">Este enlace expira en <strong>1 hora</strong>.</p>
                </td></tr>
                <!-- Footer -->
                <tr><td style="background:#f9fafb;border-top:1px solid #e5e7eb;padding:20px 40px;text-align:center;">
                  <p style="margin:0;color:#9ca3af;font-size:12px;">
                    TelecomBoliviaNet · Cochabamba, Bolivia<br>
                    Si tienes problemas, contacta al administrador del sistema.
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string BuildAccountActivationHtml(string name, string activationLink) => $"""
        <!DOCTYPE html>
        <html lang="es">
        <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#f4f6f9;font-family:Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f9;padding:40px 0;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,0.08);">
                <tr><td style="background:#1e40af;padding:32px 40px;text-align:center;">
                  <h1 style="margin:0;color:#ffffff;font-size:22px;font-weight:700;letter-spacing:-0.3px;">TelecomBoliviaNet</h1>
                  <p style="margin:6px 0 0;color:#93c5fd;font-size:13px;">Sistema de Gestión</p>
                </td></tr>
                <tr><td style="padding:40px;">
                  <h2 style="margin:0 0 12px;color:#111827;font-size:20px;font-weight:700;">Activa tu cuenta</h2>
                  <p style="margin:0 0 20px;color:#4b5563;font-size:15px;line-height:1.6;">
                    Hola <strong>{name}</strong>, el administrador ha creado una cuenta para ti en TelecomBoliviaNet.
                    Haz clic en el botón para activar tu cuenta y elegir tu contraseña:
                  </p>
                  <table cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
                    <tr><td style="background:#1e40af;border-radius:8px;padding:14px 32px;text-align:center;">
                      <a href="{activationLink}" style="color:#ffffff;text-decoration:none;font-size:15px;font-weight:600;">
                        Activar mi cuenta
                      </a>
                    </td></tr>
                  </table>
                  <p style="margin:0 0 8px;color:#6b7280;font-size:13px;line-height:1.6;">
                    Si no esperabas este correo, puedes ignorarlo sin problema.
                  </p>
                  <p style="margin:0;color:#9ca3af;font-size:12px;">Este enlace expira en <strong>48 horas</strong>.</p>
                </td></tr>
                <tr><td style="background:#f9fafb;border-top:1px solid #e5e7eb;padding:20px 40px;text-align:center;">
                  <p style="margin:0;color:#9ca3af;font-size:12px;">
                    TelecomBoliviaNet · Cochabamba, Bolivia<br>
                    Si tienes problemas con el enlace, contacta al administrador del sistema.
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string BuildPasswordChangedHtml(string name) => $"""
        <!DOCTYPE html>
        <html lang="es">
        <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#f4f6f9;font-family:Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f9;padding:40px 0;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,0.08);">
                <tr><td style="background:#1e40af;padding:32px 40px;text-align:center;">
                  <h1 style="margin:0;color:#ffffff;font-size:22px;font-weight:700;">TelecomBoliviaNet</h1>
                  <p style="margin:6px 0 0;color:#93c5fd;font-size:13px;">Sistema de Gestión</p>
                </td></tr>
                <tr><td style="padding:40px;">
                  <h2 style="margin:0 0 12px;color:#111827;font-size:20px;font-weight:700;">Contraseña actualizada</h2>
                  <p style="margin:0 0 20px;color:#4b5563;font-size:15px;line-height:1.6;">
                    Hola <strong>{name}</strong>, tu contraseña fue restablecida exitosamente.
                  </p>
                  <div style="background:#f0fdf4;border:1px solid #bbf7d0;border-radius:8px;padding:16px 20px;margin-bottom:20px;">
                    <p style="margin:0;color:#166534;font-size:14px;">
                      ✓ Tu cuenta está protegida con tu nueva contraseña.
                    </p>
                  </div>
                  <p style="margin:0;color:#6b7280;font-size:13px;line-height:1.6;">
                    Si no realizaste este cambio, contacta de inmediato al administrador del sistema.
                  </p>
                </td></tr>
                <tr><td style="background:#f9fafb;border-top:1px solid #e5e7eb;padding:20px 40px;text-align:center;">
                  <p style="margin:0;color:#9ca3af;font-size:12px;">
                    TelecomBoliviaNet · Cochabamba, Bolivia
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
}
