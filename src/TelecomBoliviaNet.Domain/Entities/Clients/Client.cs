using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Clients;

/// <summary>
/// Cliente del ISP TelecomBoliviaNet.
/// Contiene datos personales, ubicación e información de instalación.
/// </summary>
public class Client : Entity
{
    // ── Código único ────────────────────────────────────────────────────────
    /// <summary>Código correlativo único: TBN-0001, TBN-0152…</summary>
    public string TbnCode          { get; set; } = string.Empty;

    // ── Datos personales ────────────────────────────────────────────────────
    public string FullName         { get; set; } = string.Empty;
    public string IdentityCard     { get; set; } = string.Empty; // acepta letras por el complemento
    public string PhoneMain        { get; set; } = string.Empty; // número WhatsApp principal
    public string? PhoneSecondary  { get; set; }

    // ── Ubicación ───────────────────────────────────────────────────────────
    public string Zone             { get; set; } = string.Empty; // Loma Linda, Valle Encantado…
    public string? Street          { get; set; }                 // Calle 3, Nro. 45
    public string? LocationRef     { get; set; }                 // Frente a la cancha, portón azul
    public decimal? GpsLatitude    { get; set; }
    public decimal? GpsLongitude   { get; set; }

    // ── Datos de instalación ────────────────────────────────────────────────
    public string   WinboxNumber        { get; set; } = string.Empty;
    public DateTime InstallationDate    { get; set; }
    public Guid     InstalledByUserId   { get; set; }
    public UserSystem? InstalledBy      { get; set; }
    public Guid     PlanId              { get; set; }
    public Plan?    Plan                { get; set; }
    public bool     HasTvCable          { get; set; }
    public string?  OnuSerialNumber     { get; set; }

    // ── Estado ──────────────────────────────────────────────────────────────
    public ClientStatus Status      { get; set; } = ClientStatus.Activo;
    public DateTime?    SuspendedAt { get; set; }
    public DateTime?    CancelledAt { get; set; }

    // ── Auditoría ───────────────────────────────────────────────────────────
    public DateTime  CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt  { get; set; }

    // ── Soft Delete ─────────────────────────────────────────────────────────
    /// <summary>
    /// true = cliente eliminado lógicamente. Los registros no se borran físicamente
    /// para preservar el historial de facturas, pagos y tickets.
    /// Filtro global en AppDbContext excluye estos registros de todas las queries.
    /// </summary>
    // US-PAG-CREDITO — saldo a favor del cliente (no caduca)
    public decimal CreditBalance { get; set; } = 0m;

    // US-CLI-01 — email opcional para notificaciones y recuperación de contraseña
    public string? Email { get; set; }

    public bool      IsDeleted   { get; set; } = false;
    public DateTime? DeletedAt   { get; set; }
    public Guid?     DeletedById { get; set; }

    // ── Navegación ──────────────────────────────────────────────────────────
    public ICollection<Invoice>  Invoices  { get; set; } = new List<Invoice>();
    public ICollection<Payment>  Payments  { get; set; } = new List<Payment>();
}
