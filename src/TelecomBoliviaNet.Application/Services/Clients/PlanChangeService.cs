using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Clients;

/// <summary>
/// Gestiona solicitudes de cambio de plan.
///
/// Reglas:
///  - Día 1–24: cambio queda Pendiente, efectivo el 1ro del mes siguiente.
///  - Día 25+:  igual, efectivo el 1ro del mes siguiente.
///  - Admin puede aprobar a mitad de mes (MidMonthChange=true):
///      → Se anula factura mensual pendiente del mes actual.
///      → Se genera factura proporcional plan anterior (días 1..hoy-1).
///      → Se genera factura proporcional plan nuevo (hoy..fin de mes).
///      → El plan del cliente cambia inmediatamente.
///
/// FIX-A: Todas las operaciones dentro de transacción usan MarkModifiedAsync
/// (sin SaveChanges intermedio). Un único SaveChangesAsync se ejecuta antes
/// del CommitAsync, garantizando atomicidad real y evitando estados parciales.
///
/// FIX-D: Notificación WhatsApp al cliente al aprobar o rechazar su solicitud.
/// </summary>
public class PlanChangeService : IPlanChangeService
{
    private readonly IGenericRepository<PlanChangeRequest> _changeRepo;
    private readonly IGenericRepository<Client>             _clientRepo;
    private readonly IGenericRepository<Plan>               _planRepo;
    private readonly IGenericRepository<Invoice>            _invoiceRepo;
    private readonly IGenericRepository<SupportTicket>      _ticketRepo;
    private readonly AuditService                           _audit;
    private readonly IUnitOfWork                            _uow;
    private readonly INotifPublisher                        _notif;
    private readonly IInternalAlertService                  _alertSvc;

    public PlanChangeService(
        IGenericRepository<PlanChangeRequest> changeRepo,
        IGenericRepository<Client>            clientRepo,
        IGenericRepository<Plan>              planRepo,
        IGenericRepository<Invoice>           invoiceRepo,
        IGenericRepository<SupportTicket>     ticketRepo,
        AuditService                          audit,
        IUnitOfWork                           uow,
        INotifPublisher                       notif,
        IInternalAlertService                 alertSvc)
    {
        _changeRepo  = changeRepo;
        _clientRepo  = clientRepo;
        _planRepo    = planRepo;
        _invoiceRepo = invoiceRepo;
        _ticketRepo  = ticketRepo;
        _audit       = audit;
        _uow         = uow;
        _notif       = notif;
        _alertSvc    = alertSvc;
    }

    // ── Solicitar cambio de plan ──────────────────────────────────────────────
    public async Task<Result<Guid>> SolicitarCambioAsync(
        Guid clientId, Guid newPlanId, string? notes,
        Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == clientId);

        if (client is null)
            return Result<Guid>.Failure("Cliente no encontrado.");
        if (client.PlanId == newPlanId)
            return Result<Guid>.Failure("El cliente ya tiene ese plan.");

        var newPlan = await _planRepo.GetByIdAsync(newPlanId);
        if (newPlan is null || !newPlan.IsActive)
            return Result<Guid>.Failure("El plan seleccionado no existe o está inactivo.");

        var tienePendiente = await _changeRepo.AnyAsync(r =>
            r.ClientId == clientId && r.Status == PlanChangeStatus.Pendiente);
        if (tienePendiente)
            return Result<Guid>.Failure("El cliente ya tiene una solicitud de cambio de plan pendiente.");

