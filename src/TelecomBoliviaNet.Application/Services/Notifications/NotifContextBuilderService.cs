using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Notifications;

/// <summary>
/// Construye el diccionario de contexto completo para sustituir variables en plantillas.
/// Centraliza la lógica de enriquecimiento de datos que antes estaba duplicada en
/// NotifConfigService y NotifEnvioService.
///
/// Variables producidas:
///   nombre, apellido, nombre_completo, deuda, monto, periodo,
///   fecha_vencimiento, plan, zona, empresa, dias_mora, meses_mora,
///   meses_pendientes, fecha_corte, num_ticket, tecnico, fecha_visita,
///   meses_deuda_detalle (lista formateada de meses adeudados con montos),
///   qr_enlace (URL directa al QR de pago o imagen del QR)
/// </summary>
public class NotifContextBuilderService
{
    private static readonly string[] SpanishMonths =
    {
        "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    };

    private readonly IGenericRepository<Client>      _clientRepo;
    private readonly IGenericRepository<Invoice>     _invoiceRepo;
    private readonly IGenericRepository<ClientQr>   _qrRepo;
    private readonly IGenericRepository<SystemConfig> _sysConfigRepo;
    private readonly IConfiguration                   _configuration;

    public NotifContextBuilderService(
        IGenericRepository<Client>       clientRepo,
        IGenericRepository<Invoice>      invoiceRepo,
        IGenericRepository<ClientQr>    qrRepo,
        IGenericRepository<SystemConfig>  sysConfigRepo,
        IConfiguration                    configuration)
    {
        _clientRepo    = clientRepo;
        _invoiceRepo   = invoiceRepo;
        _qrRepo        = qrRepo;
        _sysConfigRepo = sysConfigRepo;
        _configuration = configuration;
    }

    /// <summary>
    /// Construye el contexto completo para un cliente.
    /// Params opcionales se usan solo para tipos de notificación específicos (tickets, visitas).
    /// </summary>
    public virtual async Task<Dictionary<string, string>> BuildAsync(
        Guid    clienteId,
        Guid?   ticketId      = null,
        string? tecnicoNombre = null,
        DateTime? fechaVisita = null)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == clienteId);

        if (client is null) return new Dictionary<string, string>();

        // ── Facturas pendientes ordenadas por fecha de vencimiento ───────────
        var invoicesPendientes = await _invoiceRepo.GetAll()
            .Where(i => i.ClientId == clienteId
                     && !i.IsDeleted
                     && (i.Status == InvoiceStatus.Pendiente
                      || i.Status == InvoiceStatus.Vencida
                      || i.Status == InvoiceStatus.ParcialmentePagada))
            .OrderBy(i => i.DueDate)
            .ToListAsync();

        var deudaTotal = invoicesPendientes.Sum(i => i.Amount - i.AmountPaid);
        var primerFactura = invoicesPendientes.FirstOrDefault();

        var diasMora = invoicesPendientes.Any()
            ? Math.Max(0, (int)(DateTime.UtcNow - invoicesPendientes.Min(i => i.DueDate)).TotalDays)
            : 0;

        var mesesMora = diasMora / 30;

        // ── Detalle de meses adeudados ───────────────────────────────────────
        var mesesDeudaDetalle = BuildMesesDeudaDetalle(invoicesPendientes);

        // ── QR de pago ───────────────────────────────────────────────────────
        var qrEnlace = await BuildQrEnlaceAsync(clienteId);

        // ── Nombre de empresa ────────────────────────────────────────────────
        var empresaNombre = await GetEmpresaNombreAsync();

        // ── Separar nombre y apellido ────────────────────────────────────────
        var partes = (client.FullName ?? string.Empty).Split(' ', 2, StringSplitOptions.TrimEntries);

