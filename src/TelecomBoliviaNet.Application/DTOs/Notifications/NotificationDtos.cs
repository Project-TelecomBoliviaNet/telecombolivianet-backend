using TelecomBoliviaNet.Domain.Entities.Notifications;

namespace TelecomBoliviaNet.Application.DTOs.Notifications;

// ── US-35/38 · Configuración ─────────────────────────────────────────────────

public record NotifConfigDto(
    NotifType Tipo,
    bool      Activo,
    int       DelaySegundos,
    string    HoraInicio,      // "HH:mm"
    string    HoraFin,         // "HH:mm"
    bool      Inmediato,
    int?      DiasAntes,
    Guid?     PlantillaId      // US-NOT-04
);

public record NotifConfigListDto(
    List<NotifConfigDto> Configs,
    string               HoraServidorLocal
);

public record UpdateNotifConfigDto(
    NotifType Tipo,
    bool      Activo,
    int       DelaySegundos,
    string    HoraInicio,
    string    HoraFin,
    bool      Inmediato,
    int?      DiasAntes,
    Guid?     PlantillaId      // US-NOT-04
);

public record UpdateNotifConfigsDto(List<UpdateNotifConfigDto> Configs);

// ── US-37 / US-NOT-03 · Plantillas ──────────────────────────────────────────

public record NotifPlantillaDto(
    Guid              Id,
    NotifType         Tipo,
    string            Texto,
    bool              Activa,
    PlantillaCategoria Categoria,    // US-NOT-03
    HsmStatus         HsmStatus,    // US-NOT-03
    DateTime          CreadoAt
);

public record UpdateNotifPlantillaDto(
    string             Texto,
    PlantillaCategoria Categoria = PlantillaCategoria.General,  // US-NOT-03
    HsmStatus          HsmStatus = HsmStatus.Aprobada           // US-NOT-03
);

public record UpdateHsmStatusDto(HsmStatus HsmStatus); // US-NOT-03 actualizar solo estado HSM

// ── US-36 · Historial de notificaciones ─────────────────────────────────────

public record NotifLogItemDto(
    Guid      Id,
    Guid      OutboxId,
    NotifType Tipo,
    string    TipoLabel,
    string    Estado,
    string    PhoneNumber,
    string    Mensaje,          // US-NOT-VARS: mensaje final renderizado
    int       IntentoNum,
    string?   ErrorDetalle,
    DateTime  RegistradoAt,
    DateTime? EnviarDesde
);

public record NotifLogPageDto(
    List<NotifLogItemDto> Items,
    int                   Total,
    int                   Page,
    int                   PageSize
);

// ── US-39 · Cancelación masiva ───────────────────────────────────────────────

public record CancelMasivaResultDto(int CancelledCount);

// ── US-NOT-02 · Segmentos ────────────────────────────────────────────────────

/// <summary>Una condición dentro de un grupo AND.</summary>
public record SegmentCondition(
    string Campo,      // deuda, dias_mora, zona, plan, estado
    string Operador,   // =, !=, >, <, >=, <=
    string Valor
);

/// <summary>Un grupo de condiciones AND. Los grupos se combinan con OR entre sí.</summary>
public record SegmentConditionGroup(List<SegmentCondition> Condiciones);

public record NotifSegmentDto(
    Guid                        Id,
    string                      Nombre,
    string?                     Descripcion,
    List<SegmentConditionGroup> Reglas,
    DateTime                    CreadoAt,
    int?                        ClientesEstimados  // preview de coincidencias
);

public record CreateOrUpdateSegmentDto(
    string                      Nombre,
    string?                     Descripcion,
    List<SegmentConditionGroup> Reglas
);

public record SegmentPreviewDto(int ClientesCoinciden);

// ── US-NOT-ANTISPAM · Anti-spam ──────────────────────────────────────────────

public record EnvioMasivoDto(
    NotifType Tipo,
    Guid?     SegmentId  // si null = todos los clientes activos
);

public record EnvioMasivoResultDto(int Enviados, int OmitidosAntispam, int SinTelefono);

// ── US-NOT-PREVIEW · Preview ─────────────────────────────────────────────────

public record PlantillaPreviewDto(
    string TextoRenderizado,
    List<string> VariablesNoEncontradas
);
