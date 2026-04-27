using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
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
///      → Se genera factura proporcional plan anterior (días 1..hoy)
///      → Se genera factura proporcional plan nuevo (hoy+1..fin de mes)
///      → El plan del cliente cambia inmediatamente.
///
/// CORRECCIÓN (Fix #11): AprobarCambioAsync envuelve todas las operaciones en
/// IUnitOfWork (transacción de BD) para garantizar atomicidad. Si cualquier paso
/// falla, se hace rollback completo y la BD queda en estado consistente.
/// </summary>
public class PlanChangeService
{
    private readonly IGenericRepository<PlanChangeRequest> _changeRepo;
    private readonly IGenericRepository<Client>             _clientRepo;
    private readonly IGenericRepository<Plan>               _planRepo;
    private readonly IGenericRepository<Invoice>            _invoiceRepo;
    private readonly IGenericRepository<SupportTicket>      _ticketRepo;
    private readonly AuditService                           _audit;
    private readonly IUnitOfWork                            _uow;

    public PlanChangeService(
        IGenericRepository<PlanChangeRequest> changeRepo,
        IGenericRepository<Client>            clientRepo,
        IGenericRepository<Plan>              planRepo,
        IGenericRepository<Invoice>           invoiceRepo,
        IGenericRepository<SupportTicket>     ticketRepo,
        AuditService                          audit,
        IUnitOfWork                           uow)
    {
        _changeRepo  = changeRepo;
        _clientRepo  = clientRepo;
        _planRepo    = planRepo;
        _invoiceRepo = invoiceRepo;
        _ticketRepo  = ticketRepo;
        _audit       = audit;
        _uow         = uow;
    }

    // ── Solicitar cambio de plan ──────────────────────────────────────────────
    // BUG D FIX: `_ticketRepo.AddAsync(ticket)` y `_changeRepo.AddAsync(cambio)` se
    // ejecutaban sin transacción. Si el segundo falla, queda un ticket huérfano en BD
    // (sin PlanChangeRequest asociado). Ahora ambas operaciones son atómicas via IUnitOfWork.
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

        // Verificar que no haya otra solicitud pendiente
        var tienePendiente = await _changeRepo.AnyAsync(r =>
            r.ClientId == clientId && r.Status == PlanChangeStatus.Pendiente);
        if (tienePendiente)
            return Result<Guid>.Failure("El cliente ya tiene una solicitud de cambio de plan pendiente.");

        // Fecha efectiva: siempre el 1ro del mes siguiente
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
            Origin          = TicketOrigin.Bot,
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

        // Transacción atómica: si AddAsync(cambio) falla, el ticket también hace rollback.
        // Sin esto, un fallo en el segundo insert deja un ticket huérfano en BD.
        await _uow.BeginTransactionAsync();
        try
        {
            await _ticketRepo.AddAsync(ticket);
            await _changeRepo.AddAsync(cambio);
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

        return Result<Guid>.Success(cambio.Id);
    }

