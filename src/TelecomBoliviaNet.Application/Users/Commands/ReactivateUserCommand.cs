using MediatR;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record ReactivateUserCommand(
    Guid   Id,
    Guid   ActorId,
    string ActorName,
    string ClientIp) : IRequest<Result>;

public class ReactivateUserHandler : IRequestHandler<ReactivateUserCommand, Result>
{
    private readonly IGenericRepository<UserSystem> _repo;
    private readonly AuditService                   _audit;

    public ReactivateUserHandler(IGenericRepository<UserSystem> repo, AuditService audit)
    {
        _repo  = repo;
        _audit = audit;
    }

    public async Task<Result> Handle(ReactivateUserCommand cmd, CancellationToken ct)
    {
        var user = await _repo.GetByIdAsync(cmd.Id);
        if (user is null)   return Result.Failure("Usuario no encontrado.");
        if (user.Status == UserStatus.Activo)
            return Result.Failure("La cuenta ya está activa.");

        user.Status    = UserStatus.Activo;
        user.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_REACTIVATED",
            $"Cuenta reactivada: {user.Email}.",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp);

        return Result.Success();
    }
}
