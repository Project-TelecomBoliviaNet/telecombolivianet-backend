using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Plans;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Plans;

public class PlanService
{
    private readonly IGenericRepository<Plan>        _repo;
    private readonly IGenericRepository<Client>      _clientRepo;
    private readonly IGenericRepository<PlanHistory> _histRepo;
    private readonly AuditService                    _audit;
    private readonly INotifPublisher                 _notif;

    public PlanService(
        IGenericRepository<Plan>        repo,
        IGenericRepository<Client>      clientRepo,
        IGenericRepository<PlanHistory> histRepo,
        AuditService                    audit,
        INotifPublisher                 notif)
    {
        _repo       = repo;
        _clientRepo = clientRepo;
        _histRepo   = histRepo;
        _audit      = audit;
        _notif      = notif;
    }

    public async Task<IEnumerable<PlanDto>> GetAllAsync(bool onlyActive = false)
    {
        var query = _repo.GetAll();
        if (onlyActive) query = query.Where(p => p.IsActive);
        var plans = await query.OrderBy(p => p.SpeedMb).ToListAsync();
        return plans.Select(MapToDto);
    }

    public async Task<PlanDto?> GetByIdAsync(Guid id)
    {
        var plan = await _repo.GetByIdAsync(id);
        return plan is null ? null : MapToDto(plan);
    }

    public async Task<Result<PlanDto>> CreateAsync(
        CreatePlanDto dto, Guid adminId, string adminName, string ip)
    {
        var exists = await _repo.AnyAsync(p =>
            p.Name == dto.Name && p.SpeedMb == dto.SpeedMb);
        if (exists)
            return Result<PlanDto>.Failure($"Ya existe un plan llamado '{dto.Name}' con {dto.SpeedMb} Mb.");

        var plan = new Plan
        {
            Name         = dto.Name.Trim(),
            SpeedMb      = dto.SpeedMb,
            MonthlyPrice = dto.MonthlyPrice,
            IsActive     = true,
            CreatedAt    = DateTime.UtcNow
        };

        await _repo.AddAsync(plan);
        await _repo.SaveChangesAsync();
        await _audit.LogAsync("Planes", "PLAN_CREATED",
            $"Plan creado: {plan.DisplayLabel}",
            userId: adminId, userName: adminName, ip: ip);

        return Result<PlanDto>.Success(MapToDto(plan));
    }

    public async Task<Result<PlanDto>> UpdateAsync(
        Guid id, UpdatePlanDto dto, Guid adminId, string adminName, string ip)
    {
        var plan = await _repo.GetByIdAsync(id);
        if (plan is null) return Result<PlanDto>.Failure("Plan no encontrado.");

        // FIX-C: bloquear desactivación si existen clientes activos o suspendidos en el plan
        if (!dto.IsActive && plan.IsActive)
        {
            var clientesActivos = await _clientRepo.GetAllReadOnly()
                .CountAsync(c => c.PlanId == id
                              && c.Status != ClientStatus.DadoDeBaja
                              && !c.IsDeleted);
            if (clientesActivos > 0)
                return Result<PlanDto>.Failure(
                    $"No se puede desactivar el plan '{plan.Name}': " +
                    $"{clientesActivos} cliente(s) activo(s) lo tienen asignado. " +
                    $"Migre los clientes primero.");
        }

        var prev         = plan.DisplayLabel;
        var prevPrice    = plan.MonthlyPrice;
        var priceChanged = plan.MonthlyPrice != dto.MonthlyPrice;

        // FIX-H: snapshot de valores anteriores antes de aplicar el cambio
        var snapshot = new PlanHistory
        {
            PlanId        = plan.Id,
            Name          = plan.Name,
            SpeedMb       = plan.SpeedMb,
            MonthlyPrice  = plan.MonthlyPrice,
            IsActive      = plan.IsActive,
            ChangedAt     = DateTime.UtcNow,
            ChangedById   = adminId,
            ChangedByName = adminName,
            ChangeReason  = dto.ChangeReason?.Trim(),
        };
        await _histRepo.AddAsync(snapshot);
        await _histRepo.SaveChangesAsync();

        plan.Name         = dto.Name.Trim();
        plan.SpeedMb      = dto.SpeedMb;
        plan.MonthlyPrice = dto.MonthlyPrice;
        plan.IsActive     = dto.IsActive;
        plan.UpdatedAt    = DateTime.UtcNow;

        await _repo.UpdateAsync(plan);
        await _audit.LogAsync("Planes", "PLAN_UPDATED",
            $"Plan actualizado: {prev} → {plan.DisplayLabel}",
            userId: adminId, userName: adminName, ip: ip,
            prevData: $"{{\"precio\":{prevPrice}}}",
            newData:  $"{{\"precio\":{dto.MonthlyPrice}}}");

        // Notificar a todos los clientes activos del plan si cambió el precio
        if (priceChanged)
        {
            var clientes = await _clientRepo.GetAllReadOnly()
                .Where(c => c.PlanId == id && c.Status == ClientStatus.Activo)
                .ToListAsync();

            foreach (var cliente in clientes)
            {
                await _notif.PublishAsync(
                    NotifType.CAMBIO_PRECIO,
                    cliente.Id,
                    cliente.PhoneMain,
                    new Dictionary<string, string>
                    {
                        ["nombre"]       = cliente.FullName,
                        ["plan"]         = plan.Name,
                        ["precio_nuevo"] = dto.MonthlyPrice.ToString("N0"),
                    });
            }
        }

        return Result<PlanDto>.Success(MapToDto(plan));
    }

    /// <summary>
    /// FIX-H: Devuelve el historial de versiones anteriores de un plan, ordenado de más reciente a más antiguo.
    /// Cada entrada es un snapshot con los valores ANTES del cambio.
    /// </summary>
    public async Task<List<PlanHistoryDto>> GetHistoryAsync(Guid planId)
    {
        var entries = await _histRepo.GetAllReadOnly()
            .Where(h => h.PlanId == planId)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync();

        return entries.Select(MapHistoryToDto).ToList();
    }

    private static PlanDto MapToDto(Plan p) => new(
        p.Id, p.Name, p.SpeedMb, p.MonthlyPrice, p.IsActive, p.DisplayLabel);

    private static PlanHistoryDto MapHistoryToDto(PlanHistory h) => new(
        h.Id,
        h.PlanId,
        h.Name,
        h.SpeedMb,
        h.MonthlyPrice,
        h.IsActive,
        DisplayLabel: $"{h.Name} — {h.SpeedMb} Mb — Bs. {h.MonthlyPrice:N0}/mes",
        ChangedAt:    h.ChangedAt.ToString("O"),
        h.ChangedByName,
        h.ChangeReason);
}
