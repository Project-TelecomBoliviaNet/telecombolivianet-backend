#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Notifications;

/// <summary>
/// Facade de compatibilidad para NotifConfigController.
/// Toda la lógica real vive en los servicios SRP especializados:
///   - NotifTriggerService   (US-35, US-38, US-NOT-04)
///   - NotifPlantillaService (US-37, US-NOT-03, US-NOT-VARS, US-NOT-PREVIEW)
///   - NotifSegmentService   (US-NOT-02)
///   - NotifHistorialService (US-36)
/// Esta clase solo delega; no contiene lógica propia.
/// </summary>
public class NotifConfigService
{
    private readonly NotifTriggerService   _trigger;
    private readonly NotifPlantillaService _plantilla;
    private readonly NotifSegmentService   _segment;
    private readonly NotifHistorialService _historial;

    public NotifConfigService(
        NotifTriggerService   trigger,
        NotifPlantillaService plantilla,
        NotifSegmentService   segment,
        NotifHistorialService historial)
    {
        _trigger   = trigger;
        _plantilla = plantilla;
        _segment   = segment;
        _historial = historial;
    }

    // ── Triggers (US-35 / US-38 / US-NOT-04) ────────────────────────────────

    public Task<NotifConfigListDto> GetConfigsAsync()
        => _trigger.GetConfigsAsync();

    public Task<Result> UpdateConfigsAsync(
        UpdateNotifConfigsDto dto, Guid actorId, string actorName, string ip)
        => _trigger.UpdateConfigsAsync(dto, actorId, actorName, ip);

    // ── Plantillas (US-37 / US-NOT-03) ──────────────────────────────────────

    public Task<List<NotifPlantillaDto>> GetPlantillasAsync(
        PlantillaCategoria? categoriaFilter = null,
        HsmStatus? hsmFilter = null)
        => _plantilla.GetPlantillasAsync(categoriaFilter, hsmFilter);

    public Task<Result> UpdatePlantillaAsync(
        NotifType tipo, UpdateNotifPlantillaDto dto,
        Guid actorId, string actorName, string ip)
        => _plantilla.UpdatePlantillaAsync(tipo, dto, actorId, actorName, ip);

    public Task<Result> UpdateHsmStatusAsync(
        NotifType tipo, HsmStatus nuevoStatus,
        Guid actorId, string actorName, string ip)
        => _plantilla.UpdateHsmStatusAsync(tipo, nuevoStatus, actorId, actorName, ip);

    public Task<Result> RestoreDefaultAsync(
        NotifType tipo, Guid actorId, string actorName, string ip)
        => _plantilla.RestoreDefaultAsync(tipo, actorId, actorName, ip);

    // ── Variables y preview (US-NOT-VARS / US-NOT-PREVIEW) ──────────────────

    public IReadOnlyDictionary<string, string> GetVariablesDisponibles()
        => _plantilla.GetVariablesDisponibles();

    public Task<PlantillaPreviewDto> PreviewPlantillaAsync(
        string texto, Guid? clienteId = null)
        => _plantilla.PreviewPlantillaAsync(texto, clienteId);

    // ── Segmentos (US-NOT-02) ────────────────────────────────────────────────

    public Task<List<NotifSegmentDto>> GetSegmentsAsync()
        => _segment.GetSegmentsAsync();

    public Task<Result<NotifSegmentDto>> GetSegmentByIdAsync(Guid id)
        => _segment.GetSegmentByIdAsync(id);

    public Task<Result<NotifSegmentDto>> CreateSegmentAsync(
        CreateOrUpdateSegmentDto dto, Guid actorId, string actorName, string ip)
        => _segment.CreateAsync(dto, actorId, actorName, ip);

    public Task<Result<NotifSegmentDto>> UpdateSegmentAsync(
        Guid id, CreateOrUpdateSegmentDto dto, Guid actorId, string actorName, string ip)
        => _segment.UpdateAsync(id, dto, actorId, actorName, ip);

    public Task<r> DeleteSegmentAsync(
        Guid id, Guid actorId, string actorName, string ip)
        => _segment.DeleteAsync(id, actorId, actorName, ip);

    public Task<SegmentPreviewDto> PreviewSegmentAsync(CreateOrUpdateSegmentDto dto)
        => _segment.PreviewAsync(dto);

    // ── Historial por cliente (US-36) ────────────────────────────────────────

    public Task<NotifLogPageDto> GetHistorialClienteAsync(
        Guid clienteId, int page, int pageSize,
        string? tipoFilter, DateTime? desde, DateTime? hasta)
        => _historial.GetHistorialClienteAsync(
            clienteId, page, pageSize, tipoFilter, desde, hasta);

    // ── Contexto de variables (US-NOT-VARS) ──────────────────────────────────

    public Task<Dictionary<string, string>> BuildContextoAsync(
        Guid clienteId, Guid? ticketId, string? tecnicoNombre, DateTime? fechaVisita)
        => _plantilla.BuildContextoAsync(clienteId, ticketId, tecnicoNombre, fechaVisita);
}
