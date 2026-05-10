using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Tickets.Commands;

// ── Create ────────────────────────────────────────────────────────────────────

public record CreateCannedResponseCommand(
    CreateCannedResponseDto Dto,
    Guid                    ActorId) : IRequest<Result<CannedResponseDto>>;

public class CreateCannedResponseHandler
    : IRequestHandler<CreateCannedResponseCommand, Result<CannedResponseDto>>
{
    private readonly IGenericRepository<TicketCannedResponse> _repo;

    public CreateCannedResponseHandler(IGenericRepository<TicketCannedResponse> repo)
        => _repo = repo;

    public async Task<Result<CannedResponseDto>> Handle(
        CreateCannedResponseCommand cmd, CancellationToken ct)
    {
        var entity = new TicketCannedResponse
        {
            Title           = cmd.Dto.Title.Trim(),
            Body            = cmd.Dto.Body.Trim(),
            Category        = cmd.Dto.Category?.Trim(),
            IsActive        = true,
            CreatedByUserId = cmd.ActorId,
            CreatedAt       = DateTime.UtcNow,
        };
        await _repo.AddAsync(entity);
        await _repo.SaveChangesAsync();
        return Result<CannedResponseDto>.Success(new CannedResponseDto(
            entity.Id, entity.Title, entity.Body, entity.Category, entity.IsActive, entity.CreatedAt));
    }
}

// ── Update ────────────────────────────────────────────────────────────────────

public record UpdateCannedResponseCommand(
    Guid                    Id,
    UpdateCannedResponseDto Dto) : IRequest<Result<CannedResponseDto>>;

public class UpdateCannedResponseHandler
    : IRequestHandler<UpdateCannedResponseCommand, Result<CannedResponseDto>>
{
    private readonly IGenericRepository<TicketCannedResponse> _repo;

    public UpdateCannedResponseHandler(IGenericRepository<TicketCannedResponse> repo)
        => _repo = repo;

    public async Task<Result<CannedResponseDto>> Handle(
        UpdateCannedResponseCommand cmd, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(cmd.Id);
        if (entity is null) return Result<CannedResponseDto>.Failure("Plantilla no encontrada.");

        if (cmd.Dto.Title is not null)    entity.Title    = cmd.Dto.Title.Trim();
        if (cmd.Dto.Body is not null)     entity.Body     = cmd.Dto.Body.Trim();
        if (cmd.Dto.Category is not null) entity.Category = cmd.Dto.Category.Trim();
        if (cmd.Dto.IsActive is not null) entity.IsActive = cmd.Dto.IsActive.Value;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity);
        await _repo.SaveChangesAsync();
        return Result<CannedResponseDto>.Success(new CannedResponseDto(
            entity.Id, entity.Title, entity.Body, entity.Category, entity.IsActive, entity.CreatedAt));
    }
}

// ── Delete ────────────────────────────────────────────────────────────────────

public record DeleteCannedResponseCommand(Guid Id) : IRequest<Result<bool>>;

public class DeleteCannedResponseHandler
    : IRequestHandler<DeleteCannedResponseCommand, Result<bool>>
{
    private readonly IGenericRepository<TicketCannedResponse> _repo;

    public DeleteCannedResponseHandler(IGenericRepository<TicketCannedResponse> repo)
        => _repo = repo;

    public async Task<Result<bool>> Handle(
        DeleteCannedResponseCommand cmd, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(cmd.Id);
        if (entity is null) return Result<bool>.Failure("Plantilla no encontrada.");
        await _repo.DeleteAsync(cmd.Id);
        await _repo.SaveChangesAsync();
        return Result<bool>.Success(true);
    }
}
