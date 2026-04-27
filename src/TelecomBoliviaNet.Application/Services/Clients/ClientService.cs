using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Plans;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Invoices;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Clients;

public class ClientService
{
    private readonly IGenericRepository<Client>        _clientRepo;
    private readonly IGenericRepository<Invoice>       _invoiceRepo;
    private readonly IGenericRepository<Payment>       _paymentRepo;
    private readonly IGenericRepository<PaymentInvoice> _piRepo;
    private readonly IGenericRepository<Plan>          _planRepo;
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly ITbnService       _tbn;
    private readonly AuditService      _audit;
    private readonly INotifPublisher   _notif;
    private readonly BillingService    _billing;
    private readonly IUnitOfWork              _uow;
    private readonly ILogger<ClientService>   _logger;

    public ClientService(
        IGenericRepository<Client>        clientRepo,
        IGenericRepository<Invoice>       invoiceRepo,
        IGenericRepository<Payment>       paymentRepo,
        IGenericRepository<PaymentInvoice> piRepo,
        IGenericRepository<Plan>          planRepo,
        IGenericRepository<SupportTicket> ticketRepo,
        ITbnService        tbn,
        AuditService       audit,
        INotifPublisher    notif,
        BillingService     billing,
        IUnitOfWork        uow,
        ILogger<ClientService> logger)
    {
        _clientRepo  = clientRepo;
        _invoiceRepo = invoiceRepo;
        _paymentRepo = paymentRepo;
        _piRepo      = piRepo;
        _planRepo    = planRepo;
        _ticketRepo  = ticketRepo;
        _tbn         = tbn;
        _audit       = audit;
        _notif       = notif;
        _billing     = billing;
        _uow         = uow;
        _logger      = logger;
    }
    // ── US-11 · Previsualizar próximo TBN ────────────────────────────────────
    public async Task<string> PeekTbnAsync() => await _tbn.PeekNextAsync();