        var hoy           = DateTime.UtcNow;
        var effectiveDate = new DateTime(hoy.Year, hoy.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                                .AddMonths(1);

        var ticket = new SupportTicket
        {
            ClientId        = clientId,
            Subject         = $"[Cambio de Plan] {client.TbnCode} – {client.FullName} → {newPlan.Name}",
            Type            = TicketType.CambioPlan,
            Priority        = TicketPriority.Baja,
            Status          = TicketStatus.Abierto,
            Origin          = TicketOrigin.Manual,
            Description     = $"Solicitud de cambio de plan:\n" +
                              $"Plan actual: {client.Plan?.Name ?? "—"}\n" +
                              $"Plan nuevo:  {newPlan.Name}\n" +
                              $"Fecha efectiva: {effectiveDate:dd/MM/yyyy}\n" +
                              (notes is not null ? $"Notas: {notes}" : ""),
            CreatedByUserId = actorId,
            CreatedAt       = DateTime.UtcNow,
        };

        var cambio = new PlanChangeRequest
        {
            ClientId       = clientId,
            OldPlanId      = client.PlanId,
            NewPlanId      = newPlanId,
            TicketId       = ticket.Id,
            Status         = PlanChangeStatus.Pendiente,
            EffectiveDate  = effectiveDate,
            MidMonthChange = false,
            Notes          = notes,
            RequestedAt    = DateTime.UtcNow,
            RequestedById  = actorId,
        };

        // FIX-A: AddAsync acumula en tracker. SaveChangesAsync persiste ambas entidades
        // dentro de la transacción antes del CommitAsync. Sin esto, CommitAsync
        // confirmaría una transacción vacía y la persistencia dependería del SaveChanges
        // accidental del AuditService (frágil e incorrecto).
        await _uow.BeginTransactionAsync();
        try
        {
            await _ticketRepo.AddAsync(ticket);
            await _changeRepo.AddAsync(cambio);
            await _changeRepo.SaveChangesAsync();
            await _uow.CommitAsync();
        }
        catch (Exception)
        {
            await _uow.RollbackAsync();
            throw;
        }

        await _audit.LogAsync("Clientes", "PLAN_CHANGE_REQUESTED",
            $"Cambio de plan solicitado: {client.TbnCode} — {client.Plan?.Name} → {newPlan.Name}",
            actorId, actorName, ip);

        await _alertSvc.CreateForRoleAsync(
            role:    "Admin",
            type:    "PLAN_CHANGE_PENDIENTE",
            title:   $"Solicitud de cambio de plan — {client.TbnCode}",
            message: $"{client.Plan?.Name} → {newPlan.Name}",
            link:    $"/clients/{clientId}?tab=planchange");

        return Result<Guid>.Success(cambio.Id);
    }

    // ── Aprobar cambio de plan ────────────────────────────────────────────────
    // FIX-A: Todas las modificaciones usan MarkModifiedAsync (sin SaveChanges intermedio).
    // Un único SaveChangesAsync antes del CommitAsync garantiza que:
    //   - No se guardan estados parciales de cambio (ej: MidMonthChange=true con Status=Pendiente).
    //   - El rollback del UoW deshace TODOS los cambios si cualquier paso falla.
    //   - EF Core envía todos los INSERT/UPDATE en un solo batch (más eficiente).
    // FIX-B: Usa client.ChangePlan() en lugar de asignación directa de PlanId.
    public async Task<Result<bool>> AprobarCambioAsync(
        Guid cambioId, bool midMonth,
        Guid actorId, string actorName, string ip)
    {
        var cambio = await _changeRepo.GetAll()
            .Include(r => r.Client).ThenInclude(c => c!.Plan)
            .Include(r => r.NewPlan)
            .FirstOrDefaultAsync(r => r.Id == cambioId);

        if (cambio is null)
            return Result<bool>.Failure("Solicitud de cambio no encontrada.");
        if (cambio.Status != PlanChangeStatus.Pendiente)
            return Result<bool>.Failure("Esta solicitud ya fue procesada.");

        var client  = cambio.Client!;
        var newPlan = cambio.NewPlan!;
        var hoy     = DateTime.UtcNow;

        // IgnoreQueryFilters garantiza cargar el ticket aunque el filtro global de
        // SupportTicket lo excluya por algún edge-case con la navegación al cliente.
        var ticket = cambio.TicketId.HasValue
            ? await _ticketRepo.GetAll()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == cambio.TicketId.Value)
            : null;

        await _uow.BeginTransactionAsync();
        try
        {
            if (midMonth)
            {
                cambio.MidMonthChange = true;
                cambio.EffectiveDate  = hoy;
                await ApplyMidMonthProrationAsync(client, cambio.NewPlan!, hoy);
            }

            // FIX-B: método de dominio que valida + asigna + actualiza UpdatedAt
            client.ChangePlan(newPlan.Id);
            await _clientRepo.MarkModifiedAsync(client);

            cambio.Status        = PlanChangeStatus.Aprobado;
            cambio.ProcessedAt   = hoy;
            cambio.ProcessedById = actorId;
            await _changeRepo.MarkModifiedAsync(cambio);

            if (ticket is not null)
            {
                ticket.Status            = TicketStatus.Resuelto;
                ticket.ResolutionMessage = midMonth
                    ? "Cambio de plan aprobado y ejecutado inmediatamente. Facturas proporcionales generadas."
                    : $"Cambio de plan aprobado. Efectivo el {cambio.EffectiveDate:dd/MM/yyyy}.";
                ticket.ResolvedAt = hoy;
                await _ticketRepo.MarkModifiedAsync(ticket);
            }

            // FIX-A: único SaveChangesAsync — persiste todos los cambios acumulados
            // (facturaActual, invoice1, invoice2, client, cambio, ticket) en un solo batch.
            await _changeRepo.SaveChangesAsync();
            await _uow.CommitAsync();
        }
        catch (Exception)
        {
            await _uow.RollbackAsync();
            throw;
        }

