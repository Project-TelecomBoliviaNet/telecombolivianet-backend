using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Clients.Commands;

public record UpdateClientCommand(
    Guid            ClientId,
    UpdateClientDto Dto,
    Guid            ActorId,
    string          ActorName,
    string          ClientIp) : IRequest<Result>;

public class UpdateClientHandler : IRequestHandler<UpdateClientCommand, Result>
{
    private readonly IGenericRepository<Client> _clientRepo;
    private readonly IGenericRepository<Plan>   _planRepo;
    private readonly AuditService               _audit;

    public UpdateClientHandler(
        IGenericRepository<Client> clientRepo,
        IGenericRepository<Plan>   planRepo,
        AuditService               audit)
    {
        _clientRepo = clientRepo;
        _planRepo   = planRepo;
        _audit      = audit;
    }

    public async Task<Result> Handle(UpdateClientCommand cmd, CancellationToken ct)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == cmd.ClientId, ct);

        if (client is null) return Result.Failure("Cliente no encontrado.");

        if (await _clientRepo.AnyAsync(c => c.IdentityCard == cmd.Dto.IdentityCard && c.Id != cmd.ClientId))
            return Result.Failure($"El CI '{cmd.Dto.IdentityCard}' ya pertenece a otro cliente.");

        if (await _clientRepo.AnyAsync(c => c.WinboxNumber == cmd.Dto.WinboxNumber && c.Id != cmd.ClientId))
            return Result.Failure($"El número Winbox '{cmd.Dto.WinboxNumber}' ya está en uso.");

        var plan = await _planRepo.GetByIdAsync(cmd.Dto.PlanId);
        if (plan is null || !plan.IsActive)
            return Result.Failure("El plan seleccionado no existe o está inactivo.");

        var prevData = JsonSerializer.Serialize(new
        {
            client.FullName, client.IdentityCard,
            client.PhoneMain, Plan = client.Plan?.Name, client.WinboxNumber
        });

        if (client.PlanId != cmd.Dto.PlanId)
            await _audit.LogAsync("Clientes", "PLAN_CHANGED",
                $"Plan cambiado en {client.TbnCode}: {client.Plan?.Name} → {plan.Name}",
                userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp, entityId: client.Id);

        client.FullName        = cmd.Dto.FullName.Trim();
        client.IdentityCard    = cmd.Dto.IdentityCard.Trim();
        client.PhoneMain       = cmd.Dto.PhoneMain.Trim();
        client.PhoneSecondary  = cmd.Dto.PhoneSecondary?.Trim();
        client.Zone            = cmd.Dto.Zone.Trim();
        client.Street          = cmd.Dto.Street?.Trim();
        client.LocationRef     = cmd.Dto.LocationRef?.Trim();
        client.GpsLatitude     = cmd.Dto.GpsLatitude;
        client.GpsLongitude    = cmd.Dto.GpsLongitude;
        client.WinboxNumber    = cmd.Dto.WinboxNumber.Trim();
        client.PlanId          = cmd.Dto.PlanId;
        client.HasTvCable      = cmd.Dto.HasTvCable;
        client.OnuSerialNumber = cmd.Dto.OnuSerialNumber?.Trim();
        client.UpdatedAt       = DateTime.UtcNow;

        if (cmd.Dto.RowVersion > 0)
            client.Xmin = cmd.Dto.RowVersion;

        try
        {
            await _clientRepo.UpdateAsync(client);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Conflict(
                "El cliente fue modificado por otro usuario mientras editabas. " +
                "Recarga los datos e inténtalo de nuevo.");
        }

        await _audit.LogAsync("Clientes", "CLIENT_UPDATED",
            $"Cliente editado: {client.TbnCode}",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp,
            prevData: prevData,
            newData: JsonSerializer.Serialize(new
                { client.FullName, client.IdentityCard, client.PhoneMain,
                  Plan = plan.Name, client.WinboxNumber }),
            entityId: client.Id);

        return Result.Success();
    }
}
