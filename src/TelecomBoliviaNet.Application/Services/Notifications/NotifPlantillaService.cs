using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Notifications;

/// <summary>
/// SRP: gestiona plantillas de WhatsApp, variables y preview de mensajes.
/// US-37 / US-NOT-03 / US-NOT-VARS / US-NOT-PREVIEW.
/// Extraído de NotifConfigService (Problema #6).
/// </summary>
public class NotifPlantillaService
{
    private readonly IGenericRepository<NotifPlantilla>          _repo;
    private readonly IGenericRepository<NotifPlantillaHistorial> _historialRepo;
    private readonly IGenericRepository<Client>                  _clientRepo;
    private readonly IGenericRepository<Invoice>                 _invoiceRepo;
    private readonly IGenericRepository<SystemConfig>            _sysConfigRepo;
    private readonly AuditService                                _audit;

    public NotifPlantillaService(
        IGenericRepository<NotifPlantilla>          repo,
        IGenericRepository<NotifPlantillaHistorial> historialRepo,
        IGenericRepository<Client>                  clientRepo,
        IGenericRepository<Invoice>                 invoiceRepo,
        IGenericRepository<SystemConfig>            sysConfigRepo,
        AuditService                                audit)
    {
        _repo          = repo;
        _historialRepo = historialRepo;
        _clientRepo    = clientRepo;
        _invoiceRepo   = invoiceRepo;
        _sysConfigRepo = sysConfigRepo;
        _audit         = audit;
    }

    // ── US-37 / US-NOT-03 ────────────────────────────────────────────────────

    public async Task<List<NotifPlantillaDto>> GetPlantillasAsync(
        PlantillaCategoria? categoriaFilter = null, HsmStatus? hsmFilter = null)
    {
        var query = _repo.GetAll().Where(p => p.Activa);
        if (categoriaFilter.HasValue) query = query.Where(p => p.Categoria == categoriaFilter.Value);
        if (hsmFilter.HasValue)       query = query.Where(p => p.HsmStatus  == hsmFilter.Value);
        var list = await query.OrderBy(p => p.Tipo).ToListAsync();
        return list.Select(NotifShared.ToPlantillaDto).ToList();
    }