        // FIX-D: notificación al cliente después de confirmar la transacción
        await _notif.PublishAsync(
            NotifType.CAMBIO_PLAN,
            client.Id,
            client.PhoneMain,
            new Dictionary<string, string>
            {
                ["nombre"]         = client.FullName,
                ["plan_nuevo"]     = newPlan.Name,
                ["precio_nuevo"]   = newPlan.MonthlyPrice.ToString("N0"),
                ["fecha_efectiva"] = midMonth ? "inmediatamente" : cambio.EffectiveDate.ToString("dd/MM/yyyy"),
            });

        await _audit.LogAsync("Clientes", "PLAN_CHANGE_APPROVED",
            $"Cambio de plan aprobado: {client.TbnCode} → {newPlan.Name}" +
            (midMonth ? " (mitad de mes)" : " (fin de mes)"),
            actorId, actorName, ip);

        return Result<bool>.Success(true);
    }

    // Anula factura mensual actual y crea dos facturas proporcionales.
    // FIX-A: usa MarkModifiedAsync en lugar de UpdateAsync para no generar
    // SaveChanges intermedios que dejarían cambio.Status=Pendiente guardado a medias.
    private async Task ApplyMidMonthProrationAsync(Client client, Plan newPlan, DateTime hoy)
    {
        int daysInMonth = DateTime.DaysInMonth(hoy.Year, hoy.Month);
        int daysPlanOld = hoy.Day - 1;
        int daysPlanNew = daysInMonth - hoy.Day + 1;

        var facturaActual = await _invoiceRepo.GetAll()
            .FirstOrDefaultAsync(i =>
                i.ClientId == client.Id &&
                i.Year     == hoy.Year  &&
                i.Month    == hoy.Month &&
                i.Type     == InvoiceType.Mensualidad &&
                i.Status   == InvoiceStatus.Pendiente);

        if (facturaActual is not null)
        {
            facturaActual.Status    = InvoiceStatus.Anulada;
            facturaActual.Notes     = $"Anulada por cambio de plan a mitad de mes ({hoy:dd/MM/yyyy})";
            facturaActual.UpdatedAt = hoy;
            await _invoiceRepo.MarkModifiedAsync(facturaActual);
        }

        var dueDate = new DateTime(hoy.Year, hoy.Month, 5, 0, 0, 0, DateTimeKind.Utc);
        if (dueDate < hoy) dueDate = hoy.AddDays(5);

        // ProrrataAnterior y ProrrataNueva son tipos distintos de Mensualidad para evitar
        // violar el índice único (ClientId, Year, Month, Type) en la tabla Invoices.
        // La factura mensual original queda como Anulada (auditoría), estas dos son
        // los cargos proporcionales por cada tramo del mes.
        if (daysPlanOld > 0 && client.Plan is not null)
        {
            await _invoiceRepo.AddAsync(new Invoice
            {
                ClientId  = client.Id,
                Type      = InvoiceType.ProrrataAnterior,
                Status    = InvoiceStatus.Pendiente,
                Year      = hoy.Year,
                Month     = hoy.Month,
                Amount    = Math.Round(client.Plan.MonthlyPrice * daysPlanOld / daysInMonth, 2),
                IssuedAt  = hoy,
                DueDate   = dueDate,
                Notes     = $"Proporcional {client.Plan.Name}: {daysPlanOld} de {daysInMonth} días",
                PlanName  = client.Plan.Name,
                PlanPrice = client.Plan.MonthlyPrice,
            });
        }

        if (daysPlanNew > 0)
        {
            await _invoiceRepo.AddAsync(new Invoice
            {
                ClientId  = client.Id,
                Type      = InvoiceType.ProrrataNueva,
                Status    = InvoiceStatus.Pendiente,
                Year      = hoy.Year,
                Month     = hoy.Month,
                Amount    = Math.Round(newPlan.MonthlyPrice * daysPlanNew / daysInMonth, 2),
                IssuedAt  = hoy,
                DueDate   = dueDate,
                Notes     = $"Proporcional {newPlan.Name}: {daysPlanNew} de {daysInMonth} días",
                PlanName  = newPlan.Name,
                PlanPrice = newPlan.MonthlyPrice,
            });
        }
    }

    // ── Rechazar cambio de plan ───────────────────────────────────────────────
    // FIX-A: MarkModifiedAsync + único SaveChangesAsync antes del CommitAsync.
    // FIX-D: Notificación WhatsApp al cliente informando el rechazo.
    public async Task<Result<bool>> RechazarCambioAsync(
        Guid cambioId, string motivo,
        Guid actorId, string actorName, string ip)
    {
        // FIX-D: incluir Client para tener el teléfono al notificar
        var cambio = await _changeRepo.GetAll()
            .Include(r => r.Client)
            .FirstOrDefaultAsync(r => r.Id == cambioId);

        if (cambio is null)
            return Result<bool>.Failure("Solicitud no encontrada.");
        if (cambio.Status != PlanChangeStatus.Pendiente)
            return Result<bool>.Failure("Esta solicitud ya fue procesada.");

        // IgnoreQueryFilters garantiza cargar el ticket aunque el filtro global lo excluya.
        var ticket = cambio.TicketId.HasValue
            ? await _ticketRepo.GetAll()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == cambio.TicketId.Value)
            : null;

        var ahora = DateTime.UtcNow;
        await _uow.BeginTransactionAsync();
        try
        {
            cambio.Status          = PlanChangeStatus.Rechazado;
            cambio.RejectionReason = motivo;
            cambio.ProcessedAt     = ahora;
            cambio.ProcessedById   = actorId;
            await _changeRepo.MarkModifiedAsync(cambio);

            if (ticket is not null)
            {
                ticket.Status            = TicketStatus.Cerrado;
                ticket.ResolutionMessage = $"Cambio de plan rechazado. Motivo: {motivo}";
                ticket.ClosedAt          = ahora;
                await _ticketRepo.MarkModifiedAsync(ticket);
            }

            await _changeRepo.SaveChangesAsync();
            await _uow.CommitAsync();
        }
        catch (Exception)
        {
            await _uow.RollbackAsync();
            throw;
        }

        // FIX-D: notificar al cliente que su solicitud fue rechazada
        if (cambio.Client is not null)
        {
            await _notif.PublishAsync(
                NotifType.CAMBIO_PLAN,
                cambio.Client.Id,
                cambio.Client.PhoneMain,
                new Dictionary<string, string>
                {
                    ["nombre"]  = cambio.Client.FullName,
                    ["motivo"]  = motivo,
                    ["estado"]  = "rechazado",
                });
        }

        await _audit.LogAsync("Clientes", "PLAN_CHANGE_REJECTED",
            $"Cambio de plan rechazado: {cambio.ClientId} — {motivo}",
            actorId, actorName, ip);

        return Result<bool>.Success(true);
    }

    // ── Listar solicitudes pendientes ─────────────────────────────────────────
    public async Task<List<PlanChangeItemDto>> GetPendientesAsync(Guid? clientId = null)
    {
        var query = _changeRepo.GetAll()
            .Include(r => r.Client)
            .Include(r => r.OldPlan)
            .Include(r => r.NewPlan)
            .Where(r => r.Status == PlanChangeStatus.Pendiente);

        if (clientId.HasValue)
            query = query.Where(r => r.ClientId == clientId.Value);

        var items = await query.OrderBy(r => r.RequestedAt).ToListAsync();

        return items.Select(r => new PlanChangeItemDto(
            r.Id,
            ClienteTbn:    r.Client?.TbnCode  ?? "—",
            ClienteNombre: r.Client?.FullName  ?? "—",
            PlanAnterior:  r.OldPlan?.Name     ?? "—",
            PlanNuevo:     r.NewPlan?.Name      ?? "—",
            FechaEfectiva: r.EffectiveDate.ToString("yyyy-MM-dd"),
            Notes:         r.Notes,
            SolicitadoAt:  r.RequestedAt.ToString("O")
        )).ToList();
    }

    // ── Historial completo de cambios por cliente (FIX-F) ────────────────────
    public async Task<List<PlanChangeHistorialDto>> GetHistorialAsync(Guid clientId)
    {
        var items = await _changeRepo.GetAll()
            .Include(r => r.OldPlan)
            .Include(r => r.NewPlan)
            .Where(r => r.ClientId == clientId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync();

        return items.Select(r => new PlanChangeHistorialDto(
            r.Id,
            PlanAnterior:   r.OldPlan?.Name ?? "—",
            PlanNuevo:      r.NewPlan?.Name  ?? "—",
            Status:         r.Status.ToString(),
            FechaEfectiva:  r.EffectiveDate.ToString("yyyy-MM-dd"),
            MidMonthChange: r.MidMonthChange,
            SolicitadoAt:   r.RequestedAt.ToString("O"),
            ProcesadoAt:    r.ProcessedAt?.ToString("O"),
            MotivoRechazo:  r.RejectionReason,
            Notes:          r.Notes
        )).ToList();
    }
}
