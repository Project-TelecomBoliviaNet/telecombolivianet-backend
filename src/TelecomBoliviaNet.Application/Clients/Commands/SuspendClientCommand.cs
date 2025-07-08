using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Clients.Commands;

public record SuspendClientCommand(
    Guid   ClientId,
    Guid   ActorId,
    string ActorName,
    string ClientIp) : IRequest<Result>;

public class SuspendClientHandler : IRequestHandler<SuspendClientCommand, Result>
{
    private readonly IGenericRepository<Client> _clientRepo;
    private readonly AuditService               _audit;
    private readonly INotifPublisher            _notif;

    public SuspendClientHandler(
        IGenericRepository<Client> clientRepo,
        AuditService               audit,
        INotifPublisher            notif)
    {
        _clientRepo = clientRepo;
        _audit      = audit;
        _notif      = notif;
    }

    public async Task<Result> Handle(SuspendClientCommand cmd, CancellationToken ct)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == cmd.ClientId, ct);

        if (client is null) return Result.Failure("Cliente no encontrado.");
        if (client.Status == ClientStatus.Suspendido)
            return Result.Failure("El cliente ya está suspendido.");
        if (client.Status == ClientStatus.DadoDeBaja)
            return Result.Failure("No se puede suspender un cliente dado de baja.");

        client.Suspend();
        await _clientRepo.UpdateAsync(client);

        await _audit.LogAsync("Clientes", "CLIENT_SUSPENDED",
            $"Servicio suspendido: {client.TbnCode} — {client.FullName}",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp,
            prevData: "{\"status\":\"Activo\"}",
            newData:  "{\"status\":\"Suspendido\"}",
            entityId: client.Id);

        await _notif.PublishAsync(
            NotifType.SUSPENSION,
            client.Id,
            client.PhoneMain,
            new Dictionary<string, string>
            {
                ["nombre"] = client.FullName,
                ["plan"]   = client.Plan?.Name ?? "internet",
            });

        return Result.Success();
    }
}