        return new Dictionary<string, string>
        {
            ["nombre"]               = partes.Length > 0 ? partes[0] : client.FullName ?? string.Empty,
            ["apellido"]             = partes.Length > 1 ? partes[1] : string.Empty,
            ["nombre_completo"]      = client.FullName ?? string.Empty,
            ["deuda"]                = deudaTotal.ToString("F2"),
            ["monto"]                = primerFactura is not null
                                          ? (primerFactura.Amount - primerFactura.AmountPaid).ToString("F2")
                                          : "0.00",
            ["periodo"]              = primerFactura is not null
                                          ? BuildPeriodoLabel(primerFactura)
                                          : string.Empty,
            ["fecha_vencimiento"]    = primerFactura?.DueDate.ToString("dd/MM/yyyy") ?? string.Empty,
            ["plan"]                 = client.Plan?.Name ?? string.Empty,
            ["zona"]                 = client.Zone ?? string.Empty,
            ["empresa"]              = empresaNombre,
            ["dias_mora"]            = diasMora.ToString(),
            ["meses_mora"]           = mesesMora.ToString(),
            ["meses_pendientes"]     = invoicesPendientes.Count.ToString(),
            ["fecha_corte"]          = await GetFechaCorteAsync(),
            ["num_ticket"]           = ticketId.HasValue ? ticketId.Value.ToString() : string.Empty,
            ["tecnico"]              = tecnicoNombre ?? string.Empty,
            ["fecha_visita"]         = fechaVisita?.ToString("dd/MM/yyyy") ?? string.Empty,
            // Nuevas variables
            ["meses_deuda_detalle"]  = mesesDeudaDetalle,
            ["qr_enlace"]            = qrEnlace,
        };
    }

    // ── Helpers privados ────────────────────────────────────────────────────

    /// <summary>
    /// Formatea la lista de facturas pendientes como texto para WhatsApp.
    /// Ej:
    ///   • Enero 2025 - Bs. 150.00
    ///   • Febrero 2025 - Bs. 150.00 (vence 28/02/2025)
    /// </summary>
    private static string BuildMesesDeudaDetalle(List<Invoice> invoices)
    {
        if (!invoices.Any()) return "_(Sin facturas pendientes)_";

        var lineas = invoices.Select(inv =>
        {
            var saldo = inv.Amount - inv.AmountPaid;
            var label = inv.Type == InvoiceType.Instalacion
                ? "Instalación"
                : inv.Month is >= 1 and <= 12
                    ? $"{SpanishMonths[inv.Month]} {inv.Year}"
                    : $"{inv.Year}";

            var vence = inv.Status == InvoiceStatus.Vencida
                ? $" _(vencida {inv.DueDate:dd/MM/yyyy})_"
                : $" _(vence {inv.DueDate:dd/MM/yyyy})_";

            return $"• *{label}* - Bs. {saldo:F2}{vence}";
        });

        return string.Join("\n", lineas);
    }

    /// <summary>
    /// Construye el enlace al QR de pago del cliente.
    /// Prioridad: URL de imagen del QR activo > portal URL configurado > mensaje de fallback.
    /// </summary>
    private async Task<string> BuildQrEnlaceAsync(Guid clienteId)
    {
        // 1. Buscar QR activo y vigente del cliente
        var qr = await _qrRepo.GetAll()
            .Where(q => q.ClientId == clienteId
                     && q.IsActive
                     && (q.ExpiresAt == null || q.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(q => q.UploadedAt)
            .FirstOrDefaultAsync();

        if (qr is not null && !string.IsNullOrWhiteSpace(qr.ImageUrl))
        {
            // Prefijamos con la base URL del backend si la imagen es una ruta relativa
            var baseUrl = await GetBaseUrlAsync();
            return qr.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? qr.ImageUrl
                : $"{baseUrl.TrimEnd('/')}/{qr.ImageUrl.TrimStart('/')}";
        }

        // 2. Fallback: portal de pago configurable
        var portalUrl = await GetConfigValueAsync("ISP:PortalPagoUrl");
        if (!string.IsNullOrWhiteSpace(portalUrl))
            return $"{portalUrl.TrimEnd('/')}/pay/{clienteId}";

        // 3. Sin QR configurado
        return "Contáctenos para obtener su código QR de pago.";
    }

    private static string BuildPeriodoLabel(Invoice inv)
    {
        if (inv.Type == InvoiceType.Instalacion) return "Instalación";
        return inv.Month is >= 1 and <= 12
            ? $"{SpanishMonths[inv.Month]} {inv.Year}"
            : inv.Year.ToString();
    }

    private async Task<string> GetEmpresaNombreAsync()
        => await GetConfigValueAsync("ISP:NombreEmpresa") ?? "TelecomBoliviaNet";

    private async Task<string> GetFechaCorteAsync()
        => await GetConfigValueAsync("ISP:FechaCorte") ?? string.Empty;

    private async Task<string> GetBaseUrlAsync()
    {
        var fromConfig = await GetConfigValueAsync("ISP:BackendUrl");
        return fromConfig
            ?? _configuration["BACKEND_PUBLIC_URL"]
            ?? "http://localhost:5000";
    }

    private async Task<string?> GetConfigValueAsync(string key)
    {
        var cfg = await _sysConfigRepo.GetAll()
            .FirstOrDefaultAsync(s => s.Key == key);
        return cfg?.Value;
    }
}
