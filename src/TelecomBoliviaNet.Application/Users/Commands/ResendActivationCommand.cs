using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record ResendActivationCommand(
    Guid   UserId,
    Guid   ActorId,
    string ActorName,
    string Ip) : IRequest<Result>;

public class ResendActivationHandler : IRequestHandler<ResendActivationCommand, Result>
{
    private readonly IGenericRepository<UserSystem>           _userRepo;
    private readonly IGenericRepository<EmailActivationToken> _activationRepo;
    private readonly IEmailService                            _email;
    private readonly AuditService                             _audit;
    private readonly IConfiguration                           _config;
    private readonly ILogger<ResendActivationHandler>         _logger;

    public ResendActivationHandler(
        IGenericRepository<UserSystem>           userRepo,
        IGenericRepository<EmailActivationToken> activationRepo,
        IEmailService                            email,
        AuditService                             audit,
        IConfiguration                           config,
        ILogger<ResendActivationHandler>         logger)
    {
        _userRepo       = userRepo;
        _activationRepo = activationRepo;
        _email          = email;
        _audit          = audit;
        _config         = config;
        _logger         = logger;
    }

    public async Task<Result> Handle(ResendActivationCommand cmd, CancellationToken ct)
    {
        var user = await _userRepo.GetByIdAsync(cmd.UserId);
        if (user is null)
            return Result.Failure("Usuario no encontrado.");

        if (user.Status != UserStatus.PendienteActivacion)
            return Result.Failure("Esta cuenta ya está activa o fue desactivada. No se puede reenviar la activación.");

        // Invalidar tokens anteriores no usados
        var prevTokens = await _activationRepo.GetAll()
            .Where(t => t.UserId == cmd.UserId && !t.Used && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var pt in prevTokens)
        {
            pt.Used = true;
            await _activationRepo.UpdateAsync(pt);
        }

        // Generar nuevo token (48h)
        var token = Guid.NewGuid().ToString("N");
        var activationToken = new EmailActivationToken
        {
            UserId    = user.Id,
            Token     = token,
            ExpiresAt = DateTime.UtcNow.AddHours(48),
        };
        await _activationRepo.AddAsync(activationToken);
        await _activationRepo.SaveChangesAsync();

        var frontendUrl    = _config["App:FrontendUrl"]?.TrimEnd('/') ?? "http://localhost";
        var activationLink = $"{frontendUrl}/confirm-email?token={token}";

        try
        {
            await _email.SendAccountActivationAsync(user.Email, user.FullName, activationLink);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reenviando email de activación a {Email}", user.Email);
        }

        await _audit.LogAsync("Usuarios", "ACTIVATION_RESENT",
            $"Email de activación reenviado a {user.Email} por {cmd.ActorName}.",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.Ip);

        return Result.Success();
    }
}
