using MediatR;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record DeactivateUserCommand(
    Guid   Id,
    Guid   ActorId,
    string ActorName,
    string ClientIp) : IRequest<Result>;

public class DeactivateUserHandler : IRequestHandler<DeactivateUserCommand, Result>
{
    private readonly IGenericRepository<UserSystem> _repo;
    private readonly AuditService                   _audit;

    public DeactivateUserHandler(IGenericRepository<UserSystem> repo, AuditService audit)
    {
        _repo  = repo;
        _audit = audit;
    }

    public async Task<Result> Handle(DeactivateUserCommand cmd, CancellationToken ct)
    {
        var user = await _repo.GetByIdAsync(cmd.Id);
        if (user is null)     return Result.Failure("Usuario no encontrado.");
        if (user.Status == UserStatus.Inactivo)
            return Result.Failure("La cuenta ya está desactivada.");

        var prev       = user.Status.ToString();
        user.Status    = UserStatus.Inactivo;
        user.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_DEACTIVATED",
            $"Cuenta desactivada: {user.Email}.",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp,
            prevData: $"{{\"status\":\"{prev}\"}}",
            newData:  "{\"status\":\"Inactivo\"}");

        return Result.Success();
    }
}
