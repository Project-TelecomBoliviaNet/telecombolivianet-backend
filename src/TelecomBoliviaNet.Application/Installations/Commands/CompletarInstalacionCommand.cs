using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Installations.Commands;

public record CompletarInstalacionCommand(
    Guid                    InstalacionId,
    CompletarInstalacionDto Dto,
    Guid                    ActorId,
    string                  ActorName,
    string                  ClientIp) : IRequest<Result>;

public class CompletarInstalacionHandler : IRequestHandler<CompletarInstalacionCommand, Result>
{
    private readonly IGenericRepository<Installation>  _instRepo;
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly AuditService                      _audit;

    public CompletarInstalacionHandler(
        IGenericRepository<Installation>  instRepo,
        IGenericRepository<SupportTicket> ticketRepo,
        AuditService                      audit)
    {
        _instRepo   = instRepo;
        _ticketRepo = ticketRepo;
        _audit      = audit;
    }

    public async Task<Result> Handle(CompletarInstalacionCommand cmd, CancellationToken ct)
    {
        var inst = await _instRepo.GetAll()
            .Include(i => i.Ticket)
            .FirstOrDefaultAsync(i => i.Id == cmd.InstalacionId, ct);

        if (inst is null)
            return Result.Failure("Instalación no encontrada.");
        if (inst.Status == InstallationStatus.Completada)
            return Result.Failure("La instalación ya está completada.");
        if (inst.Status == InstallationStatus.Cancelada)
            return Result.Failure("No se puede completar una instalación cancelada.");

        inst.Status        = InstallationStatus.Completada;
        inst.ActualizadoAt = DateTime.UtcNow;
        await _instRepo.UpdateAsync(inst);

        if (inst.Ticket is not null &&
            inst.Ticket.Status is TicketStatus.Abierto or TicketStatus.EnProceso)
        {
            inst.Ticket.Status            = TicketStatus.Resuelto;
            inst.Ticket.ResolutionMessage = cmd.Dto.NotasTecnico ?? "Instalación completada exitosamente.";
            inst.Ticket.ResolvedAt        = DateTime.UtcNow;
            inst.Ticket.SlaCompliant      = inst.Ticket.DueDate.HasValue &&
                                            DateTime.UtcNow <= inst.Ticket.DueDate.Value;
            await _ticketRepo.UpdateAsync(inst.Ticket);
        }

        await _audit.LogAsync("Instalaciones", "INSTALACION_COMPLETADA",
            $"Instalación {cmd.InstalacionId} completada.",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp);

        return Result.Success();
    }
}
