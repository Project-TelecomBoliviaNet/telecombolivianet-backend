namespace TelecomBoliviaNet.Application.DTOs.Bot;

// ════════════════════════════════════════════════════════════════════════════
// M10: US-BOT-06 / US-BOT-02 · Configuración del bot desde UI admin
// ════════════════════════════════════════════════════════════════════════════

/// <summary>Configuración completa del bot editable desde el panel admin.</summary>
public record BotConfigDto(
    BotMenuDto      Menu,
    BotHorarioDto   Horario,
    BotMensajesDto  Mensajes,
    List<string>?   PalabrasClave = null
);

/// <summary>Opciones del menú principal del bot.</summary>
public record BotMenuDto(
    string               TituloMenu,
    List<BotMenuItemDto> Opciones,
    string               TituloBoton,    // label del botón que abre la lista ("Ver opciones")
    string               TituloSeccion   // título de la sección dentro de la lista
);

public record BotMenuItemDto(
    string  Numero,        // "1", "2", etc. — referencia visual en el panel
    string  Etiqueta,      // título del row en WhatsApp (máx 24 chars)
    string  Intent,        // CONSULTA_DEUDA | SOLICITAR_QR | etc. — se usa como id del botón
    bool    Activa,
    string? Descripcion,   // subtítulo del row en WhatsApp (máx 72 chars)
    bool    SoloCliente    // si true, la opción solo se muestra a clientes identificados
);

/// <summary>Horario de atención del bot (fuera de horario responde diferente).</summary>
public record BotHorarioDto(
    string  HoraInicio,   // "08:00"
    string  HoraFin,      // "20:00"
    bool[]  DiasActivos,  // [L,M,X,J,V,S,D] = [true,true,true,true,true,false,false]
    string  MensajeFueraHorario
);

/// <summary>Mensajes clave editables del bot.</summary>
public record BotMensajesDto(
    string  Bienvenida,
    string  BienvenidaProspecto, // saludo para contactos sin cuenta identificada
    string  NoEntendido,         // inyectado al prompt del agente como instrucción de fallback
    string  EscaladoAgente       // mensaje al escalar a humano
);

public record UpdateBotConfigDto(BotConfigDto Config);

// ════════════════════════════════════════════════════════════════════════════
// M10: US-BOT-01 · Bandeja unificada de conversaciones
// ════════════════════════════════════════════════════════════════════════════

public record ConversationListItemDto(
    string   Id,
    string   PhoneNumber,
    string?  ClientId,
    string?  ClientName,
    bool     IsEscalated,
    string?  AgentName,
    string?  EscaladoAt,
    string   UpdatedAt,
    string   CreatedAt,
    string?  UltimoMensaje,
    int      TotalMessages
);

public record ConversationDetailDto(
    string   Id,
    string   PhoneNumber,
    string?  ClientId,
    string?  ClientName,
    bool     IsEscalated,
    string?  AgentName,
    List<ConversationMessageDto> Messages,
    string?  UpdatedAt = null,
    string?  CreatedAt = null
);

public record ConversationMessageDto(
    string   Id,
    string   Role,     // user | bot | admin
    string?  Source,
    string   Content,
    string   CreatedAt,
    string?  MediaUrl,    // F4: URL del audio (vía /chatbot-uploads/)
    string?  MediaType    // F4: "audio" | null
);

public record ConversationStatsDto(
    int TotalConversaciones,
    int Escaladas,
    int HoyConversaciones,
    int HoyMensajes
);

// ════════════════════════════════════════════════════════════════════════════
// M10: US-BOT-07 · Historial de conversaciones por cliente
// ════════════════════════════════════════════════════════════════════════════

public record ClientConversationHistoryDto(
    string   PhoneNumber,
    List<ConversationListItemDto> Conversaciones
);

// ════════════════════════════════════════════════════════════════════════════
// M10: Intervención de agente — tomar / devolver / responder conversación
// ════════════════════════════════════════════════════════════════════════════

public record SendMessageRequestDto(string Texto);
