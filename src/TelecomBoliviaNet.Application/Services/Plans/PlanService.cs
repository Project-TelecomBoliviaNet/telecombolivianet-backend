using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Plans;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Plans;

public class PlanService
{
    private readonly IGenericRepository<Plan> _repo;
    private readonly AuditService _audit;

    public PlanService(IGenericRepository<Plan> repo, AuditService audit)
    {
        _repo  = repo;
        _audit = audit;
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

        var prev = plan.DisplayLabel;

        plan.Name         = dto.Name.Trim();
        plan.SpeedMb      = dto.SpeedMb;
        plan.MonthlyPrice = dto.MonthlyPrice;
        plan.IsActive     = dto.IsActive;
        plan.UpdatedAt    = DateTime.UtcNow;

        await _repo.UpdateAsync(plan);
        await _audit.LogAsync("Planes", "PLAN_UPDATED",
            $"Plan actualizado: {prev} → {plan.DisplayLabel}",
            userId: adminId, userName: adminName, ip: ip);

        return Result<PlanDto>.Success(MapToDto(plan));
    }

    private static PlanDto MapToDto(Plan p) => new(
        p.Id, p.Name, p.SpeedMb, p.MonthlyPrice, p.IsActive, p.DisplayLabel);
}
