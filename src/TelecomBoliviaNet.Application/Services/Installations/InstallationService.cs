using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Installations;

/// <summary>
/// Servicio del Módulo de Instalaciones.
///
/// Reglas de negocio:
///  - Un cliente Activo/Suspendido no puede tener 2 instalaciones Pendiente simultáneas
///  - Al crear → se genera automáticamente un SupportTicket tipo InstalacionNueva
///  - Los slots disponibles = (técnicos totales) - (instalaciones Pendiente/EnProceso en ese horario)
///  - Al completar → el ticket asociado pasa a Resuelto automáticamente
///  - Al cancelar por ADMIN → el bot recibe notificación vía POST /bot/notify-client
/// </summary>
public class InstallationService
{
    private readonly IGenericRepository<Installation>  _instRepo;
    private readonly IGenericRepository<Client>        _clientRepo;
    private readonly IGenericRepository<Plan>          _planRepo;
    private readonly IGenericRepository<UserSystem>    _userRepo;
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly AuditService                      _audit;

    // BUG FIX: TecnicosDisponibles ya no es una constante hardcodeada.
    // Se cuenta dinámicamente desde la BD para reflejar el equipo real del ISP.
    // Duración estándar de cada instalación en minutos
    private const int DuracionEstandarMin = 120;

    public InstallationService(
        IGenericRepository<Installation>  instRepo,
        IGenericRepository<Client>        clientRepo,
        IGenericRepository<Plan>          planRepo,
        IGenericRepository<UserSystem>    userRepo,
        IGenericRepository<SupportTicket> ticketRepo,
        AuditService                      audit)
    {
        _instRepo   = instRepo;
        _clientRepo = clientRepo;
        _planRepo   = planRepo;
        _userRepo   = userRepo;
        _ticketRepo = ticketRepo;
        _audit      = audit;
    }

    // ── Helper: contar técnicos activos reales ───────────────────────────────
    // BUG FIX: reemplaza la constante TecnicosDisponibles = 3 hardcodeada.
    private async Task<int> GetTecnicosDisponiblesAsync()
    {
        var count = await _userRepo.GetAll()
            .CountAsync(u => u.Role == UserRole.Tecnico && u.Status == UserStatus.Activo);
        // Fallback mínimo de 1 para evitar división por cero si no hay técnicos registrados
        return Math.Max(count, 1);
    }

    // ── Slots disponibles (GET /api/instalaciones/slots-disponibles) ──────────

    /// <summary>
    /// Devuelve slots de horario disponibles para los próximos N días.
    /// Un slot tiene disponibilidad si hay al menos un técnico libre.
    ///
    /// Horarios ofrecidos: 08:00, 10:00, 14:00, 16:00 (cada 2h)
    /// Disponibilidad = TecnicosDisponibles - instalaciones en ese slot
    /// </summary>
    public async Task<List<SlotDisponibleDto>> GetSlotsDisponiblesAsync(int diasAdelante = 7)
    {
        var hoy   = DateTime.UtcNow.Date;
        var hasta = hoy.AddDays(diasAdelante);

        // Traer instalaciones activas en el rango
        var instActivas = await _instRepo.GetAll()
            .Where(i =>
                i.Fecha >= hoy && i.Fecha <= hasta &&
                (i.Status == InstallationStatus.Pendiente ||
                 i.Status == InstallationStatus.EnProceso))
            .ToListAsync();

        var slots = new List<SlotDisponibleDto>();

        // Horarios ofrecidos (hora Bolivia = UTC-4; se guarda en UTC pero se muestra en local)
        var horariosOfrecidos = new[]
        {
            new TimeOnly(8,  0),
            new TimeOnly(10, 0),
            new TimeOnly(14, 0),
            new TimeOnly(16, 0),
        };

        // BUG FIX: obtener número real de técnicos activos (reemplaza constante hardcodeada)
        var tecnicosDisponibles = await GetTecnicosDisponiblesAsync();

        for (var dia = hoy; dia <= hasta; dia = dia.AddDays(1))
        {
            // No ofrecer el día actual si ya pasó el mediodía (Bolivia UTC-4 = 16:00 UTC)
            if (dia == hoy && DateTime.UtcNow.Hour >= 16) continue;

            // No ofrecer domingos
            if (dia.DayOfWeek == DayOfWeek.Sunday) continue;

            foreach (var hora in horariosOfrecidos)
            {
                var ocupados = instActivas.Count(i =>
                    i.Fecha.Date == dia &&
                    i.HoraInicio == hora);

                var disponibles = tecnicosDisponibles - ocupados;

                slots.Add(new SlotDisponibleDto(
                    Fecha:       dia.ToString("yyyy-MM-dd"),
                    HoraInicio:  hora.ToString("HH:mm"),
                    HoraFin:     hora.AddMinutes(DuracionEstandarMin).ToString("HH:mm"),
                    Disponibles: Math.Max(0, disponibles),
                    Disponible:  disponibles > 0
                ));
            }
        }

        return slots;
    }

