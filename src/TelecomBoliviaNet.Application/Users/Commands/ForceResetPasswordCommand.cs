using MediatR;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record ForceResetPasswordCommand(
    Guid   Id,
    string TempPassword,
    Guid   ActorId,
    string ActorName,
    string ClientIp) : IRequest<Result>;

public class ForceResetPasswordHandler : IRequestHandler<ForceResetPasswordCommand, Result>
{
    private readonly IGenericRepository<UserSystem> _repo;
    private readonly IPasswordHasher                _hasher;
    private readonly AuditService                   _audit;

    public ForceResetPasswordHandler(
        IGenericRepository<UserSystem> repo,
        IPasswordHasher                hasher,
        AuditService                   audit)
    {
        _repo   = repo;
        _hasher = hasher;
        _audit  = audit;
    }

    public async Task<Result> Handle(ForceResetPasswordCommand cmd, CancellationToken ct)
    {
        var user = await _repo.GetByIdAsync(cmd.Id);
        if (user is null) return Result.Failure("Usuario no encontrado.");

        user.PasswordHash           = _hasher.Hash(cmd.TempPassword);
        user.RequiresPasswordChange = true;
        user.FailedLoginAttempts    = 0;
        user.UpdatedAt              = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_PASSWORD_FORCE_RESET",
            $"Contraseña reseteada por admin: {user.Email}",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp);

        return Result.Success();
    }
}
