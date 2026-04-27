#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981
using System.Text.Json;
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
/// Servicio para gestión de configuración, plantillas, segmentos, historial y cancelaciones.
/// US-35, US-37, US-38, US-39, US-NOT-02, US-NOT-03, US-NOT-04, US-NOT-VARS,
/// US-NOT-PREVIEW, US-NOT-ANTISPAM.
/// </summary>
public class NotifConfigService
{
    private readonly IGenericRepository<NotifConfig>             _configRepo;
    private readonly IGenericRepository<NotifPlantilla>          _plantillaRepo;
    private readonly IGenericRepository<NotifPlantillaHistorial> _historialRepo;
    private readonly IGenericRepository<NotifOutbox>             _outboxRepo;
    private readonly IGenericRepository<NotifLog>                _logRepo;
    private readonly IGenericRepository<NotifSegment>            _segmentRepo;
    private readonly IGenericRepository<Client>                  _clientRepo;
    private readonly IGenericRepository<Invoice>                 _invoiceRepo;
    private readonly IGenericRepository<SystemConfig>            _sysConfigRepo;
    private readonly AuditService                                _audit;

    // ── Variables dinámicas disponibles (US-NOT-VARS) ────────────────────────
    public static readonly Dictionary<string, string> VariableDescriptions = new()
    {
        ["{{nombre}}"]            = "Primer nombre del cliente",
        ["{{apellido}}"]          = "Apellido del cliente",
        ["{{nombre_completo}}"]   = "Nombre completo del cliente",
        ["{{deuda}}"]             = "Deuda total pendiente en Bs.",
        ["{{monto}}"]             = "Monto de la factura o pago",
        ["{{periodo}}"]           = "Período de la factura (ej: Enero 2026)",
        ["{{fecha_vencimiento}}"] = "Fecha de vencimiento de la factura",
        ["{{plan}}"]              = "Nombre del plan del cliente",
        ["{{zona}}"]              = "Zona del cliente",
        ["{{empresa}}"]           = "Nombre del ISP (SystemConfig)",
        ["{{dias_mora}}"]         = "Días de mora de la factura más antigua",
        ["{{meses_mora}}"]        = "Meses de mora",
        ["{{meses_pendientes}}"]  = "Cantidad de meses con facturas pendientes",
        ["{{fecha_corte}}"]       = "Fecha de corte configurada",
        ["{{num_ticket}}"]        = "Número correlativo del ticket (TK-AAAA-NNNN)",
        ["{{tecnico}}"]           = "Nombre del técnico asignado al ticket",
        ["{{fecha_visita}}"]      = "Fecha programada de visita técnica",
    };

    // Textos por defecto para restaurar (US-37)
    private static readonly Dictionary<NotifType, string> DefaultTextos = new()
    {
        [NotifType.SUSPENSION]        = "Estimado/a {{nombre}},\n\nSu servicio *{{plan}}* ha sido *suspendido* por falta de pago.\nComuníquese con nosotros para regularizar.\n\n*{{empresa}}*",
        [NotifType.REACTIVACION]      = "Estimado/a {{nombre}},\n\nSu servicio *{{plan}}* ha sido *reactivado*. ¡Ya puede usarlo con normalidad!\n\n*{{empresa}}*",
        [NotifType.RECORDATORIO_R1]   = "Estimado/a {{nombre}},\n\nLe recordamos que tiene una factura de *Bs. {{monto}}* con vencimiento el *{{fecha_vencimiento}}* ({{meses_pendientes}} mes(es) pendiente(s)).\n\n*{{empresa}}*",
        [NotifType.RECORDATORIO_R2]   = "Estimado/a {{nombre}},\n\n⚠️ Su factura de *Bs. {{monto}}* vence el *{{fecha_vencimiento}}*. Evite la suspensión pagando a tiempo.\n\n*{{empresa}}*",
        [NotifType.RECORDATORIO_R3]   = "Estimado/a {{nombre}},\n\n🚨 Su factura vence *mañana* ({{fecha_vencimiento}}). Monto: *Bs. {{monto}}*. Pague hoy para no perder el servicio.\n\n*{{empresa}}*",
        [NotifType.FACTURA_VENCIDA]   = "Estimado/a {{nombre}},\n\nSu factura del periodo *{{periodo}}* por *Bs. {{monto}}* está *vencida*. Regularice su pago para evitar la suspensión.\n\n*{{empresa}}*",
        [NotifType.CONFIRMACION_PAGO] = "Estimado/a {{nombre}},\n\n✅ Hemos registrado su pago de *Bs. {{monto}}* correspondiente al periodo *{{periodo}}*.\n\nGracias por su pago. *{{empresa}}*",
        [NotifType.TICKET_CREADO]     = "Estimado/a {{nombre}},\n\nSu solicitud de soporte ha sido registrada con el número *{{num_ticket}}*.\n\nLe atenderemos a la brevedad. *{{empresa}}*",
        [NotifType.TICKET_RESUELTO]   = "Estimado/a {{nombre}},\n\nSu ticket *{{num_ticket}}* ha sido *resuelto* por {{tecnico}}.\n\nSi tiene alguna consulta adicional, contáctenos. *{{empresa}}*",
        [NotifType.CAMBIO_PLAN]       = "Estimado/a {{nombre}},\n\nSu plan de servicio ha sido actualizado a *{{plan}}*.\n\nEl cambio es efectivo inmediatamente. *{{empresa}}*",
    };