    // ── Crear instalación (POST /api/instalaciones) ───────────────────────────

    /// <summary>
    /// Agenda una instalación y crea el ticket asociado automáticamente.
    /// Llamado tanto por el bot como por el panel admin.
    /// </summary>
    public async Task<Result<InstalacionCreadaDto>> CrearAsync(
        CrearInstalacionDto dto,
        Guid actorId, string actorName, string ip)
    {
        // Validar cliente
        Client? client = null;
        if (dto.ClienteId.HasValue)
        {
            client = await _clientRepo.GetAll()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == dto.ClienteId.Value);

            if (client is null)
                return Result<InstalacionCreadaDto>.Failure("El cliente indicado no existe.");

            // No permitir segunda instalación pendiente para el mismo cliente
            var tienePendiente = await _instRepo.AnyAsync(i =>
                i.ClientId == client.Id &&
                (i.Status == InstallationStatus.Pendiente ||
                 i.Status == InstallationStatus.EnProceso));

            if (tienePendiente)
                return Result<InstalacionCreadaDto>.Failure(
                    "El cliente ya tiene una instalación pendiente o en proceso.");
        }

        // Validar plan
        var plan = await _planRepo.GetByIdAsync(dto.PlanId);
        if (plan is null || !plan.IsActive)
            return Result<InstalacionCreadaDto>.Failure("El plan seleccionado no existe o está inactivo.");

        // Parsear fecha y hora
        if (!DateTime.TryParse(dto.Fecha, out var fecha))
            return Result<InstalacionCreadaDto>.Failure("Formato de fecha inválido. Use YYYY-MM-DD.");

        if (!TimeOnly.TryParse(dto.HoraInicio, out var horaInicio))
            return Result<InstalacionCreadaDto>.Failure("Formato de hora inválido. Use HH:mm.");

        // Verificar disponibilidad del slot
        var ocupados = await _instRepo.GetAll()
            .CountAsync(i =>
                i.Fecha.Date == fecha.Date &&
                i.HoraInicio == horaInicio &&
                (i.Status == InstallationStatus.Pendiente ||
                 i.Status == InstallationStatus.EnProceso));

        if (ocupados >= await GetTecnicosDisponiblesAsync())
            return Result<InstalacionCreadaDto>.Failure(
                "El horario seleccionado ya no tiene disponibilidad. Por favor elige otro.");

        // Crear instalación
        var instalacion = new Installation
        {
            ClientId    = dto.ClienteId ?? Guid.Empty,
            PlanId      = dto.PlanId,
            Fecha       = DateTime.SpecifyKind(fecha.Date, DateTimeKind.Utc),
            HoraInicio  = horaInicio,
            DuracionMin = DuracionEstandarMin,
            Direccion   = dto.Direccion.Trim(),
            Notas       = dto.Notas?.Trim(),
            Status      = InstallationStatus.Pendiente,
            CreadoPorId = actorId,
            CreadoAt    = DateTime.UtcNow,
        };

        await _instRepo.AddAsync(instalacion);

        // Crear ticket automáticamente
        var nombreCliente = client?.FullName ?? "Cliente nuevo";
        var tbnCode       = client?.TbnCode ?? "NUEVO";

        var ticket = new SupportTicket
        {
            ClientId        = dto.ClienteId ?? Guid.Empty,
            Subject         = $"[Instalación Nueva] {tbnCode} – {nombreCliente} – {dto.Fecha} {dto.HoraInicio}",
            Type            = TicketType.InstalacionNueva,
            Priority        = TicketPriority.Media,
            Status          = TicketStatus.Abierto,
            Origin          = TicketOrigin.Bot,
            Description     = $"Instalación agendada para el {dto.Fecha} a las {dto.HoraInicio}.\n" +
                              $"Plan: {plan.Name}\n" +
                              $"Dirección: {dto.Direccion}\n" +
                              (dto.Notas is not null ? $"Notas: {dto.Notas}" : ""),
            CreatedByUserId = actorId,
            CreatedAt       = DateTime.UtcNow,
            DueDate         = fecha.Date.AddHours(horaInicio.Hour + DuracionEstandarMin / 60.0),
        };