    public async Task<Result> UpdatePlantillaAsync(
        NotifType tipo, UpdateNotifPlantillaDto dto, Guid actorId, string actorName, string ip)
    {
        var actual = await _repo.GetAll()
            .FirstOrDefaultAsync(p => p.Tipo == tipo && p.Activa);

        if (actual is null)
            return Result.Failure($"No se encontró plantilla activa para el tipo {tipo}.");

        await _historialRepo.AddAsync(new NotifPlantillaHistorial
        {
            PlantillaId    = actual.Id,
            Tipo           = actual.Tipo,
            Texto          = actual.Texto,
            ArchivadoAt    = DateTime.UtcNow,
            ArchivadoPorId = actorId,
        });

        var prevTexto  = actual.Texto;
        actual.Texto   = dto.Texto;
        actual.Categoria  = dto.Categoria;
        actual.HsmStatus  = dto.HsmStatus;
        actual.CreadoPorId = actorId;
        actual.CreadoAt    = DateTime.UtcNow;
        await _repo.UpdateAsync(actual);

        await _audit.LogAsync("Notificaciones", "NOTIF_PLANTILLA_UPDATED",
            $"Plantilla actualizada: {tipo}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: prevTexto, newData: dto.Texto);

        return Result.Success();
    }

    public async Task<Result> UpdateHsmStatusAsync(
        NotifType tipo, HsmStatus nuevoStatus, Guid actorId, string actorName, string ip)
    {
        var actual = await _repo.GetAll()
            .FirstOrDefaultAsync(p => p.Tipo == tipo && p.Activa);
        if (actual is null) return Result.Failure($"Plantilla no encontrada para tipo {tipo}.");

        var prev = actual.HsmStatus.ToString();
        actual.HsmStatus = nuevoStatus;
        await _repo.UpdateAsync(actual);

        await _audit.LogAsync("Notificaciones", "NOTIF_HSM_UPDATED",
            $"HSM status actualizado: {tipo} → {nuevoStatus}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: prev, newData: nuevoStatus.ToString());

        return Result.Success();
    }

    public async Task<Result> RestoreDefaultAsync(
        NotifType tipo, Guid actorId, string actorName, string ip)
    {
        if (!NotifShared.DefaultTextos.TryGetValue(tipo, out var defaultText))
            return Result.Failure($"No hay texto por defecto definido para {tipo}.");

        return await UpdatePlantillaAsync(tipo,
            new UpdateNotifPlantillaDto(defaultText), actorId, actorName, ip);
    }

    // ── US-NOT-VARS / US-NOT-PREVIEW ─────────────────────────────────────────

    public IReadOnlyDictionary<string, string> GetVariablesDisponibles()
        => NotifShared.VariableDescriptions;

    public async Task<PlantillaPreviewDto> PreviewPlantillaAsync(
        string texto, Guid? clienteId = null)
    {
        var ctx = clienteId.HasValue
            ? await BuildContextoAsync(clienteId.Value, null, null, null)
            : await BuildContextoAsync(
                await GetPrimerClienteIdAsync(), null, null, null);

        ctx["empresa"] = await GetEmpresaNombreAsync();

        var rendered      = texto;
        var noEncontradas = new List<string>();

        foreach (var variable in NotifShared.VariableDescriptions.Keys)
        {
            var key = variable.Trim('{', '}');
            if (ctx.TryGetValue(key, out var valor) && !string.IsNullOrEmpty(valor))
                rendered = rendered.Replace(variable, valor);
            else if (rendered.Contains(variable))
                noEncontradas.Add(variable);
        }

        return new PlantillaPreviewDto(rendered, noEncontradas);
    }

    public async Task<Dictionary<string, string>> BuildContextoAsync(
        Guid clienteId, Guid? ticketId, string? tecnicoNombre, DateTime? fechaVisita)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == clienteId);

        if (client is null) return new();

        var invoices = await _invoiceRepo.GetAll()
            .Where(i => i.ClientId == clienteId
                     && (i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida))
            .OrderBy(i => i.DueDate)
            .ToListAsync();

        var deudaTotal = invoices.Sum(i => i.Amount);
        var diasMora   = invoices.Any()
            ? (int)(DateTime.UtcNow - invoices.First().DueDate).TotalDays
            : 0;
        var empresa    = await GetEmpresaNombreAsync();
        var partes     = client.FullName.Split(' ', 2);

        return new Dictionary<string, string>
        {
            ["nombre"]            = partes.Length > 0 ? partes[0] : client.FullName,
            ["apellido"]          = partes.Length > 1 ? partes[1] : string.Empty,
            ["nombre_completo"]   = client.FullName,
            ["deuda"]             = deudaTotal.ToString("F2"),
            ["monto"]             = invoices.FirstOrDefault()?.Amount.ToString("F2") ?? "0.00",
            ["periodo"]           = FormatPeriodo(invoices.FirstOrDefault()),
            ["fecha_vencimiento"] = invoices.FirstOrDefault()?.DueDate.ToString("dd/MM/yyyy") ?? string.Empty,
            ["plan"]              = client.Plan?.Name ?? string.Empty,
            ["zona"]              = client.Zone,
            ["empresa"]           = empresa,
            ["dias_mora"]         = diasMora.ToString(),
            ["meses_mora"]        = (diasMora / 30).ToString(),
            ["meses_pendientes"]  = invoices.Count.ToString(),
            ["fecha_corte"]       = string.Empty,
            ["num_ticket"]        = ticketId?.ToString() ?? string.Empty,
            ["tecnico"]           = tecnicoNombre ?? string.Empty,
            ["fecha_visita"]      = fechaVisita?.ToString("dd/MM/yyyy") ?? string.Empty,
        };
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private static string FormatPeriodo(Invoice? inv)
    {
        if (inv is null) return string.Empty;
        // BUG FIX: GetMonthName(0) lanza ArgumentOutOfRangeException cuando inv.Month es 0
        // (facturas de instalación u otros casos edge). Validar rango antes de llamar.
        if (inv.Month < 1 || inv.Month > 12) return $"{inv.Year}";
        var culture = new System.Globalization.CultureInfo("es-BO");
        return $"{culture.DateTimeFormat.GetMonthName(inv.Month)} {inv.Year}";
    }

    private async Task<Guid> GetPrimerClienteIdAsync()
        => await _clientRepo.GetAll()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.Id)
            .FirstOrDefaultAsync();

    private async Task<string> GetEmpresaNombreAsync()
    {
        var cfg = await _sysConfigRepo.GetAll()
            .FirstOrDefaultAsync(s => s.Key == "ISP:NombreEmpresa");
        return cfg?.Value ?? "TelecomBoliviaNet";
    }
}