    public NotifConfigService(
        IGenericRepository<NotifConfig>             configRepo,
        IGenericRepository<NotifPlantilla>          plantillaRepo,
        IGenericRepository<NotifPlantillaHistorial> historialRepo,
        IGenericRepository<NotifOutbox>             outboxRepo,
        IGenericRepository<NotifLog>                logRepo,
        IGenericRepository<NotifSegment>            segmentRepo,
        IGenericRepository<Client>                  clientRepo,
        IGenericRepository<Invoice>                 invoiceRepo,
        IGenericRepository<SystemConfig>            sysConfigRepo,
        AuditService                                audit)
    {
        _configRepo    = configRepo;
        _plantillaRepo = plantillaRepo;
        _historialRepo = historialRepo;
        _outboxRepo    = outboxRepo;
        _logRepo       = logRepo;
        _segmentRepo   = segmentRepo;
        _clientRepo    = clientRepo;
        _invoiceRepo   = invoiceRepo;
        _sysConfigRepo = sysConfigRepo;
        _audit         = audit;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-35/38 · Configuración de triggers
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<NotifConfigListDto> GetConfigsAsync()
    {
        var configs = await _configRepo.GetAll().OrderBy(c => c.Tipo).ToListAsync();
        var horaLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, NotifShared.BoliviaZone).ToString("HH:mm");
        return new NotifConfigListDto(configs.Select(ToDto).ToList(), horaLocal);
    }

