using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record ResetPasswordCommand(
    string Token,
    string NewPassword,
    string ConfirmPassword) : IRequest<Result>;

public class ResetPasswordHandler : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly IGenericRepository<UserSystem>         _repo;
    private readonly IGenericRepository<PasswordResetToken> _resetRepo;
    private readonly IPasswordHasher                        _hasher;
    private readonly IEmailService                          _email;

    public ResetPasswordHandler(
        IGenericRepository<UserSystem>         repo,
        IGenericRepository<PasswordResetToken> resetRepo,
        IPasswordHasher                        hasher,
        IEmailService                          email)
    {
        _repo      = repo;
        _resetRepo = resetRepo;
        _hasher    = hasher;
        _email     = email;
    }

    public async Task<Result> Handle(ResetPasswordCommand cmd, CancellationToken ct)
    {
        if (cmd.NewPassword != cmd.ConfirmPassword)
            return Result.Failure("Las contraseñas no coinciden.");

        if (cmd.NewPassword.Length < 8)
            return Result.Failure("La contraseña debe tener al menos 8 caracteres.");

        var resetToken = await _resetRepo.GetAll()
            .FirstOrDefaultAsync(t => t.Token == cmd.Token, ct);

        if (resetToken is null || resetToken.Used)
            return Result.Failure("Token inválido o ya utilizado.");

        if (resetToken.IsExpired)
            return Result.Failure("El token ha expirado. Solicita uno nuevo.");

        var user = await _repo.GetByIdAsync(resetToken.UserId);
        if (user is null) return Result.Failure("Usuario no encontrado.");

        user.PasswordHash           = _hasher.Hash(cmd.NewPassword);
        user.RequiresPasswordChange = false;
        user.FailedLoginAttempts    = 0;
        user.UpdatedAt              = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        resetToken.Used = true;
        await _resetRepo.UpdateAsync(resetToken);

        await _email.SendPasswordChangedNotificationAsync(user.Email, user.FullName);

        return Result.Success();
    }
}
