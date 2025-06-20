using MediatR;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record SoftDeleteUserCommand(
    Guid   Id,
    string Justificacion,
    Guid   ActorId,
    string ActorName,
    string ClientIp) : IRequest<Result>;

public class SoftDeleteUserHandler : IRequestHandler<SoftDeleteUserCommand, Result>
{
    private readonly IGenericRepository<UserSystem> _repo;
    private readonly AuditService                   _audit;

    public SoftDeleteUserHandler(IGenericRepository<UserSystem> repo, AuditService audit)
    {
        _repo  = repo;
        _audit = audit;
    }

    public async Task<Result> Handle(SoftDeleteUserCommand cmd, CancellationToken ct)
    {
        var user = await _repo.GetByIdAsync(cmd.Id);
        if (user is null)    return Result.Failure("Usuario no encontrado.");
        if (user.IsDeleted)  return Result.Failure("El usuario ya está eliminado.");

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.Status    = UserStatus.Inactivo;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_SOFT_DELETED",
            $"Usuario dado de baja: {user.Email} razón={cmd.Justificacion}",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp);

        return Result.Success();
    }
}
