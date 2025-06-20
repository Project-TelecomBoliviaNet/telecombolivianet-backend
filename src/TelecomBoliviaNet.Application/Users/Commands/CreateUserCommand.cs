using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record CreateUserCommand(
    CreateUserDto Dto,
    Guid          ActorId,
    string        ActorName,
    string        ClientIp) : IRequest<Result<UserSystemDto>>;

public class CreateUserHandler : IRequestHandler<CreateUserCommand, Result<UserSystemDto>>
{
    private readonly IGenericRepository<UserSystem>            _repo;
    private readonly IGenericRepository<EmailActivationToken>  _activationRepo;
    private readonly IPasswordHasher                           _hasher;
    private readonly IEmailService                             _email;
    private readonly AuditService                              _audit;
    private readonly IConfiguration                            _config;
    private readonly ILogger<CreateUserHandler>                _logger;

    public CreateUserHandler(
        IGenericRepository<UserSystem>           repo,
        IGenericRepository<EmailActivationToken> activationRepo,
        IPasswordHasher                          hasher,
        IEmailService                            email,
        AuditService                             audit,
        IConfiguration                           config,
        ILogger<CreateUserHandler>               logger)
    {
        _repo           = repo;
        _activationRepo = activationRepo;
        _hasher         = hasher;
        _email          = email;
        _audit          = audit;
        _config         = config;
        _logger         = logger;
    }

    public async Task<Result<UserSystemDto>> Handle(CreateUserCommand cmd, CancellationToken ct)
    {
        var dto = cmd.Dto;

        if (await _repo.AnyAsync(u => u.Email == dto.Email))
            return Result<UserSystemDto>.Failure($"Ya existe un usuario con el correo {dto.Email}.");

        if (!Enum.TryParse<UserRole>(dto.Role, out var role))
            return Result<UserSystemDto>.Failure("Rol inválido.");

        // Verificar que el dominio del correo existe en DNS
        var domain = dto.Email.Split('@', 2).ElementAtOrDefault(1) ?? "";
        if (!await DomainExistsAsync(domain))
            return Result<UserSystemDto>.Failure(
                $"El dominio '{domain}' no existe o no puede recibir correos. Verifica que el correo sea correcto.");

        var user = new UserSystem
        {
            FullName               = dto.FullName.Trim(),
            Email                  = dto.Email.Trim().ToLower(),
            PasswordHash           = _hasher.Hash(dto.TemporaryPassword),
            Role                   = role,
            Phone                  = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
            Status                 = UserStatus.PendienteActivacion,
            RequiresPasswordChange = true,
            CreatedAt              = DateTime.UtcNow,
        };

        await _repo.AddAsync(user);
        await _repo.SaveChangesAsync();

        // Generar token de activación (48h) y enviar email
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
            _logger.LogError(ex, "Error enviando email de activación a {Email}", user.Email);
        }

        await _audit.LogAsync("Usuarios", "USER_CREATED",
            $"Usuario creado: {user.Email} con rol {user.Role}. Pendiente activación por email.",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp,
            newData: JsonSerializer.Serialize(new { user.Email, Role = user.Role.ToString(), Status = "PendienteActivacion" }));

        return Result<UserSystemDto>.Success(UserMapper.ToDto(user));
    }

    private static async Task<bool> DomainExistsAsync(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(domain);
            return addresses.Length > 0;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
