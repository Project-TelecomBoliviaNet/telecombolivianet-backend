using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Tickets.Commands;

// ── Create ──────────────────────────────────────────────────────────────────

public record CreateSlaPlanCommand(CreateSlaPlanDto Dto) : IRequest<Result<SlaPlanDto>>;

public class CreateSlaPlanHandler : IRequestHandler<CreateSlaPlanCommand, Result<SlaPlanDto>>
{
    private readonly IGenericRepository<SlaPlan> _slaPlanRepo;

    public CreateSlaPlanHandler(IGenericRepository<SlaPlan> slaPlanRepo) => _slaPlanRepo = slaPlanRepo;

    public async Task<Result<SlaPlanDto>> Handle(CreateSlaPlanCommand cmd, CancellationToken ct)
    {
        var dto = cmd.Dto;
        if (!new[] { "Critica", "Alta", "Media", "Baja" }.Contains(dto.Priority))
            return Result<SlaPlanDto>.Failure("Prioridad inválida.");
        if (await _slaPlanRepo.AnyAsync(p => p.Priority == dto.Priority))
            return Result<SlaPlanDto>.Failure($"Ya existe un plan SLA para la prioridad {dto.Priority}.");
        if (!Enum.TryParse<SlaSchedule>(dto.Schedule, out var sched))
            return Result<SlaPlanDto>.Failure("Horario inválido. Use: Veinticuatro7 | Laboral");

        var plan = new SlaPlan
        {
            Name = dto.Name.Trim(), Priority = dto.Priority,
            FirstResponseMinutes = dto.FirstResponseMinutes,
            ResolutionMinutes    = dto.ResolutionMinutes,
            Schedule             = sched, IsActive = true, CreatedAt = DateTime.UtcNow,
        };
        await _slaPlanRepo.AddAsync(plan);
        return Result<SlaPlanDto>.Success(TicketHelper.MapSlaPlan(plan));
    }
}

// ── Update ──────────────────────────────────────────────────────────────────

public record UpdateSlaPlanCommand(Guid Id, UpdateSlaPlanDto Dto) : IRequest<Result<SlaPlanDto>>;

public class UpdateSlaPlanHandler : IRequestHandler<UpdateSlaPlanCommand, Result<SlaPlanDto>>
{
    private readonly IGenericRepository<SlaPlan> _slaPlanRepo;

    public UpdateSlaPlanHandler(IGenericRepository<SlaPlan> slaPlanRepo) => _slaPlanRepo = slaPlanRepo;

    public async Task<Result<SlaPlanDto>> Handle(UpdateSlaPlanCommand cmd, CancellationToken ct)
    {
        var plan = await _slaPlanRepo.GetByIdAsync(cmd.Id);
        if (plan is null) return Result<SlaPlanDto>.Failure("Plan SLA no encontrado.");

        var dto = cmd.Dto;
        if (dto.Name is not null)                plan.Name                 = dto.Name.Trim();
        if (dto.FirstResponseMinutes.HasValue)   plan.FirstResponseMinutes = dto.FirstResponseMinutes.Value;
        if (dto.ResolutionMinutes.HasValue)      plan.ResolutionMinutes    = dto.ResolutionMinutes.Value;
        if (dto.IsActive.HasValue)               plan.IsActive             = dto.IsActive.Value;
        if (dto.Schedule is not null)
        {
            if (!Enum.TryParse<SlaSchedule>(dto.Schedule, out var sched))
                return Result<SlaPlanDto>.Failure("Horario inválido.");
            plan.Schedule = sched;
        }
        await _slaPlanRepo.UpdateAsync(plan);
        return Result<SlaPlanDto>.Success(TicketHelper.MapSlaPlan(plan));
    }
}

// ── Delete ──────────────────────────────────────────────────────────────────

public record DeleteSlaPlanCommand(Guid Id) : IRequest<Result<bool>>;

public class DeleteSlaPlanHandler : IRequestHandler<DeleteSlaPlanCommand, Result<bool>>
{
    private readonly IGenericRepository<SlaPlan> _slaPlanRepo;

    public DeleteSlaPlanHandler(IGenericRepository<SlaPlan> slaPlanRepo) => _slaPlanRepo = slaPlanRepo;

    public async Task<Result<bool>> Handle(DeleteSlaPlanCommand cmd, CancellationToken ct)
    {
        var plan = await _slaPlanRepo.GetByIdAsync(cmd.Id);
        if (plan is null) return Result<bool>.Failure("Plan SLA no encontrado.");
        await _slaPlanRepo.DeleteAsync(plan.Id);
        return Result<bool>.Success(true);
    }
}
