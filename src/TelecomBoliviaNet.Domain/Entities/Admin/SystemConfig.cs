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
}
