using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Clients.Helpers;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Clients.Commands;

public record CancelClientCommand(
    Guid   ClientId,
    bool   Confirmed,
    Guid   ActorId,
    string ActorName,
    string ClientIp) : IRequest<Result<string>>;

public class CancelClientHandler : IRequestHandler<CancelClientCommand, Result<string>>
{
    private readonly IGenericRepository<Client>        _clientRepo;
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly AuditService                      _audit;

    public CancelClientHandler(
        IGenericRepository<Client>        clientRepo,
        IGenericRepository<SupportTicket> ticketRepo,
        AuditService                      audit)
    {
        _clientRepo = clientRepo;
        _ticketRepo = ticketRepo;
        _audit      = audit;
    }

    public async Task<Result<string>> Handle(CancelClientCommand cmd, CancellationToken ct)
    {
        var client = await _clientRepo.GetAll()
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c => c.Id == cmd.ClientId, ct);

        if (client is null) return Result<string>.Failure("Cliente no encontrado.");
        if (client.Status == ClientStatus.DadoDeBaja)
            return Result<string>.Failure("El cliente ya está dado de baja.");

        var debt = ClientProjectionHelpers.CalcDebt(client.Invoices);
        if (debt > 0 && !cmd.Confirmed)
            return Result<string>.Success("needs_confirmation");

        client.Cancel();
        client.IsDeleted   = true;
        client.DeletedAt   = DateTime.UtcNow;
        client.DeletedById = cmd.ActorId;

        await _clientRepo.UpdateAsync(client);

        await _audit.LogAsync("Clientes", "CLIENT_CANCELLED",
            $"Cliente dado de baja: {client.TbnCode} — {client.FullName}" +
            (debt > 0 ? $" (con deuda: Bs. {debt:N2})" : ""),
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp, entityId: client.Id);

        if (!string.IsNullOrWhiteSpace(client.OnuSerialNumber))
        {
            var ticket = new SupportTicket
            {
                ClientId        = client.Id,
                Type            = TicketType.RecoleccionEquipo,
                Priority        = TicketPriority.Media,
                Status          = TicketStatus.Abierto,
                Origin          = TicketOrigin.Automatico,
                Description     = $"Recolección de equipo ONU (S/N: {client.OnuSerialNumber}) " +
                                   $"del cliente dado de baja: {client.TbnCode} — {client.FullName}",
                CreatedByUserId = cmd.ActorId,
                CreatedAt       = DateTime.UtcNow,
                DueDate         = DateTime.UtcNow.AddHours(48)
            };
            await _ticketRepo.AddAsync(ticket);
            await _audit.LogAsync("Tickets", "TICKET_AUTO_CREATED",
                $"Ticket de recolección creado para {client.TbnCode} (ONU: {client.OnuSerialNumber})",
                userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp);

            return Result<string>.Success("cancelled_with_ticket");
        }

        return Result<string>.Success("cancelled");
    }
}