        if (dto.ClienteId.HasValue)
        {
            await _ticketRepo.AddAsync(ticket);
            // Vincular ticket a instalación
            instalacion.TicketId     = ticket.Id;
            instalacion.ActualizadoAt = DateTime.UtcNow;
            await _instRepo.UpdateAsync(instalacion);
        }

        await _audit.LogAsync("Instalaciones", "INSTALACION_CREADA",
            $"Instalación agendada: {tbnCode} – {dto.Fecha} {dto.HoraInicio} – {plan.Name}",
            actorId, actorName, ip);

        return Result<InstalacionCreadaDto>.Success(new InstalacionCreadaDto(
            InstalacionId: instalacion.Id.ToString(),
            TicketId:      ticket.Id.ToString(),
            Fecha:         dto.Fecha,
            HoraInicio:    dto.HoraInicio,
            Status:        instalacion.Status.ToString()
        ));
    }

    // ── Crear desde panel admin (más campos) ──────────────────────────────────

    public async Task<Result<InstalacionDetalleDto>> CrearAdminAsync(
        CrearInstalacionAdminDto dto,
        Guid actorId, string actorName, string ip)
    {
        // Adaptar al DTO base
        var dtoBase = new CrearInstalacionDto
        {
            ClienteId  = dto.ClienteId,
            PlanId     = dto.PlanId,
            Fecha      = dto.Fecha,
            HoraInicio = dto.HoraInicio,
            Direccion  = dto.Direccion,
            Notas      = dto.Notas,
        };

        var result = await CrearAsync(dtoBase, actorId, actorName, ip);
        if (!result.IsSuccess)
            return Result<InstalacionDetalleDto>.Failure(result.ErrorMessage!);

        // Asignar técnico si se indicó
        if (dto.TecnicoId.HasValue && Guid.TryParse(result.Value!.InstalacionId, out var instId))
        {
            var asignResult = await AsignarTecnicoAsync(
                instId, new AsignarTecnicoDto { TecnicoId = dto.TecnicoId.Value },
                actorId, actorName, ip);
        }

        var instId2 = Guid.Parse(result.Value!.InstalacionId);
        return await GetDetalleAsync(instId2)
            is InstalacionDetalleDto detalle
                ? Result<InstalacionDetalleDto>.Success(detalle)
                : Result<InstalacionDetalleDto>.Failure("Error al obtener detalle.");
    }

    // ── Cancelar instalación (PATCH /api/instalaciones/{id}/cancelar) ─────────

    public async Task<Result<bool>> CancelarAsync(
        Guid id, CancelarInstalacionDto dto,
        Guid actorId, string actorName, string ip)
    {
        var inst = await _instRepo.GetAll()
            .Include(i => i.Client)
            .Include(i => i.Ticket)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inst is null)
            return Result<bool>.Failure("Instalación no encontrada.");

        if (inst.Status is InstallationStatus.Completada or InstallationStatus.Cancelada)
            return Result<bool>.Failure($"La instalación ya está {inst.Status}. No se puede cancelar.");

        inst.Status            = InstallationStatus.Cancelada;
        inst.MotivoCancelacion = dto.MotivoCancelacion.Trim();
        inst.CanceladoPor      = dto.CanceladoPor;
        inst.ActualizadoAt     = DateTime.UtcNow;
        await _instRepo.UpdateAsync(inst);

        // Cancelar el ticket asociado si está abierto
        if (inst.Ticket is not null &&
            inst.Ticket.Status is TicketStatus.Abierto or TicketStatus.EnProceso)
        {
            inst.Ticket.Status            = TicketStatus.Cerrado;
            inst.Ticket.ResolutionMessage = $"Instalación cancelada. Motivo: {dto.MotivoCancelacion}";
            inst.Ticket.ClosedAt          = DateTime.UtcNow;
            await _ticketRepo.UpdateAsync(inst.Ticket);
        }

        await _audit.LogAsync("Instalaciones", "INSTALACION_CANCELADA",
            $"Instalación cancelada: {id} — {dto.MotivoCancelacion} — por {dto.CanceladoPor}",
            actorId, actorName, ip);

        return Result<bool>.Success(true);
    }

    // ── Reprogramar (PATCH /api/instalaciones/{id}/reprogramar) ──────────────

    public async Task<Result<bool>> ReprogramarAsync(
        Guid id, ReprogramarInstalacionDto dto,
        Guid actorId, string actorName, string ip)
    {
        var inst = await _instRepo.GetByIdAsync(id);
        if (inst is null) return Result<bool>.Failure("Instalación no encontrada.");

        if (inst.Status is InstallationStatus.Completada or InstallationStatus.Cancelada)
            return Result<bool>.Failure("No se puede reprogramar una instalación completada o cancelada.");

        if (!DateTime.TryParse(dto.Fecha, out var nuevaFecha))
            return Result<bool>.Failure("Formato de fecha inválido.");

        if (!TimeOnly.TryParse(dto.HoraInicio, out var nuevaHora))
            return Result<bool>.Failure("Formato de hora inválido.");

        // Verificar disponibilidad del nuevo slot
        var ocupados = await _instRepo.GetAll()
            .CountAsync(i =>
                i.Id != id &&
                i.Fecha.Date == nuevaFecha.Date &&
                i.HoraInicio == nuevaHora &&
                (i.Status == InstallationStatus.Pendiente ||
                 i.Status == InstallationStatus.EnProceso));

        if (ocupados >= await GetTecnicosDisponiblesAsync())
            return Result<bool>.Failure("El nuevo horario no tiene disponibilidad.");

        inst.Fecha         = DateTime.SpecifyKind(nuevaFecha.Date, DateTimeKind.Utc);
        inst.HoraInicio    = nuevaHora;
        inst.Status        = InstallationStatus.Reprogramada;
        inst.ActualizadoAt = DateTime.UtcNow;
        await _instRepo.UpdateAsync(inst);

        await _audit.LogAsync("Instalaciones", "INSTALACION_REPROGRAMADA",
            $"Instalación {id} reprogramada a {dto.Fecha} {dto.HoraInicio}",
            actorId, actorName, ip);

        return Result<bool>.Success(true);
    }

    // ── Completar (PATCH /api/instalaciones/{id}/completar) ───────────────────

    public async Task<Result<bool>> CompletarAsync(
        Guid id, CompletarInstalacionDto dto,
        Guid actorId, string actorName, string ip)
    {
        var inst = await _instRepo.GetAll()
            .Include(i => i.Ticket)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inst is null) return Result<bool>.Failure("Instalación no encontrada.");
        if (inst.Status == InstallationStatus.Completada)
            return Result<bool>.Failure("La instalación ya está completada.");
        if (inst.Status == InstallationStatus.Cancelada)
            return Result<bool>.Failure("No se puede completar una instalación cancelada.");

        inst.Status        = InstallationStatus.Completada;
        inst.ActualizadoAt = DateTime.UtcNow;
        await _instRepo.UpdateAsync(inst);

        // Resolver el ticket automáticamente
        if (inst.Ticket is not null &&
            inst.Ticket.Status is TicketStatus.Abierto or TicketStatus.EnProceso)
        {
            inst.Ticket.Status            = TicketStatus.Resuelto;
            inst.Ticket.ResolutionMessage = dto.NotasTecnico ?? "Instalación completada exitosamente.";
            inst.Ticket.ResolvedAt        = DateTime.UtcNow;
            inst.Ticket.SlaCompliant      = inst.Ticket.DueDate.HasValue &&
                                            DateTime.UtcNow <= inst.Ticket.DueDate.Value;
            await _ticketRepo.UpdateAsync(inst.Ticket);
        }

        await _audit.LogAsync("Instalaciones", "INSTALACION_COMPLETADA",
            $"Instalación {id} completada.",
            actorId, actorName, ip);

        return Result<bool>.Success(true);
    }

    // ── Asignar técnico (PATCH /api/instalaciones/{id}/tecnico) ──────────────

    public async Task<Result<bool>> AsignarTecnicoAsync(
        Guid id, AsignarTecnicoDto dto,
        Guid actorId, string actorName, string ip)
    {
        var inst = await _instRepo.GetByIdAsync(id);
        if (inst is null) return Result<bool>.Failure("Instalación no encontrada.");

        var tecnico = await _userRepo.GetByIdAsync(dto.TecnicoId);
        if (tecnico is null) return Result<bool>.Failure("El técnico indicado no existe.");
        if (tecnico.Role != UserRole.Tecnico && tecnico.Role != UserRole.Admin)
            return Result<bool>.Failure("Solo se puede asignar a un usuario con rol Técnico o Admin.");

        inst.TecnicoId     = dto.TecnicoId;
        inst.Status        = InstallationStatus.EnProceso;
        inst.ActualizadoAt = DateTime.UtcNow;
        await _instRepo.UpdateAsync(inst);

        await _audit.LogAsync("Instalaciones", "INSTALACION_TECNICO_ASIGNADO",
            $"Técnico {tecnico.FullName} asignado a instalación {id}",
            actorId, actorName, ip);

        return Result<bool>.Success(true);
    }

    // ── Detalle (GET /api/instalaciones/{id}) ─────────────────────────────────

    public async Task<InstalacionDetalleDto?> GetDetalleAsync(Guid id)
    {
        var inst = await _instRepo.GetAll()
            .Include(i => i.Client)
            .Include(i => i.Plan)
            .Include(i => i.Tecnico)
            .FirstOrDefaultAsync(i => i.Id == id);

        return inst is null ? null : MapDetalle(inst);
    }

    // ── Listado (GET /api/instalaciones) ──────────────────────────────────────

    public async Task<PagedResult<InstalacionListItemDto>> GetAllAsync(
        InstalacionFilterDto filter)
    {
        var query = _instRepo.GetAll()
            .Include(i => i.Client)
            .Include(i => i.Plan)
            .Include(i => i.Tecnico)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Status) &&
            Enum.TryParse<InstallationStatus>(filter.Status, out var st))
            query = query.Where(i => i.Status == st);

        if (filter.TecnicoId.HasValue)
            query = query.Where(i => i.TecnicoId == filter.TecnicoId);

        if (filter.ClienteId.HasValue)
            query = query.Where(i => i.ClientId == filter.ClienteId);

        if (!string.IsNullOrWhiteSpace(filter.Fecha) &&
            DateTime.TryParse(filter.Fecha, out var fd))
            query = query.Where(i => i.Fecha.Date == fd.Date);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(i => i.Fecha).ThenBy(i => i.HoraInicio)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PagedResult<InstalacionListItemDto>(
            items.Select(MapListItem),
            total, filter.Page, filter.PageSize);
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static InstalacionDetalleDto MapDetalle(Installation i) => new(
        Id:               i.Id,
        ClienteTbn:       i.Client?.TbnCode  ?? "—",
        ClienteNombre:    i.Client?.FullName ?? "—",
        ClientePhone:     i.Client?.PhoneMain ?? "—",
        PlanNombre:       i.Plan?.Name ?? "—",
        Fecha:            i.Fecha.ToString("yyyy-MM-dd"),
        HoraInicio:       i.HoraInicio.ToString("HH:mm"),
        HoraFin:          i.HoraInicio.AddMinutes(i.DuracionMin).ToString("HH:mm"),
        DuracionMin:      i.DuracionMin,
        Direccion:        i.Direccion,
        Notas:            i.Notas,
        Status:           i.Status.ToString(),
        TecnicoNombre:    i.Tecnico?.FullName,
        TecnicoId:        i.TecnicoId,
        TicketId:         i.TicketId,
        MotivoCancelacion: i.MotivoCancelacion,
        CanceladoPor:     i.CanceladoPor,
        CreadoAt:         i.CreadoAt
    );

    private static InstalacionListItemDto MapListItem(Installation i) => new(
        Id:            i.Id,
        ClienteTbn:    i.Client?.TbnCode  ?? "—",
        ClienteNombre: i.Client?.FullName ?? "—",
        PlanNombre:    i.Plan?.Name ?? "—",
        Fecha:         i.Fecha.ToString("yyyy-MM-dd"),
        HoraInicio:    i.HoraInicio.ToString("HH:mm"),
        Status:        i.Status.ToString(),
        TecnicoNombre: i.Tecnico?.FullName,
        TicketId:      i.TicketId,
        CreadoAt:      i.CreadoAt
    );
}
