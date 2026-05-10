using MediatR;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record UnlockUserCommand(
    Guid   Id,
    Guid   ActorId,
    string ActorName,
    string ClientIp) : IRequest<Result>;

public class UnlockUserHandler : IRequestHandler<UnlockUserCommand, Result>
{
    private readonly IGenericRepository<UserSystem> _repo;
    private readonly AuditService                   _audit;

    public UnlockUserHandler(IGenericRepository<UserSystem> repo, AuditService audit)
    {
        _repo  = repo;
        _audit = audit;
    }

    public async Task<Result> Handle(UnlockUserCommand cmd, CancellationToken ct)
    {
        var user = await _repo.GetByIdAsync(cmd.Id);
        if (user is null) return Result.Failure("Usuario no encontrado.");
        if (user.Status != UserStatus.Bloqueado)
            return Result.Failure("La cuenta no está bloqueada.");

        user.Status              = UserStatus.Activo;
        user.FailedLoginAttempts = 0;
        user.UpdatedAt           = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "ACCOUNT_UNLOCKED",
            $"Cuenta desbloqueada por admin: {user.Email}.",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp,
            prevData: "{\"status\":\"Bloqueado\"}",
            newData:  "{\"status\":\"Activo\",\"failedAttempts\":0}");

        return Result.Success();
    }
}
