namespace TelecomBoliviaNet.Application.DTOs.Bot;

// ════════════════════════════════════════════════════════════════════════════
// M10: US-BOT-06 / US-BOT-02 · Configuración del bot desde UI admin
// ════════════════════════════════════════════════════════════════════════════

/// <summary>Configuración completa del bot editable desde el panel admin.</summary>
public record BotConfigDto(
    BotMenuDto      Menu,
    BotHorarioDto   Horario,
    BotMensajesDto  Mensajes,
    List<string>    PalabrasClave   // US-BOT-06: palabras que activan el menú
);

/// <summary>Opciones del menú principal del bot.</summary>
public record BotMenuDto(
    string          TituloMenu,
    List<BotMenuItemDto> Opciones
);

public record BotMenuItemDto(
    string  Numero,       // "1", "2", etc.
    string  Etiqueta,     // "Ver mi deuda"
    string  Intent,       // CONSULTA_DEUDA | SOLICITAR_QR | etc.
    bool    Activa
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
    string  Despedida,
    string  NoEntendido,   // cuando el LLM no reconoce intención
    string  EscaladoAgente // mensaje al escalar a humano
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
    List<ConversationMessageDto> Messages
);

public record ConversationMessageDto(
    string   Id,
    string   Role,     // user | bot | admin
    string?  Source,
    string   Content,
    string   CreatedAt
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
