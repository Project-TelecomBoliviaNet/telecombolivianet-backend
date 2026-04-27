using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Notifications;

public enum NotifEstadoFinal
{
    ENVIADO,
    FALLIDO,
    CANCELADO,
    OMITIDO
}

/// <summary>
/// Cola de envíos pendientes. El monolito escribe, el Worker consume (US-30..US-34, US-39).
/// </summary>
public class NotifOutbox : Entity
{
    public NotifType           Tipo            { get; set; }
    public Guid                ClienteId       { get; set; }
    public Client?             Cliente         { get; set; }
    public string              PhoneNumber     { get; set; } = string.Empty;
    /// <summary>Plantilla activa al momento de insertar (referencia informativa).</summary>
    public Guid?               PlantillaId     { get; set; }
    /// <summary>true cuando el Worker ya tomó el registro para procesar.</summary>
    public bool                Publicado       { get; set; } = false;
    public int                 Intentos        { get; set; } = 0;
    /// <summary>now() + delay_segundos. El Worker no procesa antes de esta hora.</summary>
    public DateTime            EnviarDesde     { get; set; }
    /// <summary>Calculado con backoff exponencial tras cada fallo.</summary>
    public DateTime?           ProximoIntento  { get; set; }
    /// <summary>NULL mientras está pendiente.</summary>
    public NotifEstadoFinal?   EstadoFinal     { get; set; }
    public DateTime            CreadoAt        { get; set; } = DateTime.UtcNow;
    public DateTime?           ProcesadoAt     { get; set; }
    /// <summary>Datos de contexto para sustituir variables de plantilla (JSON).</summary>
    public string              ContextoJson    { get; set; } = "{}";
    /// <summary>ID de referencia (factura, pago) para deduplicación de recordatorios.</summary>
    public Guid?               ReferenciaId    { get; set; }
}