    // ── US-12 · Registrar cliente ─────────────────────────────────────────────
    public async Task<Result<ClientListItemDto>> RegisterAsync(
        RegisterClientDto dto, Guid actorId, string actorName, string ip)
    {
        if (await _clientRepo.AnyAsync(c => c.IdentityCard == dto.IdentityCard))
            return Result<ClientListItemDto>.Failure($"Ya existe un cliente con el CI '{dto.IdentityCard}'.");

        if (await _clientRepo.AnyAsync(c => c.WinboxNumber == dto.WinboxNumber))
            return Result<ClientListItemDto>.Failure($"El número Winbox '{dto.WinboxNumber}' ya está registrado.");

        var plan = await _planRepo.GetByIdAsync(dto.PlanId);
        if (plan is null || !plan.IsActive)
            return Result<ClientListItemDto>.Failure("El plan seleccionado no existe o está inactivo.");

        var tbnCode = await _tbn.GenerateNextAsync();

        var client = new Client
        {
            TbnCode           = tbnCode,
            FullName          = dto.FullName.Trim(),
            IdentityCard      = dto.IdentityCard.Trim(),
            PhoneMain         = dto.PhoneMain.Trim(),
            PhoneSecondary    = dto.PhoneSecondary?.Trim(),
            Zone              = dto.Zone.Trim(),
            Street            = dto.Street?.Trim(),
            LocationRef       = dto.LocationRef?.Trim(),
            GpsLatitude       = dto.GpsLatitude,
            GpsLongitude      = dto.GpsLongitude,
            WinboxNumber      = dto.WinboxNumber.Trim(),
            InstallationDate  = DateTime.SpecifyKind(dto.InstallationDate, DateTimeKind.Utc),
            InstalledByUserId = dto.InstalledByUserId,
            PlanId            = dto.PlanId,
            HasTvCable        = dto.HasTvCable,
            OnuSerialNumber   = dto.OnuSerialNumber?.Trim(),
            Status            = ClientStatus.Activo,
            CreatedAt         = DateTime.UtcNow
        };

        // Validar método de pago antes de comenzar la transacción para fallar rápido
        PaymentMethod? paymentMethod = null;
        if (dto.PaymentMethod is not null)
        {
            if (!Enum.TryParse<PaymentMethod>(dto.PaymentMethod, out var parsedMethod))
                return Result<ClientListItemDto>.Failure("Método de pago inválido.");
            paymentMethod = parsedMethod;
        }

        var now        = DateTime.UtcNow;
        var invoiceIds = new List<Guid>();

        // Transacción explícita: cliente + facturas + pago inicial en un único commit.
        // Si cualquier paso falla, el rollback garantiza que no quede ningún registro parcial.
        // Nota: SaveChangesAsync en línea ~125 es necesario dentro de la transacción porque
        // GenerateBackfillInvoicesAsync consulta la BD por ClientId — la misma conexión
        // ve sus cambios no-commiteados gracias al aislamiento READ COMMITTED de PostgreSQL.
        await _uow.BeginTransactionAsync();
        try
        {
            await _clientRepo.AddAsync(client);

            // ── Factura de instalación ───────────────────────────────────────
            var instInvoice = new Invoice
            {
                ClientId = client.Id,
                Type     = InvoiceType.Instalacion,
                Status   = dto.PaidInstallation ? InvoiceStatus.Pagada : InvoiceStatus.Pendiente,
                Year     = now.Year,
                Month    = 0,
                Amount   = dto.InstallationCost,
                IssuedAt = now,
                DueDate  = now.AddDays(5)
            };
            await _invoiceRepo.AddAsync(instInvoice);
            if (dto.PaidInstallation) invoiceIds.Add(instInvoice.Id);

            // Flush necesario: GenerateBackfillInvoicesAsync necesita ver el cliente en BD
            await _clientRepo.SaveChangesAsync();

            // ── Facturas retroactivas desde fecha de instalación hasta hoy ───
            await _billing.GenerateBackfillInvoicesAsync(client, plan.MonthlyPrice);

            // Si se indicó pago del primer mes, marcarlo como pagado
            if (dto.PaidFirstMonth)
            {
                var installDate       = client.InstallationDate;
                var firstMonthInvoice = await _invoiceRepo.GetAll()
                    .FirstOrDefaultAsync(i =>
                        i.ClientId == client.Id &&
                        i.Year  == installDate.Year  &&
                        i.Month == installDate.Month &&
                        i.Type  == InvoiceType.Mensualidad);

                if (firstMonthInvoice is not null)
                {
                    firstMonthInvoice.Status    = InvoiceStatus.Pagada;
                    firstMonthInvoice.UpdatedAt = DateTime.UtcNow;
                    await _invoiceRepo.UpdateAsync(firstMonthInvoice);
                    invoiceIds.Add(firstMonthInvoice.Id);
                }
            }

            // ── Pago inicial ─────────────────────────────────────────────────
            if (invoiceIds.Count > 0 && paymentMethod is not null)
            {
                var paidInvoices = await _invoiceRepo.GetAll()
                    .Where(i => invoiceIds.Contains(i.Id))
                    .ToListAsync();
                var totalPaid = paidInvoices.Sum(i => i.Amount);

                var payment = new Payment
                {
                    ClientId              = client.Id,
                    Amount                = totalPaid,
                    Method                = paymentMethod.Value,
                    Bank                  = dto.Bank,
                    PhysicalReceiptNumber = dto.PhysicalReceiptNumber,
                    PaidAt                = DateTime.UtcNow,
                    RegisteredByUserId    = actorId,
                    FromWhatsApp          = false
                };
                await _paymentRepo.AddAsync(payment);
                var piEntries = invoiceIds
                    .Select(invId => new PaymentInvoice { PaymentId = payment.Id, InvoiceId = invId })
                    .ToList();
                await _piRepo.AddRangeAsync(piEntries);
                await _paymentRepo.SaveChangesAsync();
            }

            await _uow.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en transacción RegisterAsync para TBN={TbnCode}", tbnCode);
            await _uow.RollbackAsync();
            return Result<ClientListItemDto>.Failure(
                "Error al registrar el cliente. Los datos no fueron guardados.");
        }

        // Auditoría fuera de la transacción: un fallo de log no debe deshacer el registro
        await _audit.LogAsync("Clientes", "CLIENT_REGISTERED",
            $"Cliente registrado: {tbnCode} — {client.FullName}",
            userId: actorId, userName: actorName, ip: ip,
            newData: JsonSerializer.Serialize(new { client.TbnCode, client.FullName, Plan = plan.Name }));

        return Result<ClientListItemDto>.Success(new ClientListItemDto(
            client.Id, tbnCode, client.FullName, client.Zone,
            client.PhoneMain, plan.Name, client.HasTvCable, "Activo", 0, 0));
    }

