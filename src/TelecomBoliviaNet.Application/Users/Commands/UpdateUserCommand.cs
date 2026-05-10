using MediatR;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Users.Commands;

public record UpdateUserCommand(
    Guid          Id,
    UpdateUserDto Dto,
    Guid          ActorId,
    string        ActorName,
    string        ClientIp) : IRequest<Result<UserSystemDto>>;

public class UpdateUserHandler : IRequestHandler<UpdateUserCommand, Result<UserSystemDto>>
{
    private readonly IGenericRepository<UserSystem> _repo;
    private readonly AuditService                   _audit;

    public UpdateUserHandler(IGenericRepository<UserSystem> repo, AuditService audit)
    {
        _repo  = repo;
        _audit = audit;
    }

    public async Task<Result<UserSystemDto>> Handle(UpdateUserCommand cmd, CancellationToken ct)
    {
        var user = await _repo.GetByIdAsync(cmd.Id);
        if (user is null)
            return Result<UserSystemDto>.Failure("Usuario no encontrado.");

        if (await _repo.AnyAsync(u => u.Email == cmd.Dto.Email && u.Id != cmd.Id))
            return Result<UserSystemDto>.Failure($"El correo {cmd.Dto.Email} ya está en uso.");

        if (!Enum.TryParse<UserRole>(cmd.Dto.Role, out var role))
            return Result<UserSystemDto>.Failure("Rol inválido.");

        var prev = JsonSerializer.Serialize(new
            { user.FullName, user.Email, Role = user.Role.ToString() });

        user.FullName  = cmd.Dto.FullName.Trim();
        user.Email     = cmd.Dto.Email.Trim().ToLower();
        if (cmd.Dto.Phone is not null)
            user.Phone = string.IsNullOrWhiteSpace(cmd.Dto.Phone) ? null : cmd.Dto.Phone.Trim();
        user.Role      = role;
        user.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_UPDATED",
            $"Usuario actualizado: {user.Email}.",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp,
            prevData: prev,
            newData: JsonSerializer.Serialize(new
                { user.FullName, user.Email, Role = user.Role.ToString() }));

        return Result<UserSystemDto>.Success(UserMapper.ToDto(user));
    }
}
