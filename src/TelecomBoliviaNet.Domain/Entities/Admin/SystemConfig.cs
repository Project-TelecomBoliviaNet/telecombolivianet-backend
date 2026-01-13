using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Admin;

/// <summary>
/// Configuración de sistema persistida en base de datos (clave/valor).
/// Reemplaza appsettings.runtime.json para entornos multi-réplica o con
/// filesystem efímero (Kubernetes, contenedores sin volumen persistente).
///
/// Claves definidas en SystemConfigKeys para evitar magic strings.
/// </summary>
public class SystemConfig : Entity
{
    /// <summary>Clave única. Usar constantes de SystemConfigKeys.</summary>
    public string Key       { get; set; } = string.Empty;

    public string Value     { get; set; } = string.Empty;

    /// <summary>Descripción legible para el panel de administración.</summary>
    public string? Description { get; set; }

    /// <summary>Si true, el valor se oculta en logs y respuestas API (tokens, passwords).</summary>
    public bool   IsSecret  { get; set; } = false;

    public DateTime UpdatedAt  { get; set; } = DateTime.UtcNow;
    public Guid?    UpdatedById { get; set; }
}

/// <summary>
/// Constantes de claves para SystemConfig — elimina magic strings en todo el codebase.
/// </summary>
public static class SystemConfigKeys
{
    public const string WhatsAppToken          = "WhatsApp:Token";
    public const string WhatsAppPhoneNumberId  = "WhatsApp:PhoneNumberId";
    public const string WhatsAppApiVersion     = "WhatsApp:ApiVersion";
    public const string SlaHorasAnticipacion   = "SlaAlert:HorasAnticipacion";
    public const string SlaHoraInicioLaboral   = "SlaAlert:HoraInicioLaboral";
    public const string SlaHoraFinLaboral      = "SlaAlert:HoraFinLaboral";
    public const string MaxFailedLoginAttempts = "Security:MaxFailedLoginAttempts";
    /// <summary>Fecha de vencimiento del QR de pago de la empresa (ISO 8601 UTC).</summary>
    public const string CompanyQrExpiresAt     = "Bot:CompanyQrExpiresAt";
    /// <summary>Número WhatsApp del administrador que recibe alertas operativas (8 dígitos sin prefijo).</summary>
    public const string AdminPhoneNumber       = "Bot:AdminPhoneNumber";
    /// <summary>Última fecha en que se envió alerta de QR empresa (yyyy-MM-dd Bolivia). Anti-duplicado.</summary>
    public const string CompanyQrAlertSentDate = "Bot:CompanyQrAlertSentDate";

    // ── BillingBackgroundJob — persistencia en BD para idempotencia multi-réplica ──
    /// <summary>Último mes en que se generaron facturas (formato "YYYY-MM").</summary>
    public const string BillingLastGeneratedMonth = "billing.lastGeneratedMonth";
    /// <summary>Último mes en que se marcaron facturas vencidas (formato "YYYY-MM").</summary>
    public const string BillingLastVoidedMonth    = "billing.lastVoidedMonth";
    /// <summary>Último mes en que se enviaron recordatorios de pago (formato "YYYY-MM").</summary>
    public const string BillingLastReminderMonth  = "billing.lastReminderMonth";

    // ── M12 — Suspensión automática por deuda ─────────────────────────────────
    /// <summary>Activa/desactiva la suspensión automática de clientes con deuda vencida.</summary>
    public const string AutoSuspendActivo     = "AutoSuspend:Activo";
    /// <summary>Días de gracia tras el vencimiento antes de suspender automáticamente.</summary>
    public const string AutoSuspendDiasGracia = "AutoSuspend:DiasGracia";
}