    // ── US-13 · Listar clientes ───────────────────────────────────────────────
    public async Task<PagedResult<ClientListItemDto>> GetAllAsync(ClientFilterDto filter)
    {
        // BUG FIX: GetAllReadOnly para listado (no se modifican, evita ChangeTracker)
        var query = _clientRepo.GetAllReadOnly()
            .Include(c => c.Plan)
            .Include(c => c.Invoices)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = ApplyFullTextSearch(query, filter.Search);

        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "all")
            if (Enum.TryParse<ClientStatus>(filter.Status, out var status))
                query = query.Where(c => c.Status == status);

        if (filter.PlanId.HasValue)
            query = query.Where(c => c.PlanId == filter.PlanId.Value);

        query = filter.SortBy switch
        {
            "code" => query.OrderBy(c => c.TbnCode),
            "zone" => query.OrderBy(c => c.Zone),
            "name" => query.OrderBy(c => c.FullName),
            _      => query.OrderBy(c => c.TbnCode)
        };

        // BUG FIX: aplicar filtro de deuda en la query de BD ANTES de paginar.
        // El filtro post-paginación anterior daba totales y páginas incorrectos.
        if (filter.DebtFilter == "paid")
            query = query.Where(c => !c.Invoices.Any(
                i => i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida));
        else if (filter.DebtFilter == "debt")
            query = query.Where(c => c.Invoices.Any(
                i => i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida));

        var total   = await query.CountAsync();
        var clients = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var items = clients.Select(c => new
        {
            c,
            debt    = CalcDebt(c.Invoices),
            pending = CalcPendingMonths(c.Invoices)
        });

