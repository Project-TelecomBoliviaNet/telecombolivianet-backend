using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record ConfirmEmailCommand(string Token) : IRequest<Result>;

public class ConfirmEmailHandler : IRequestHandler<ConfirmEmailCommand, Result>
{
    private readonly IGenericRepository<EmailActivationToken> _activationRepo;
    private readonly IGenericRepository<UserSystem>           _userRepo;
    private readonly AuditService                             _audit;

    public ConfirmEmailHandler(
        IGenericRepository<EmailActivationToken> activationRepo,
        IGenericRepository<UserSystem>           userRepo,
        AuditService                             audit)
    {
        _activationRepo = activationRepo;
        _userRepo       = userRepo;
        _audit          = audit;
    }

    public async Task<Result> Handle(ConfirmEmailCommand cmd, CancellationToken ct)
    {
        var token = await _activationRepo.GetAll()
            .FirstOrDefaultAsync(t => t.Token == cmd.Token, ct);

        if (token is null || token.Used)
            return Result.Failure("El enlace de activación no es válido o ya fue utilizado.");

        if (token.IsExpired)
            return Result.Failure("El enlace de activación ha expirado. Solicita al administrador que reenvíe la invitación.");

        var user = await _userRepo.GetByIdAsync(token.UserId);
        if (user is null)
            return Result.Failure("Usuario no encontrado.");

        if (user.Status != UserStatus.PendienteActivacion)
            return Result.Failure("Esta cuenta ya fue activada anteriormente.");

        user.Status    = UserStatus.Activo;
        user.UpdatedAt = DateTime.UtcNow;
        token.Used     = true;

        await _userRepo.UpdateAsync(user);
        await _activationRepo.UpdateAsync(token);
        await _userRepo.SaveChangesAsync();

        await _audit.LogAsync("Usuarios", "EMAIL_CONFIRMED",
            $"Cuenta activada por confirmación de email: {user.Email}",
            userId: user.Id, userName: user.FullName);

        return Result.Success();
    }
}
