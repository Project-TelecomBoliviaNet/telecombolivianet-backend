using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Bot;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Bot;

/// <summary>
/// M10: US-BOT-06 / US-BOT-02 — Configuración del bot persistida en SystemConfig.
/// La clave JSON de configuración es "Bot:Config".
/// El chatbot NestJS puede consultarla vía un endpoint GET /api/bot-config.
/// </summary>
public class BotConfigService : IBotConfigService
{
    private const string CONFIG_KEY = "Bot:Config";

    private readonly IGenericRepository<SystemConfig> _sysRepo;
    private readonly AuditService                     _audit;

    private static readonly BotConfigDto _default = new(
        Menu: new BotMenuDto(
            "¿En qué puedo ayudarte hoy?",
            [
                new("1", "Ver mi deuda",        "CONSULTA_DEUDA",         true),
                new("2", "Solicitar QR de pago","SOLICITAR_QR",           true),
                new("3", "Reportar falla",       "SIN_CONEXION",          true),
                new("4", "Abrir un ticket",      "SOLICITAR_AGENTE",      true),
                new("5", "Hablar con un agente", "SOLICITAR_AGENTE",      true),
            ]),
        Horario: new BotHorarioDto(
            "08:00", "20:00",
            [true, true, true, true, true, false, false],
            "Nuestro horario de atención es de lunes a viernes de 8:00 a 20:00. " +
            "Puedes dejar tu consulta y te responderemos al inicio del próximo día hábil."),
        Mensajes: new BotMensajesDto(
            Bienvenida:     "¡Hola {{nombre}}! 👋 Soy el asistente virtual de *TelecomBoliviaNet*. ¿En qué puedo ayudarte?",
            Despedida:      "Fue un placer ayudarte. Que tengas un excelente día. *TelecomBoliviaNet*",
            NoEntendido:    "Disculpa, no entendí tu consulta. ¿Puedes reformularla o elegir una opción del menú?",
            EscaladoAgente: "Te estoy conectando con un agente de soporte. Un momento por favor... 🔄"),
        PalabrasClave: ["menu", "menú", "ayuda", "hola", "inicio", "0"]
    );

    public BotConfigService(
        IGenericRepository<SystemConfig> sysRepo,
        AuditService                     audit)
    {
        _sysRepo = sysRepo;
        _audit   = audit;
    }

    public async Task<BotConfigDto> GetAsync()
    {
        var cfg = await _sysRepo.GetAll()
            .FirstOrDefaultAsync(c => c.Key == CONFIG_KEY);

        if (cfg is null || string.IsNullOrWhiteSpace(cfg.Value))
            return _default;

        try { return JsonSerializer.Deserialize<BotConfigDto>(cfg.Value)!; }
        catch { return _default; }
    }

    public async Task<BotConfigDto> UpdateAsync(
        BotConfigDto dto, Guid actorId, string actorName, string ip)
    {
        var cfg = await _sysRepo.GetAll()
            .FirstOrDefaultAsync(c => c.Key == CONFIG_KEY);

        var json    = JsonSerializer.Serialize(dto);
        var prevVal = cfg?.Value ?? "(default)";

        if (cfg is null)
        {
            await _sysRepo.AddAsync(new SystemConfig
            {
                Key         = CONFIG_KEY,
                Value       = json,
                Description = "Configuración del chatbot WhatsApp",
            });
            await _sysRepo.SaveChangesAsync();
        }
        else
        {
            cfg.Value     = json;
            cfg.UpdatedAt = DateTime.UtcNow;
            await _sysRepo.UpdateAsync(cfg);
        }

        await _audit.LogAsync("Chatbot", "BOT_CONFIG_UPDATED",
            "Configuración del bot actualizada",
            userId: actorId, userName: actorName, ip: ip,
            prevData: prevVal, newData: json);

        return dto;
    }

    public BotConfigDto GetDefault() => _default;
}
