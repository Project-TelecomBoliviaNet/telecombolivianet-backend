using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record ForgotPasswordCommand(string Email) : IRequest<Result<ForgotPasswordResultDto>>;

public class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, Result<ForgotPasswordResultDto>>
{
    private readonly IGenericRepository<UserSystem>         _repo;
    private readonly IGenericRepository<PasswordResetToken> _resetRepo;
    private readonly IEmailService                          _email;
    private readonly IConfiguration                        _config;
    private readonly ILogger<ForgotPasswordHandler>         _logger;

    public ForgotPasswordHandler(
        IGenericRepository<UserSystem>         repo,
        IGenericRepository<PasswordResetToken> resetRepo,
        IEmailService                          email,
        IConfiguration                         config,
        ILogger<ForgotPasswordHandler>         logger)
    {
        _repo      = repo;
        _resetRepo = resetRepo;
        _email     = email;
        _config    = config;
        _logger    = logger;
    }

    public async Task<Result<ForgotPasswordResultDto>> Handle(
        ForgotPasswordCommand cmd, CancellationToken ct)
    {
        var user = await _repo.GetAll()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == cmd.Email.ToLower(), ct);

        // Sistema interno: se da feedback directo para no bloquear a empleados.
        if (user is null)
            return Result<ForgotPasswordResultDto>.Failure(
                "No existe ninguna cuenta registrada con ese correo electrónico.");

        if (user.Status == UserStatus.Inactivo)
            return Result<ForgotPasswordResultDto>.Failure(
                "Esta cuenta está desactivada. Contacta al administrador del sistema.");

        if (user.Status == UserStatus.PendienteActivacion)
            return Result<ForgotPasswordResultDto>.Failure(
                "Esta cuenta aún no ha sido activada. Revisa tu correo para el enlace de activación.");

        var prevTokens = await _resetRepo.GetAll()
            .Where(t => t.UserId == user.Id && !t.Used && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var pt in prevTokens)
        {
            pt.Used = true;
            await _resetRepo.UpdateAsync(pt);
        }

        var token = Guid.NewGuid().ToString("N");
        var resetToken = new PasswordResetToken
        {
            UserId    = user.Id,
            Token     = token,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Channel   = "Email",
            SentTo    = UserMapper.MaskEmail(user.Email),
        };
        await _resetRepo.AddAsync(resetToken);
        await _resetRepo.SaveChangesAsync();

        var frontendUrl = _config["App:FrontendUrl"]?.TrimEnd('/') ?? "http://localhost";
        var resetLink   = $"{frontendUrl}/reset-password?token={token}";

        try
        {
            await _email.SendPasswordResetAsync(user.Email, user.FullName, resetLink);
        }
        catch (Exception ex)
        {
            // Un fallo de email no debe romper el flujo — el token ya fue generado y guardado.
            _logger.LogError(ex, "Error enviando email de reset a {Email}", user.Email);
        }

        _logger.LogInformation("PASSWORD_RESET solicitado para {Email}", user.Email);

        return Result<ForgotPasswordResultDto>.Success(
            new ForgotPasswordResultDto(
                $"Te enviamos el enlace de recuperación a {resetToken.SentTo}. Revisa tu correo.",
                "Email",
                resetToken.SentTo));
    }
}
