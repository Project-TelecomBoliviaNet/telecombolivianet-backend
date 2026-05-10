using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Installations.Commands;

public record CrearInstalacionCommand(
    CrearInstalacionDto Dto,
    Guid   ActorId,
    string ActorName,
    string ClientIp) : IRequest<Result<InstalacionCreadaDto>>;

public class CrearInstalacionHandler
    : IRequestHandler<CrearInstalacionCommand, Result<InstalacionCreadaDto>>
{
    private readonly IGenericRepository<Installation>  _instRepo;
    private readonly IGenericRepository<Client>        _clientRepo;
    private readonly IGenericRepository<Plan>          _planRepo;
    private readonly IGenericRepository<UserSystem>    _userRepo;
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly AuditService                      _audit;
    private const int DuracionEstandarMin = 120;

    public CrearInstalacionHandler(
        IGenericRepository<Installation>  instRepo,
        IGenericRepository<Client>        clientRepo,
        IGenericRepository<Plan>          planRepo,
        IGenericRepository<UserSystem>    userRepo,
        IGenericRepository<SupportTicket> ticketRepo,
        AuditService                      audit)
    {
        _instRepo   = instRepo;
        _clientRepo = clientRepo;
        _planRepo   = planRepo;
        _userRepo   = userRepo;
        _ticketRepo = ticketRepo;
        _audit      = audit;
    }

    public async Task<Result<InstalacionCreadaDto>> Handle(
        CrearInstalacionCommand cmd, CancellationToken ct)
    {
        var dto = cmd.Dto;

        Client? client = null;
        if (dto.ClienteId.HasValue)
        {
            client = await _clientRepo.GetAll()
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == dto.ClienteId.Value, ct);

            if (client is null)
                return Result<InstalacionCreadaDto>.Failure("El cliente indicado no existe.");

            var tienePendiente = await _instRepo.AnyAsync(i =>
                i.ClientId == client.Id &&
                (i.Status == InstallationStatus.Pendiente ||
                 i.Status == InstallationStatus.EnProceso));

            if (tienePendiente)
                return Result<InstalacionCreadaDto>.Failure(
                    "El cliente ya tiene una instalación pendiente o en proceso.");
        }

        var plan = await _planRepo.GetByIdAsync(dto.PlanId);
        if (plan is null || !plan.IsActive)
            return Result<InstalacionCreadaDto>.Failure(
                "El plan seleccionado no existe o está inactivo.");

        if (!DateTime.TryParse(dto.Fecha, out var fechaParsed))
            return Result<InstalacionCreadaDto>.Failure(
                "Formato de fecha inválido. Use YYYY-MM-DD.");

        var fecha = DateTime.SpecifyKind(fechaParsed, DateTimeKind.Utc);

        if (!TimeOnly.TryParse(dto.HoraInicio, out var horaInicio))
            return Result<InstalacionCreadaDto>.Failure(
                "Formato de hora inválido. Use HH:mm.");

        var ocupados = await _instRepo.GetAll()
            .CountAsync(i =>
                i.Fecha.Date == fecha.Date &&
                i.HoraInicio == horaInicio &&
                (i.Status == InstallationStatus.Pendiente ||
                 i.Status == InstallationStatus.EnProceso), ct);

        if (ocupados >= await CountTecnicosAsync(ct))
            return Result<InstalacionCreadaDto>.Failure(
                "El horario seleccionado ya no tiene disponibilidad. Por favor elige otro.");

        var instalacion = new Installation
        {
            ClientId    = dto.ClienteId ?? Guid.Empty,
            PlanId      = dto.PlanId,
            Fecha       = DateTime.SpecifyKind(fecha.Date, DateTimeKind.Utc),
            HoraInicio  = horaInicio,
            DuracionMin = DuracionEstandarMin,
            Direccion   = dto.Direccion.Trim(),
            Notas       = dto.Notas?.Trim(),
            Status      = InstallationStatus.Pendiente,
            CreadoPorId = cmd.ActorId,
            CreadoAt    = DateTime.UtcNow,
        };

        await _instRepo.AddAsync(instalacion);

        var nombreCliente = client?.FullName ?? "Cliente nuevo";
        var tbnCode       = client?.TbnCode  ?? "NUEVO";
        var ticket        = new SupportTicket { Id = Guid.Empty };

        if (dto.ClienteId.HasValue)
        {
            ticket = new SupportTicket
            {
                ClientId        = dto.ClienteId.Value,
                Subject         = $"[Instalación Nueva] {tbnCode} – {nombreCliente} – {dto.Fecha} {dto.HoraInicio}",
                Type            = TicketType.InstalacionNueva,
                Priority        = TicketPriority.Media,
                Status          = TicketStatus.Abierto,
                Origin          = TicketOrigin.Bot,
                Description     = $"Instalación agendada para el {dto.Fecha} a las {dto.HoraInicio}.\n" +
                                  $"Plan: {plan.Name}\nDirección: {dto.Direccion}" +
                                  (dto.Notas is not null ? $"\nNotas: {dto.Notas}" : ""),
                CreatedByUserId = cmd.ActorId,
                CreatedAt       = DateTime.UtcNow,
                DueDate         = fecha.Date.AddHours(horaInicio.Hour + DuracionEstandarMin / 60.0),
            };

            await _ticketRepo.AddAsync(ticket);
            instalacion.TicketId      = ticket.Id;
            instalacion.ActualizadoAt = DateTime.UtcNow;
        }

        await _instRepo.SaveChangesAsync();

        await _audit.LogAsync("Instalaciones", "INSTALACION_CREADA",
            $"Instalación agendada: {tbnCode} – {dto.Fecha} {dto.HoraInicio} – {plan.Name}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp);

        return Result<InstalacionCreadaDto>.Success(new InstalacionCreadaDto(
            InstalacionId: instalacion.Id.ToString(),
            TicketId:      ticket.Id.ToString(),
            Fecha:         dto.Fecha,
            HoraInicio:    dto.HoraInicio,
            Status:        instalacion.Status.ToString()
        ));
    }

    private async Task<int> CountTecnicosAsync(CancellationToken ct)
    {
        var count = await _userRepo.GetAll()
            .CountAsync(u => u.Role == UserRole.Tecnico && u.Status == UserStatus.Activo, ct);
        return Math.Max(count, 1);
    }
}