    /// <summary>US-35/38 / US-NOT-04 · Actualizar configs incluyendo PlantillaId.</summary>
    public async Task<r> UpdateConfigsAsync(
        UpdateNotifConfigsDto dto, Guid actorId, string actorName, string ip)
    {
        foreach (var upd in dto.Configs)
        {
            // US-NOT-04: proteger trigger de Suspensión de desactivación sin confirmación
            // (se valida en el controller con un flag de confirmación)
            var config = await _configRepo.GetAll().FirstOrDefaultAsync(c => c.Tipo == upd.Tipo);
            if (config is null) continue;

            var prev = JsonSerializer.Serialize(ToDto(config));
            config.Activo            = upd.Activo;
            config.DelaySegundos     = upd.DelaySegundos;
            config.HoraInicio        = TimeOnly.Parse(upd.HoraInicio);
            config.HoraFin           = TimeOnly.Parse(upd.HoraFin);
            config.Inmediato         = upd.Inmediato;
            config.DiasAntes         = upd.DiasAntes;
            config.PlantillaId       = upd.PlantillaId;   // US-NOT-04
            config.ActualizadoAt     = DateTime.UtcNow;
            config.ActualizadoPorId  = actorId;
            await _configRepo.UpdateAsync(config);

            await _audit.LogAsync("Notificaciones", "NOTIF_CONFIG_UPDATED",
                $"Config actualizada: {upd.Tipo}",
                userId: actorId, userName: actorName, ip: ip,
                prevData: prev, newData: JsonSerializer.Serialize(upd));
        }
        return Result.Success();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-37 / US-NOT-03 · Plantillas con Categoría y HsmStatus
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<NotifPlantillaDto>> GetPlantillasAsync(
        PlantillaCategoria? categoriaFilter = null,
        HsmStatus? hsmFilter = null)
    {
        var query = _plantillaRepo.GetAll().Where(p => p.Activa);

        if (categoriaFilter.HasValue)
            query = query.Where(p => p.Categoria == categoriaFilter.Value);
        if (hsmFilter.HasValue)
            query = query.Where(p => p.HsmStatus == hsmFilter.Value);

        var list = await query.OrderBy(p => p.Tipo).ToListAsync();
        return list.Select(ToPlantillaDto).ToList();
    }

    public async Task<r> UpdatePlantillaAsync(
        NotifType tipo, UpdateNotifPlantillaDto dto, Guid actorId, string actorName, string ip)
    {
        var actual = await _plantillaRepo.GetAll()
            .FirstOrDefaultAsync(p => p.Tipo == tipo && p.Activa);

        if (actual is null)
            return Result.Failure($"No se encontró plantilla activa para el tipo {tipo}.");

        // US-NOT-03: no permitir activar plantilla Rechazada en trigger
        // (validación adicional en controller al actualizar trigger.PlantillaId)

        await _historialRepo.AddAsync(new NotifPlantillaHistorial
        {
            PlantillaId    = actual.Id,
            Tipo           = actual.Tipo,
            Texto          = actual.Texto,
            ArchivadoAt    = DateTime.UtcNow,
            ArchivadoPorId = actorId
        });

        var prevTexto = actual.Texto;
        actual.Texto      = dto.Texto;
        actual.Categoria  = dto.Categoria;   // US-NOT-03
        actual.HsmStatus  = dto.HsmStatus;   // US-NOT-03
        actual.CreadoPorId = actorId;
        actual.CreadoAt   = DateTime.UtcNow;
        await _plantillaRepo.UpdateAsync(actual);

        await _audit.LogAsync("Notificaciones", "NOTIF_PLANTILLA_UPDATED",
            $"Plantilla actualizada: {tipo}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: prevTexto, newData: dto.Texto);
        return Result.Success();
    }

    /// <summary>US-NOT-03 · Actualizar solo el estado HSM (admin mientras no hay API Meta).</summary>
    public async Task<r> UpdateHsmStatusAsync(
        NotifType tipo, HsmStatus nuevoStatus, Guid actorId, string actorName, string ip)
    {
        var actual = await _plantillaRepo.GetAll()
            .FirstOrDefaultAsync(p => p.Tipo == tipo && p.Activa);

        if (actual is null)
            return Result.Failure($"Plantilla no encontrada para tipo {tipo}.");

        var prev = actual.HsmStatus.ToString();
        actual.HsmStatus = nuevoStatus;
        await _plantillaRepo.UpdateAsync(actual);

        await _audit.LogAsync("Notificaciones", "NOTIF_HSM_UPDATED",
            $"HSM status actualizado: {tipo} → {nuevoStatus}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: prev, newData: nuevoStatus.ToString());
        return Result.Success();
    }

    /// <summary>US-37 · Restaurar texto por defecto.</summary>
    public async Task<r> RestoreDefaultAsync(
        NotifType tipo, Guid actorId, string actorName, string ip)
    {
        if (!DefaultTextos.TryGetValue(tipo, out var defaultText))
            return Result.Failure($"No hay texto por defecto definido para {tipo}.");

        return await UpdatePlantillaAsync(tipo,
            new UpdateNotifPlantillaDto(defaultText),
            actorId, actorName, ip);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-NOT-VARS · Variables disponibles + Preview
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>US-NOT-VARS · Lista de variables con descripción.</summary>
    public Dictionary<string, string> GetVariablesDisponibles() => VariableDescriptions;

    /// <summary>
    /// US-NOT-PREVIEW · Renderiza un texto de plantilla con datos reales de un cliente.
    /// Resalta variables no encontradas.
    /// </summary>
    public async Task<PlantillaPreviewDto> PreviewPlantillaAsync(string texto, Guid? clienteId = null)
    {
        Dictionary<string, string> ctx;

        if (clienteId.HasValue)
        {
            ctx = await BuildContextoAsync(clienteId.Value, null, null, null);
        }
        else
        {
            // Usar primer cliente activo
            var primerCliente = await _clientRepo.GetAll()
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.CreatedAt)
                .Select(c => c.Id)
                .FirstOrDefaultAsync();

            ctx = primerCliente != Guid.Empty
                ? await BuildContextoAsync(primerCliente, null, null, null)
                : new Dictionary<string, string>();
        }

        var empresaNombre = await GetEmpresaNombreAsync();
        ctx["empresa"] = empresaNombre;

        var rendered      = texto;
        var noEncontradas = new List<string>();

        foreach (var variable in VariableDescriptions.Keys)
        {
            var key = variable.Trim('{', '}');
            if (ctx.TryGetValue(key, out var valor) && !string.IsNullOrEmpty(valor))
            {
                rendered = rendered.Replace(variable, valor);
            }
            else if (rendered.Contains(variable))
            {
                // Marcar como no encontrada pero dejar la variable en el texto
                noEncontradas.Add(variable);
            }
        }

        return new PlantillaPreviewDto(rendered, noEncontradas);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-NOT-02 · Segmentos de destinatarios
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<NotifSegmentDto>> GetSegmentsAsync()
    {
        var segments = await _segmentRepo.GetAll().OrderBy(s => s.Nombre).ToListAsync();
        return segments.Select(s => ToSegmentDto(s, null)).ToList();
    }

    public async Task<Result<NotifSegmentDto>> GetSegmentByIdAsync(Guid id)
    {
        var seg = await _segmentRepo.GetByIdAsync(id);
        if (seg is null) return Result<NotifSegmentDto>.Failure("Segmento no encontrado.");
        var preview = await EvaluateSegmentCountAsync(seg);
        return Result<NotifSegmentDto>.Success(ToSegmentDto(seg, preview));
    }

    public async Task<Result<NotifSegmentDto>> CreateSegmentAsync(
        CreateOrUpdateSegmentDto dto, Guid actorId, string actorName, string ip)
    {
        if (!dto.Reglas.Any() || dto.Reglas.All(g => !g.Condiciones.Any()))
            return Result<NotifSegmentDto>.Failure("El segmento debe tener al menos 1 condición.");

        if (await _segmentRepo.GetAll().AnyAsync(s => s.Nombre == dto.Nombre))
            return Result<NotifSegmentDto>.Failure($"Ya existe un segmento con el nombre '{dto.Nombre}'.");

        var seg = new NotifSegment
        {
            Nombre       = dto.Nombre,
            Descripcion  = dto.Descripcion,
            ReglasJson   = JsonSerializer.Serialize(dto.Reglas),
            CreadoAt     = DateTime.UtcNow,
            CreadoPorId  = actorId,
        };
        await _segmentRepo.AddAsync(seg);

        await _audit.LogAsync("Notificaciones", "NOTIF_SEGMENT_CREATED",
            $"Segmento creado: {dto.Nombre}",
            userId: actorId, userName: actorName, ip: ip);

        var preview = await EvaluateSegmentCountAsync(seg);
        return Result<NotifSegmentDto>.Success(ToSegmentDto(seg, preview));
    }

    public async Task<Result<NotifSegmentDto>> UpdateSegmentAsync(
        Guid id, CreateOrUpdateSegmentDto dto, Guid actorId, string actorName, string ip)
    {
        var seg = await _segmentRepo.GetByIdAsync(id);
        if (seg is null) return Result<NotifSegmentDto>.Failure("Segmento no encontrado.");

        if (!dto.Reglas.Any() || dto.Reglas.All(g => !g.Condiciones.Any()))
            return Result<NotifSegmentDto>.Failure("El segmento debe tener al menos 1 condición.");

        var duplicado = await _segmentRepo.GetAll()
            .AnyAsync(s => s.Nombre == dto.Nombre && s.Id != id);
        if (duplicado)
            return Result<NotifSegmentDto>.Failure($"Ya existe un segmento con el nombre '{dto.Nombre}'.");

        var prev = seg.ReglasJson;
        seg.Nombre           = dto.Nombre;
        seg.Descripcion      = dto.Descripcion;
        seg.ReglasJson       = JsonSerializer.Serialize(dto.Reglas);
        seg.ActualizadoAt    = DateTime.UtcNow;
        seg.ActualizadoPorId = actorId;
        await _segmentRepo.UpdateAsync(seg);

        await _audit.LogAsync("Notificaciones", "NOTIF_SEGMENT_UPDATED",
            $"Segmento actualizado: {dto.Nombre}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: prev, newData: seg.ReglasJson);

        var preview = await EvaluateSegmentCountAsync(seg);
        return Result<NotifSegmentDto>.Success(ToSegmentDto(seg, preview));
    }

    public async Task<r> DeleteSegmentAsync(Guid id, Guid actorId, string actorName, string ip)
    {
        var seg = await _segmentRepo.GetByIdAsync(id);
        if (seg is null) return Result.Failure("Segmento no encontrado.");

        await _segmentRepo.DeleteAsync(seg.Id);
        await _audit.LogAsync("Notificaciones", "NOTIF_SEGMENT_DELETED",
            $"Segmento eliminado: {seg.Nombre}",
            userId: actorId, userName: actorName, ip: ip);
        return Result.Success();
    }

    /// <summary>US-NOT-02 · Preview: cuántos clientes coinciden con el segmento.</summary>
    public async Task<SegmentPreviewDto> PreviewSegmentAsync(CreateOrUpdateSegmentDto dto)
    {
        var tempSeg = new NotifSegment { ReglasJson = JsonSerializer.Serialize(dto.Reglas) };
        var count = await EvaluateSegmentCountAsync(tempSeg);
        return new SegmentPreviewDto(count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-NOT-ANTISPAM · Envío masivo con anti-spam
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// US-NOT-ANTISPAM · Envío masivo: inserta en outbox con deduplicación 24h.
    /// </summary>
    public async Task<Result<EnvioMasivoResultDto>> EnvioMasivoAsync(
        EnvioMasivoDto dto, Guid actorId, string actorName, string ip)
    {
        // 1. Obtener config del tipo
        var config = await _configRepo.GetAll()
            .FirstOrDefaultAsync(c => c.Tipo == dto.Tipo);
        if (config is null || !config.Activo)
            return Result<EnvioMasivoResultDto>.Failure($"El tipo {dto.Tipo} está desactivado.");

        // 2. Obtener clientes del segmento (o todos si no hay segmento)
        List<Client> clientes;
        if (dto.SegmentId.HasValue)
        {
            var seg = await _segmentRepo.GetByIdAsync(dto.SegmentId.Value);
            if (seg is null) return Result<EnvioMasivoResultDto>.Failure("Segmento no encontrado.");
            clientes = await GetClientesFromSegmentAsync(seg);
        }
        else
        {
            clientes = await _clientRepo.GetAll()
                .Where(c => !c.IsDeleted && c.Status == ClientStatus.Activo)
                .ToListAsync();
        }

        var ventana24h   = DateTime.UtcNow.AddHours(-24);
        int enviados     = 0;
        int omitidos     = 0;
        int sinTelefono  = 0;

        foreach (var cliente in clientes)
        {
            if (string.IsNullOrWhiteSpace(cliente.PhoneMain))
            {
                sinTelefono++;
                continue;
            }

            // US-NOT-ANTISPAM: verificar si ya recibió este tipo en 24h
            var yaRecibio = await _logRepo.GetAll()
                .AnyAsync(l => l.ClienteId == cliente.Id
                            && l.Tipo      == dto.Tipo
                            && l.Estado    == NotifLogEstado.ENVIADO
                            && l.RegistradoAt >= ventana24h);

            if (yaRecibio)
            {
                // Registrar como OMITIDO_ANTISPAM
                await _logRepo.AddAsync(new NotifLog
                {
                    OutboxId     = Guid.Empty,
                    ClienteId    = cliente.Id,
                    Tipo         = dto.Tipo,
                    PhoneNumber  = cliente.PhoneMain,
                    Mensaje      = string.Empty,
                    Estado       = NotifLogEstado.OMITIDO,
                    IntentoNum   = 0,
                    ErrorDetalle = "OMITIDO_ANTISPAM: mensaje enviado en las últimas 24h",
                    RegistradoAt = DateTime.UtcNow
                });
                omitidos++;
                continue;
            }

            var outbox = new NotifOutbox
            {
                Tipo         = dto.Tipo,
                ClienteId    = cliente.Id,
                PhoneNumber  = cliente.PhoneMain,
                Publicado    = false,
                Intentos     = 0,
                EnviarDesde  = DateTime.UtcNow.AddSeconds(config.DelaySegundos),
                EstadoFinal  = null,
                CreadoAt     = DateTime.UtcNow,
                ContextoJson = "{}", // el worker enriquece el contexto al procesar
            };
            await _outboxRepo.AddAsync(outbox);
            enviados++;
        }

        await _audit.LogAsync("Notificaciones", "NOTIF_ENVIO_MASIVO",
            $"Envío masivo tipo={dto.Tipo} enviados={enviados} omitidos={omitidos}",
            userId: actorId, userName: actorName, ip: ip);

        return Result<EnvioMasivoResultDto>.Success(new EnvioMasivoResultDto(enviados, omitidos, sinTelefono));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-36 · Historial de notificaciones por cliente
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<NotifLogPageDto> GetHistorialClienteAsync(
        Guid clienteId, int page, int pageSize,
        string? tipoFilter, DateTime? desde, DateTime? hasta)
    {
        var query = _logRepo.GetAll().Where(l => l.ClienteId == clienteId);

        if (!string.IsNullOrWhiteSpace(tipoFilter) && Enum.TryParse<NotifType>(tipoFilter, out var tipo))
            query = query.Where(l => l.Tipo == tipo);
        if (desde.HasValue) query = query.Where(l => l.RegistradoAt >= desde.Value);
        if (hasta.HasValue) query = query.Where(l => l.RegistradoAt <= hasta.Value);

        var total = await query.CountAsync();
        var logs  = await query
            .OrderByDescending(l => l.RegistradoAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var outboxIds = logs.Select(l => l.OutboxId).Where(id => id != Guid.Empty).ToList();
        var outboxMap = await _outboxRepo.GetAll()
            .Where(o => outboxIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.EnviarDesde);

        var items = logs.Select(l => new NotifLogItemDto(
            l.Id, l.OutboxId, l.Tipo, l.Tipo.ToString(), l.Estado.ToString(),
            l.PhoneNumber, l.Mensaje, l.IntentoNum, l.ErrorDetalle, l.RegistradoAt,
            outboxMap.TryGetValue(l.OutboxId, out var env) ? env : null
        )).ToList();

        return new NotifLogPageDto(items, total, page, pageSize);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-39 · Cancelación
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<r> CancelNotifAsync(Guid notifId, Guid actorId, string actorName, string ip)
    {
        var outbox = await _outboxRepo.GetByIdAsync(notifId);
        if (outbox is null) return Result.Failure("Notificación no encontrada.");
        if (outbox.EstadoFinal.HasValue)
            return Result.Failure($"La notificación ya tiene estado final: {outbox.EstadoFinal}.");

        outbox.EstadoFinal = NotifEstadoFinal.CANCELADO;
        outbox.Publicado   = true;
        outbox.ProcesadoAt = DateTime.UtcNow;
        await _outboxRepo.UpdateAsync(outbox);

        await _logRepo.AddAsync(new NotifLog
        {
            OutboxId = outbox.Id, ClienteId = outbox.ClienteId, Tipo = outbox.Tipo,
            PhoneNumber = outbox.PhoneNumber, Mensaje = string.Empty,
            Estado = NotifLogEstado.CANCELADO, IntentoNum = outbox.Intentos, RegistradoAt = DateTime.UtcNow
        });

        await _audit.LogAsync("Notificaciones", "NOTIF_CANCELLED",
            $"Notificación cancelada: {outbox.Id} tipo={outbox.Tipo}",
            userId: actorId, userName: actorName, ip: ip);
        return Result.Success();
    }

    public async Task<Result<CancelMasivaResultDto>> CancelMasivaAsync(
        NotifType tipo, string? razon, Guid actorId, string actorName, string ip)
    {
        var pendientes = await _outboxRepo.GetAll()
            .Where(o => o.Tipo == tipo && o.EstadoFinal == null).ToListAsync();

        foreach (var o in pendientes)
        {
            o.EstadoFinal = NotifEstadoFinal.CANCELADO;
            o.Publicado   = true;
            o.ProcesadoAt = DateTime.UtcNow;
            await _outboxRepo.UpdateAsync(o);
            await _logRepo.AddAsync(new NotifLog
            {
                OutboxId = o.Id, ClienteId = o.ClienteId, Tipo = o.Tipo,
                PhoneNumber = o.PhoneNumber, Mensaje = string.Empty,
                Estado = NotifLogEstado.CANCELADO, IntentoNum = o.Intentos,
                ErrorDetalle = razon, RegistradoAt = DateTime.UtcNow
            });
        }

        await _audit.LogAsync("Notificaciones", "NOTIF_MASIVA_CANCELADA",
            $"Cancelación masiva tipo={tipo} cantidad={pendientes.Count} razón={razon}",
            userId: actorId, userName: actorName, ip: ip);

        return Result<CancelMasivaResultDto>.Success(new CancelMasivaResultDto(pendientes.Count));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers privados
    // ═══════════════════════════════════════════════════════════════════════

    private static NotifConfigDto ToDto(NotifConfig c) => new(
        c.Tipo, c.Activo, c.DelaySegundos,
        c.HoraInicio.ToString("HH:mm"), c.HoraFin.ToString("HH:mm"),
        c.Inmediato, c.DiasAntes, c.PlantillaId);

    private static NotifPlantillaDto ToPlantillaDto(NotifPlantilla p) => new(
        p.Id, p.Tipo, p.Texto, p.Activa, p.Categoria, p.HsmStatus, p.CreadoAt);

    private static NotifSegmentDto ToSegmentDto(NotifSegment s, int? preview)
    {
        var reglas = string.IsNullOrEmpty(s.ReglasJson)
            ? new List<SegmentConditionGroup>()
            : JsonSerializer.Deserialize<List<SegmentConditionGroup>>(s.ReglasJson)
              ?? new List<SegmentConditionGroup>();
        return new NotifSegmentDto(s.Id, s.Nombre, s.Descripcion, reglas, s.CreadoAt, preview);
    }

    /// <summary>
    /// Evalúa cuántos clientes coinciden con las reglas de un segmento.
    /// Los grupos (OR) contienen condiciones (AND).
    /// Los filtros sobre deuda y mora requieren cruzar con Invoices en memoria.
    /// </summary>
    private async Task<int> EvaluateSegmentCountAsync(NotifSegment seg)
    {
        if (string.IsNullOrEmpty(seg.ReglasJson)) return 0;

        var grupos = JsonSerializer.Deserialize<List<SegmentConditionGroup>>(seg.ReglasJson)
            ?? new List<SegmentConditionGroup>();
        if (!grupos.Any()) return 0;

        var clientes = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Where(c => !c.IsDeleted)
            .ToListAsync();

        // Para deuda y mora necesitamos facturas pendientes
        var clientIds = clientes.Select(c => c.Id).ToList();
        var invoices  = await _invoiceRepo.GetAll()
            .Where(i => clientIds.Contains(i.ClientId)
                     && (i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida))
            .ToListAsync();

        var invoicesByClient = invoices.GroupBy(i => i.ClientId)
            .ToDictionary(g => g.Key, g => g.ToList());

        int count = 0;
        foreach (var c in clientes)
        {
            invoicesByClient.TryGetValue(c.Id, out var clientInvoices);
            clientInvoices ??= new List<Invoice>();

            // OR entre grupos, AND entre condiciones del mismo grupo
            bool matches = grupos.Any(grupo =>
                grupo.Condiciones.All(cond => EvaluaCondicion(c, clientInvoices, cond)));

            if (matches) count++;
        }
        return count;
    }

    private async Task<List<Client>> GetClientesFromSegmentAsync(NotifSegment seg)
    {
        if (string.IsNullOrEmpty(seg.ReglasJson)) return new();

        var grupos = JsonSerializer.Deserialize<List<SegmentConditionGroup>>(seg.ReglasJson)
            ?? new List<SegmentConditionGroup>();

        var clientes = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Where(c => !c.IsDeleted)
            .ToListAsync();

        var clientIds = clientes.Select(c => c.Id).ToList();
        var invoices  = await _invoiceRepo.GetAll()
            .Where(i => clientIds.Contains(i.ClientId)
                     && (i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida))
            .ToListAsync();
        var invoicesByClient = invoices.GroupBy(i => i.ClientId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return clientes.Where(c =>
        {
            invoicesByClient.TryGetValue(c.Id, out var inv);
            inv ??= new List<Invoice>();
            return grupos.Any(g => g.Condiciones.All(cond => EvaluaCondicion(c, inv, cond)));
        }).ToList();
    }

    private static bool EvaluaCondicion(Client c, List<Invoice> inv, SegmentCondition cond)
    {
        try
        {
            return cond.Campo switch
            {
                "zona"   => ComparaString(c.Zone,                          cond.Operador, cond.Valor),
                "plan"   => ComparaString(c.Plan?.Name ?? string.Empty,   cond.Operador, cond.Valor),
                "estado" => ComparaString(c.Status.ToString(),             cond.Operador, cond.Valor),
                "deuda"  => ComparaDecimal(inv.Sum(i => i.Amount),        cond.Operador, decimal.Parse(cond.Valor)),
                "dias_mora" => ComparaDecimal(
                    inv.Any() ? (decimal)(DateTime.UtcNow - inv.Min(i => i.DueDate)).TotalDays : 0,
                    cond.Operador, decimal.Parse(cond.Valor)),
                _ => false
            };
        }
        catch { return false; }
    }

    private static bool ComparaString(string actual, string op, string valor)
        => op switch
        {
            "="  => actual.Equals(valor, StringComparison.OrdinalIgnoreCase),
            "!=" => !actual.Equals(valor, StringComparison.OrdinalIgnoreCase),
            _    => false
        };

    private static bool ComparaDecimal(decimal actual, string op, decimal valor)
        => op switch
        {
            "="  => actual == valor,
            "!=" => actual != valor,
            ">"  => actual > valor,
            "<"  => actual < valor,
            ">=" => actual >= valor,
            "<=" => actual <= valor,
            _    => false
        };

    /// <summary>US-NOT-VARS · Construye contexto completo para un cliente.</summary>
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

        var deudaTotal  = invoices.Sum(i => i.Amount);
        var diasMora    = invoices.Any()
            ? (int)(DateTime.UtcNow - invoices.First().DueDate).TotalDays
            : 0;
        var mesesMora   = (diasMora / 30).ToString();
        var mesesPend   = invoices.Count.ToString();

        var empresa = await GetEmpresaNombreAsync();
        var partes   = client.FullName.Split(' ', 2);

        return new Dictionary<string, string>
        {
            ["nombre"]            = partes.Length > 0 ? partes[0] : client.FullName,
            ["apellido"]          = partes.Length > 1 ? partes[1] : string.Empty,
            ["nombre_completo"]   = client.FullName,
            ["deuda"]             = deudaTotal.ToString("F2"),
            ["monto"]             = invoices.FirstOrDefault()?.Amount.ToString("F2") ?? "0.00",
            ["periodo"]           = invoices.FirstOrDefault()?.Month is int m && invoices.FirstOrDefault()?.Year is int y
                                    ? $"{System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)} {y}"
                                    : string.Empty,
            ["fecha_vencimiento"] = invoices.FirstOrDefault()?.DueDate.ToString("dd/MM/yyyy") ?? string.Empty,
            ["plan"]              = client.Plan?.Name ?? string.Empty,
            ["zona"]              = client.Zone,
            ["empresa"]           = empresa,
            ["dias_mora"]         = diasMora.ToString(),
            ["meses_mora"]        = mesesMora,
            ["meses_pendientes"]  = mesesPend,
            ["fecha_corte"]       = string.Empty, // configurable en SystemConfig
            ["num_ticket"]        = ticketId.HasValue ? ticketId.Value.ToString() : string.Empty,
            ["tecnico"]           = tecnicoNombre ?? string.Empty,
            ["fecha_visita"]      = fechaVisita?.ToString("dd/MM/yyyy") ?? string.Empty,
        };
    }

    private async Task<string> GetEmpresaNombreAsync()
    {
        var cfg = await _sysConfigRepo.GetAll()
            .FirstOrDefaultAsync(s => s.Key == "ISP:NombreEmpresa");
        return cfg?.Value ?? "TelecomBoliviaNet";
    }
}