    // ── Aprobar cambio de plan ────────────────────────────────────────────────
    // CORRECCIÓN (Fix #11): Todas las operaciones ejecutan dentro de una transacción
    // atómica. Si cualquier paso falla, se hace rollback y la BD queda consistente.
    public async Task<Result<bool>> AprobarCambioAsync(
        Guid cambioId, bool midMonth,
        Guid actorId, string actorName, string ip)
    {
        var cambio = await _changeRepo.GetAll()
            .Include(r => r.Client).ThenInclude(c => c!.Plan)
            .Include(r => r.NewPlan)
            .Include(r => r.Ticket)
            .FirstOrDefaultAsync(r => r.Id == cambioId);

        if (cambio is null)
            return Result<bool>.Failure("Solicitud de cambio no encontrada.");
        if (cambio.Status != PlanChangeStatus.Pendiente)
            return Result<bool>.Failure("Esta solicitud ya fue procesada.");

        var client  = cambio.Client!;
        var newPlan = cambio.NewPlan!;
        var hoy     = DateTime.UtcNow;

        await _uow.BeginTransactionAsync();
        try
        {
            if (midMonth)
            {
                // ── Cambio a mitad de mes ────────────────────────────────────────
                cambio.MidMonthChange = true;
                cambio.EffectiveDate  = hoy;

                // Anular factura del mes actual si está pendiente
                var facturaActual = await _invoiceRepo.GetAll()
                    .FirstOrDefaultAsync(i =>
                        i.ClientId == client.Id &&
                        i.Year     == hoy.Year  &&
                        i.Month    == hoy.Month &&
                        i.Type     == InvoiceType.Mensualidad &&
                        i.Status   == InvoiceStatus.Pendiente);

                int daysInMonth   = DateTime.DaysInMonth(hoy.Year, hoy.Month);
                int daysPlanOld   = hoy.Day - 1;           // días con plan anterior (1..ayer)
                int daysPlanNew   = daysInMonth - hoy.Day + 1; // días con plan nuevo (hoy..fin mes)

                if (facturaActual is not null)
                {
                    facturaActual.Status    = InvoiceStatus.Anulada;
                    facturaActual.Notes     = $"Anulada por cambio de plan a mitad de mes ({hoy:dd/MM/yyyy})";
                    facturaActual.UpdatedAt = hoy;
                    await _invoiceRepo.UpdateAsync(facturaActual);
                }

                var dueDate = new DateTime(hoy.Year, hoy.Month, 5, 0, 0, 0, DateTimeKind.Utc);
                if (dueDate < hoy) dueDate = hoy.AddDays(5);

                // Factura proporcional plan anterior (si hay días)
                if (daysPlanOld > 0 && client.Plan is not null)
                {
                    var montoPlanOld = Math.Round(
                        client.Plan.MonthlyPrice * daysPlanOld / daysInMonth, 2);
                    await _invoiceRepo.AddAsync(new Invoice
                    {
                        ClientId = client.Id,
                        Type     = InvoiceType.Mensualidad,
                        Status   = InvoiceStatus.Pendiente,
                        Year     = hoy.Year,
                        Month    = hoy.Month,
                        Amount   = montoPlanOld,
                        IssuedAt = hoy,
                        DueDate  = dueDate,
                        Notes    = $"Proporcional {client.Plan.Name}: {daysPlanOld} de {daysInMonth} días",
                    });
                }

                // Factura proporcional plan nuevo
                if (daysPlanNew > 0)
                {
                    var montoPlanNew = Math.Round(
                        newPlan.MonthlyPrice * daysPlanNew / daysInMonth, 2);
                    await _invoiceRepo.AddAsync(new Invoice
                    {
                        ClientId = client.Id,
                        Type     = InvoiceType.Mensualidad,
                        Status   = InvoiceStatus.Pendiente,
                        Year     = hoy.Year,
                        Month    = hoy.Month,
                        Amount   = montoPlanNew,
                        IssuedAt = hoy,
                        DueDate  = dueDate,
                        Notes    = $"Proporcional {newPlan.Name}: {daysPlanNew} de {daysInMonth} días",
                    });
                }

                // Cambiar plan del cliente inmediatamente
                client.PlanId     = newPlan.Id;
                client.UpdatedAt  = hoy;
                await _clientRepo.UpdateAsync(client);
            }
            // else: cambio a fin de mes — el BillingJob generará la factura correcta el día 1

            // Actualizar solicitud
            cambio.Status        = PlanChangeStatus.Aprobado;
            cambio.ProcessedAt   = hoy;
            cambio.ProcessedById = actorId;
            await _changeRepo.UpdateAsync(cambio);

            // Cerrar ticket
            if (cambio.Ticket is not null)
            {
                cambio.Ticket.Status            = TicketStatus.Resuelto;
                cambio.Ticket.ResolutionMessage = midMonth
                    ? "Cambio de plan aprobado y ejecutado inmediatamente. Facturas proporcionales generadas."
                    : $"Cambio de plan aprobado. Efectivo el {cambio.EffectiveDate:dd/MM/yyyy}.";
                cambio.Ticket.ResolvedAt = hoy;
                await _ticketRepo.UpdateAsync(cambio.Ticket);
            }

            await _uow.CommitAsync();
        }
        catch (Exception)
        {
            await _uow.RollbackAsync();
            throw;
        }

        await _audit.LogAsync("Clientes", "PLAN_CHANGE_APPROVED",
            $"Cambio de plan aprobado: {client.TbnCode} → {newPlan.Name}" +
            (midMonth ? " (mitad de mes)" : " (fin de mes)"),
            actorId, actorName, ip);

        return Result<bool>.Success(true);
    }

    // ── Rechazar cambio de plan ───────────────────────────────────────────────

    public async Task<Result<bool>> RechazarCambioAsync(
        Guid cambioId, string motivo,
        Guid actorId, string actorName, string ip)
    {
        var cambio = await _changeRepo.GetAll()
            .Include(r => r.Ticket)
            .FirstOrDefaultAsync(r => r.Id == cambioId);

        if (cambio is null)
            return Result<bool>.Failure("Solicitud no encontrada.");
        if (cambio.Status != PlanChangeStatus.Pendiente)
            return Result<bool>.Failure("Esta solicitud ya fue procesada.");

        cambio.Status          = PlanChangeStatus.Rechazado;
        cambio.RejectionReason = motivo;
        cambio.ProcessedAt     = DateTime.UtcNow;
        cambio.ProcessedById   = actorId;
        await _changeRepo.UpdateAsync(cambio);

        if (cambio.Ticket is not null)
        {
            cambio.Ticket.Status            = TicketStatus.Cerrado;
            cambio.Ticket.ResolutionMessage = $"Cambio de plan rechazado. Motivo: {motivo}";
            cambio.Ticket.ClosedAt          = DateTime.UtcNow;
            await _ticketRepo.UpdateAsync(cambio.Ticket);
        }

        await _audit.LogAsync("Clientes", "PLAN_CHANGE_REJECTED",
            $"Cambio de plan rechazado: {cambio.ClientId} — {motivo}",
            actorId, actorName, ip);

        return Result<bool>.Success(true);
    }

    // ── Listar solicitudes pendientes ─────────────────────────────────────────
    // clientId opcional: si se pasa, filtra solo las del cliente indicado
    // (usado por ClientProfilePage → pestaña Cambio de Plan).

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
}
