using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Installations.Commands;

public record CancelarInstalacionCommand(
    Guid                   InstalacionId,
    CancelarInstalacionDto Dto,
    Guid                   ActorId,
    string                 ActorName,
    string                 ClientIp) : IRequest<Result>;

public class CancelarInstalacionHandler : IRequestHandler<CancelarInstalacionCommand, Result>
{
    private readonly IGenericRepository<Installation>  _instRepo;
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly AuditService                      _audit;

    public CancelarInstalacionHandler(
        IGenericRepository<Installation>  instRepo,
        IGenericRepository<SupportTicket> ticketRepo,
        AuditService                      audit)
    {
        _instRepo   = instRepo;
        _ticketRepo = ticketRepo;
        _audit      = audit;
    }

    public async Task<Result> Handle(CancelarInstalacionCommand cmd, CancellationToken ct)
    {
        var inst = await _instRepo.GetAll()
            .Include(i => i.Client)
            .Include(i => i.Ticket)
            .FirstOrDefaultAsync(i => i.Id == cmd.InstalacionId, ct);

        if (inst is null)
            return Result.Failure("Instalación no encontrada.");

        if (inst.Status is InstallationStatus.Completada or InstallationStatus.Cancelada)
            return Result.Failure($"La instalación ya está {inst.Status}. No se puede cancelar.");

        inst.Status            = InstallationStatus.Cancelada;
        inst.MotivoCancelacion = cmd.Dto.MotivoCancelacion.Trim();
        inst.CanceladoPor      = cmd.Dto.CanceladoPor;
        inst.ActualizadoAt     = DateTime.UtcNow;
        await _instRepo.UpdateAsync(inst);

        if (inst.Ticket is not null &&
            inst.Ticket.Status is TicketStatus.Abierto or TicketStatus.EnProceso)
        {
            inst.Ticket.Status            = TicketStatus.Cerrado;
            inst.Ticket.ResolutionMessage = $"Instalación cancelada. Motivo: {cmd.Dto.MotivoCancelacion}";
            inst.Ticket.ClosedAt          = DateTime.UtcNow;
            await _ticketRepo.UpdateAsync(inst.Ticket);
        }

        await _audit.LogAsync("Instalaciones", "INSTALACION_CANCELADA",
            $"Instalación cancelada: {cmd.InstalacionId} — {cmd.Dto.MotivoCancelacion} — por {cmd.Dto.CanceladoPor}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp);

        return Result.Success();
    }
}