        return new PagedResult<ClientListItemDto>(
            items.Select(x => new ClientListItemDto(
                x.c.Id, x.c.TbnCode, x.c.FullName, x.c.Zone,
                x.c.PhoneMain, x.c.Plan?.Name ?? "—",
                x.c.HasTvCable, x.c.Status.ToString(), x.debt, x.pending)),
            total, filter.PageNumber, filter.PageSize);
    }

    // ── US-CLI-BUSQUEDA · Búsqueda avanzada con filtros múltiples ─────────────
    public async Task<ClientSearchResultDto> SearchAsync(ClientSearchDto dto)
    {
        var query = _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Include(c => c.Invoices)
            .AsQueryable();

        // Búsqueda full-text (nombre, TBN, CI, teléfono, email, zona)
        if (!string.IsNullOrWhiteSpace(dto.Query))
            query = ApplyFullTextSearch(query, dto.Query);

        if (!string.IsNullOrWhiteSpace(dto.Zone))
            query = query.Where(c => c.Zone == dto.Zone);

        if (!string.IsNullOrWhiteSpace(dto.Status) && Enum.TryParse<ClientStatus>(dto.Status, out var st))
            query = query.Where(c => c.Status == st);

        if (Guid.TryParse(dto.PlanId, out var planId))
            query = query.Where(c => c.PlanId == planId);

        // US-CLI-01: filtrar por tiene/no tiene email
        if (dto.HasEmail == true)  query = query.Where(c => c.Email != null && c.Email != "");
        if (dto.HasEmail == false) query = query.Where(c => c.Email == null || c.Email == "");

        // Filtro de deuda en SQL ANTES de paginar — evita counts y page sizes incorrectos
        if (dto.HasDebt == true)
            query = query.Where(c => c.Invoices.Any(
                i => i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida));
        if (dto.HasDebt == false)
            query = query.Where(c => !c.Invoices.Any(
                i => i.Status == InvoiceStatus.Pendiente || i.Status == InvoiceStatus.Vencida));

        var total   = await query.CountAsync();
        var clients = await query
            .OrderBy(c => c.TbnCode)
            .Skip((dto.Page - 1) * dto.PageSize)
            .Take(dto.PageSize)
            .ToListAsync();

        var items = clients.Select(c =>
        {
            var debt    = CalcDebt(c.Invoices);
            var pending = CalcPendingMonths(c.Invoices);
            return new ClientListItemDto(
                c.Id, c.TbnCode, c.FullName, c.Zone,
                c.PhoneMain, c.Plan?.Name ?? "—",
                c.HasTvCable, c.Status.ToString(), debt, pending);
        }).ToList();

        return new ClientSearchResultDto(items, total, dto.Page, dto.PageSize, dto.Query);
    }

    // ── US-14 · Perfil del cliente ────────────────────────────────────────────
    public async Task<ClientDetailDto?> GetByIdAsync(Guid id)
    {
        var c = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Include(c => c.InstalledBy)
            .Include(c => c.Invoices)
                .ThenInclude(i => i.PaymentInvoices)
                    .ThenInclude(pi => pi.Payment)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (c is null) return null;

        var debt    = CalcDebt(c.Invoices);
        var pending = CalcPendingMonths(c.Invoices);
        var lastPay = c.Invoices
            .SelectMany(i => i.PaymentInvoices)
            .Select(pi => pi.Payment?.PaidAt)
            .Where(d => d.HasValue)
            .OrderByDescending(d => d)
            .FirstOrDefault();
        var instPaid = c.Invoices
            .FirstOrDefault(i => i.Type == InvoiceType.Instalacion)?.Status == InvoiceStatus.Pagada;

        return new ClientDetailDto(
            c.Id, c.TbnCode, c.FullName, c.IdentityCard,
            c.PhoneMain, c.PhoneSecondary,
            c.Zone, c.Street, c.LocationRef, c.GpsLatitude, c.GpsLongitude,
            c.WinboxNumber, c.InstallationDate,
            c.InstalledByUserId, c.InstalledBy?.FullName ?? "—",
            c.Plan is null ? null! : new PlanDto(c.Plan.Id, c.Plan.Name, c.Plan.SpeedMb,
                                                  c.Plan.MonthlyPrice, c.Plan.IsActive, c.Plan.DisplayLabel),
            c.HasTvCable, c.OnuSerialNumber,
            c.Status.ToString(), c.SuspendedAt, c.CancelledAt,
            debt, pending, lastPay, instPaid, c.CreatedAt,
            // M5 campos nuevos
            c.Email,
            c.CreditBalance,
            0); // AttachmentCount se calcula en el controller si se necesita
    }

    // ── US-15 · Grid de facturas ──────────────────────────────────────────────
    public async Task<InvoiceGridDto> GetInvoiceGridAsync(Guid clientId, int year)
    {
        var invoices = await _invoiceRepo.GetAll()
            .Where(i => i.ClientId == clientId &&
                        (i.Year == year || i.Type == InvoiceType.Instalacion))
            .OrderBy(i => i.Month)
            .ToListAsync();

        var payments = await _paymentRepo.GetAll()
            .Include(p => p.RegisteredBy)
            .Include(p => p.PaymentInvoices)
                .ThenInclude(pi => pi.Invoice)
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync();

        var debt    = CalcDebt(invoices.Where(i => i.Year == year || i.Type == InvoiceType.Instalacion));
        var pending = CalcPendingMonths(invoices);
        var lastPay = payments.Select(p => (DateTime?)p.PaidAt).FirstOrDefault();

        return new InvoiceGridDto(
            invoices.Select(MapInvoice),
            payments.Select(MapPaymentDto),
            debt, pending, lastPay);
    }

    // ── US-17 · Editar cliente ────────────────────────────────────────────────
    public async Task<r> UpdateAsync(
        Guid id, UpdateClientDto dto, Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (client is null) return Result.Failure("Cliente no encontrado.");

        if (await _clientRepo.AnyAsync(c => c.IdentityCard == dto.IdentityCard && c.Id != id))
            return Result.Failure($"El CI '{dto.IdentityCard}' ya pertenece a otro cliente.");

        if (await _clientRepo.AnyAsync(c => c.WinboxNumber == dto.WinboxNumber && c.Id != id))
            return Result.Failure($"El número Winbox '{dto.WinboxNumber}' ya está en uso.");

        var plan = await _planRepo.GetByIdAsync(dto.PlanId);
        if (plan is null || !plan.IsActive)
            return Result.Failure("El plan seleccionado no existe o está inactivo.");

        var prevData = JsonSerializer.Serialize(new
        {
            client.FullName, client.IdentityCard,
            client.PhoneMain, Plan = client.Plan?.Name, client.WinboxNumber
        });

        if (client.PlanId != dto.PlanId)
            await _audit.LogAsync("Clientes", "PLAN_CHANGED",
                $"Plan cambiado en {client.TbnCode}: {client.Plan?.Name} → {plan.Name}",
                userId: actorId, userName: actorName, ip: ip);

        client.FullName        = dto.FullName.Trim();
        client.IdentityCard    = dto.IdentityCard.Trim();
        client.PhoneMain       = dto.PhoneMain.Trim();
        client.PhoneSecondary  = dto.PhoneSecondary?.Trim();
        client.Zone            = dto.Zone.Trim();
        client.Street          = dto.Street?.Trim();
        client.LocationRef     = dto.LocationRef?.Trim();
        client.GpsLatitude     = dto.GpsLatitude;
        client.GpsLongitude    = dto.GpsLongitude;
        client.WinboxNumber    = dto.WinboxNumber.Trim();
        client.PlanId          = dto.PlanId;
        client.HasTvCable      = dto.HasTvCable;
        client.OnuSerialNumber = dto.OnuSerialNumber?.Trim();
        client.UpdatedAt       = DateTime.UtcNow;

        await _clientRepo.UpdateAsync(client);
        await _audit.LogAsync("Clientes", "CLIENT_UPDATED",
            $"Cliente editado: {client.TbnCode}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: prevData,
            newData: JsonSerializer.Serialize(new
                { client.FullName, client.IdentityCard, client.PhoneMain,
                  Plan = plan.Name, client.WinboxNumber }));

        return Result.Success();
    }

    // ── US-18 · Suspender servicio + notificación WhatsApp ────────────────────
    public async Task<r> SuspendAsync(Guid id, Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (client is null) return Result.Failure("Cliente no encontrado.");
        if (client.Status == ClientStatus.Suspendido)
            return Result.Failure("El cliente ya está suspendido.");
        if (client.Status == ClientStatus.DadoDeBaja)
            return Result.Failure("No se puede suspender un cliente dado de baja.");

        client.Status      = ClientStatus.Suspendido;
        client.SuspendedAt = DateTime.UtcNow;
        client.UpdatedAt   = DateTime.UtcNow;
        await _clientRepo.UpdateAsync(client);

        await _audit.LogAsync("Clientes", "CLIENT_SUSPENDED",
            $"Servicio suspendido: {client.TbnCode} — {client.FullName}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: "{\"status\":\"Activo\"}",
            newData:  "{\"status\":\"Suspendido\"}");

        // US-18 · Notificación de suspensión vía outbox → Notifier Python envía WhatsApp
        await _notif.PublishAsync(
            NotifType.SUSPENSION,
            client.Id,
            client.PhoneMain,
            new Dictionary<string, string>
            {
                ["nombre"] = client.FullName,
                ["plan"]   = client.Plan?.Name ?? "internet",
            });

        return Result.Success();
    }

    // ── US-18 · Reactivar servicio ────────────────────────────────────────────
    public async Task<r> ReactivateAsync(Guid id, Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (client is null) return Result.Failure("Cliente no encontrado.");
        if (client.Status == ClientStatus.Activo)
            return Result.Failure("El cliente ya está activo.");

        client.Status      = ClientStatus.Activo;
        client.SuspendedAt = null;
        client.UpdatedAt   = DateTime.UtcNow;
        await _clientRepo.UpdateAsync(client);

        await _audit.LogAsync("Clientes", "CLIENT_REACTIVATED",
            $"Servicio reactivado: {client.TbnCode} — {client.FullName}",
            userId: actorId, userName: actorName, ip: ip);

        // Notificación de reactivación vía outbox → Notifier Python envía WhatsApp
        await _notif.PublishAsync(
            NotifType.REACTIVACION,
            client.Id,
            client.PhoneMain,
            new Dictionary<string, string>
            {
                ["nombre"] = client.FullName,
                ["plan"]   = client.Plan?.Name ?? "internet",
            });

        return Result.Success();
    }

    // ── US-19 · Dar de baja + ticket de recolección ───────────────────────────
    public async Task<Result<string>> CancelAsync(
        Guid id, bool confirmed, Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (client is null) return Result<string>.Failure("Cliente no encontrado.");
        if (client.Status == ClientStatus.DadoDeBaja)
            return Result<string>.Failure("El cliente ya está dado de baja.");

        var debt = CalcDebt(client.Invoices);
        if (debt > 0 && !confirmed)
            // "needs_confirmation" es la señal para el controlador
            return Result<string>.Success("needs_confirmation");

        client.Status      = ClientStatus.DadoDeBaja;
        client.CancelledAt = DateTime.UtcNow;
        client.UpdatedAt   = DateTime.UtcNow;

        // Soft delete: el cliente queda marcado como eliminado lógicamente.
        // El filtro global HasQueryFilter excluirá este registro de futuras queries.
        // Los datos históricos (facturas, pagos, tickets) se conservan intactos.
        client.IsDeleted   = true;
        client.DeletedAt   = DateTime.UtcNow;
        client.DeletedById = actorId;

        await _clientRepo.UpdateAsync(client);

        await _audit.LogAsync("Clientes", "CLIENT_CANCELLED",
            $"Cliente dado de baja: {client.TbnCode} — {client.FullName}" +
            (debt > 0 ? $" (con deuda: Bs. {debt:N2})" : ""),
            userId: actorId, userName: actorName, ip: ip);

        // US-19 · Crear ticket de recolección de equipo si tiene ONU
        if (!string.IsNullOrWhiteSpace(client.OnuSerialNumber))
        {
            var ticket = new SupportTicket
            {
                ClientId     = client.Id,
                Type         = TicketType.RecoleccionEquipo,
                Priority     = TicketPriority.Media,
                Status       = TicketStatus.Abierto,
                Origin       = TicketOrigin.Automatico,
                Description  = $"Recolección de equipo ONU (S/N: {client.OnuSerialNumber}) " +
                               $"del cliente dado de baja: {client.TbnCode} — {client.FullName}",
                CreatedByUserId = actorId,
                CreatedAt    = DateTime.UtcNow,
                DueDate      = DateTime.UtcNow.AddHours(48) // prioridad media = 48h
            };
            await _ticketRepo.AddAsync(ticket);
            await _audit.LogAsync("Tickets", "TICKET_AUTO_CREATED",
                $"Ticket de recolección creado para {client.TbnCode} (ONU: {client.OnuSerialNumber})",
                userId: actorId, userName: actorName, ip: ip);

            return Result<string>.Success("cancelled_with_ticket");
        }

        return Result<string>.Success("cancelled");
    }

    // ── US-15 · Registrar pago + notificación WhatsApp ───────────────────────
    public async Task<r> RegisterPaymentAsync(
        RegisterPaymentDto dto, Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetByIdAsync(dto.ClientId);
        if (client is null) return Result.Failure("Cliente no encontrado.");

        if (!Enum.TryParse<PaymentMethod>(dto.Method, out var method))
            return Result.Failure("Método de pago inválido.");

        var invoices = await _invoiceRepo.GetAll()
            .Where(i => dto.InvoiceIds.Contains(i.Id) && i.ClientId == dto.ClientId)
            .ToListAsync();

        if (invoices.Count != dto.InvoiceIds.Count)
            return Result.Failure("Una o más facturas no pertenecen a este cliente.");

        if (invoices.Any(i => i.Status == InvoiceStatus.Pagada))
            return Result.Failure("Una o más facturas seleccionadas ya están pagadas.");

        var payment = new Payment
        {
            ClientId              = dto.ClientId,
            Amount                = dto.Amount,
            Method                = method,
            Bank                  = dto.Bank,
            PhysicalReceiptNumber = dto.PhysicalReceiptNumber,
            PaidAt                = DateTime.SpecifyKind(dto.PaidAt, DateTimeKind.Utc),
            RegisteredByUserId    = actorId,
            RegisteredAt          = DateTime.UtcNow,
            FromWhatsApp          = false
        };
        await _paymentRepo.AddAsync(payment);

        foreach (var inv in invoices)
        {
            inv.Status = InvoiceStatus.Pagada;
            await _invoiceRepo.UpdateAsync(inv);
            await _piRepo.AddAsync(new PaymentInvoice { PaymentId = payment.Id, InvoiceId = inv.Id });
        }

        // Persistir pago y relaciones PaymentInvoice. UpdateAsync ya hace SaveChanges
        // por cada factura, pero AddAsync(payment) y AddAsync(PaymentInvoice) solo
        // acumulan en el ChangeTracker. Flush explícito para dejar todo consistente
        // antes de registrar el audit log y publicar la notificación.
        await _paymentRepo.SaveChangesAsync();

        var coveredDesc = string.Join(", ", invoices.Select(i =>
            i.Type == InvoiceType.Instalacion
                ? "Instalación"
                : $"{new DateTime(i.Year, i.Month, 1):MMMM yyyy}"));

        var action = dto.ConfirmedDuplicate == true ? "PAYMENT_REGISTERED_DUPLICATE" : "PAYMENT_REGISTERED";
        await _audit.LogAsync("Pagos", action,
            $"Pago registrado: {client.TbnCode} — Bs. {dto.Amount:N2} — {coveredDesc}"
            + (dto.ConfirmedDuplicate == true ? " (DUPLICADO CONFIRMADO por el usuario)" : ""),
            userId: actorId, userName: actorName, ip: ip);

        // US-15 · Confirmación de pago vía outbox → Notifier Python envía WhatsApp
        // CONFIRMACION_PAGO tiene Inmediato=true → se envía sin respetar ventana horaria
        await _notif.PublishAsync(
            NotifType.CONFIRMACION_PAGO,
            client.Id,
            client.PhoneMain,
            new Dictionary<string, string>
            {
                ["nombre"]  = client.FullName,
                ["monto"]   = $"{dto.Amount:F2}",
                ["periodo"] = coveredDesc,
            },
            referenciaId: payment.Id);

        return Result.Success();
    }

    // ── Bot: buscar cliente por teléfono (instancia — BUG FIX) ─────────────────
    // BUG FIX: movido de ClientServiceBotExtensions (static) a método de instancia.
    // El controller ya no necesita inyectar _clientRepo explícitamente para esta llamada.
    public async Task<ClientBotDto?> GetByPhoneAsync(string rawPhone)
    {
        var phone = rawPhone.Trim();
        if (phone.StartsWith("591") && phone.Length > 3)
            phone = phone[3..];

        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c =>
                c.PhoneMain      == phone    ||
                c.PhoneMain      == rawPhone ||
                c.PhoneSecondary == phone    ||
                c.PhoneSecondary == rawPhone);

        if (client is null) return null;

        var pendingInvoices = client.Invoices
            .Where(i => i.Status is InvoiceStatus.Pendiente or InvoiceStatus.Vencida)
            .ToList();

        return new ClientBotDto(
            Id:            client.Id.ToString(),
            TbnCode:       client.TbnCode,
            FullName:      client.FullName,
            PhoneMain:     client.PhoneMain,
            Status:        client.Status.ToString(),
            PlanId:        client.PlanId.ToString(),
            PlanName:      client.Plan?.Name ?? "—",
            PlanSpeedMbps: client.Plan?.SpeedMb ?? 0,
            TotalDebt:     pendingInvoices.Sum(i => i.Amount),
            PendingMonths: pendingInvoices.Count(i => i.Type == InvoiceType.Mensualidad),
            Zone:          client.Zone);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IQueryable<Client> ApplyFullTextSearch(IQueryable<Client> query, string searchTerm)
    {
        var q = searchTerm.ToLower();
        return query.Where(c =>
            c.FullName.ToLower().Contains(q)       ||
            c.TbnCode.ToLower().Contains(q)        ||
            c.IdentityCard.ToLower().Contains(q)   ||
            c.WinboxNumber.ToLower().Contains(q)   ||
            c.PhoneMain.Contains(q)                ||
            c.Zone.ToLower().Contains(q)           ||
            (c.PhoneSecondary != null && c.PhoneSecondary.Contains(q)) ||
            (c.Email != null && c.Email.ToLower().Contains(q)));
    }

    private static decimal CalcDebt(IEnumerable<Invoice> invoices) =>
        invoices.Where(i => i.IsUnpaid).Sum(i => i.Amount);

    private static int CalcPendingMonths(IEnumerable<Invoice> invoices) =>
        invoices.Count(i => i.Type == InvoiceType.Mensualidad && i.IsUnpaid);

    private static InvoiceDto MapInvoice(Invoice i) => new(
        i.Id, i.Type.ToString(), i.Status.ToString(),
        i.Year, i.Month, i.Amount, i.IssuedAt, i.DueDate, i.Notes);

    private static PaymentDto MapPaymentDto(Payment p) => new(
        p.Id, p.Amount, p.Method.ToString(), p.Bank,
        p.PaidAt, p.RegisteredAt,
        p.RegisteredBy?.FullName ?? "Sistema",
        p.FromWhatsApp,
        p.ReceiptImageUrl,
        CanVoid: !p.IsVoided && (DateTime.UtcNow - p.RegisteredAt).TotalDays <= 30,
        IsVoided: p.IsVoided,
        p.PaymentInvoices.Select(pi =>
            pi.Invoice?.Type == InvoiceType.Instalacion
                ? "Instalación"
                : pi.Invoice is null ? "—"
                : $"{new DateTime(pi.Invoice.Year, pi.Invoice.Month, 1):MMMM yyyy}"));
}