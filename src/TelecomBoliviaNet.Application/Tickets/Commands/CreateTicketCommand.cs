using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Tickets;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Tickets.Commands;

public record CreateTicketCommand(
    CreateTicketDto Dto,
    Guid            ActorId,
    string          ActorName,
    string          ClientIp,
    string?         UserAgent = null) : IRequest<Result<TicketDetailDto>>;

public class CreateTicketHandler : IRequestHandler<CreateTicketCommand, Result<TicketDetailDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly IGenericRepository<Client>        _clientRepo;
    private readonly IGenericRepository<UserSystem>    _userRepo;
    private readonly TicketHelper                      _helper;
    private readonly TicketNumberService               _ticketNumSvc;
    private readonly TicketBalanceoService             _balanceoSvc;
    private readonly AuditService                      _audit;
    private readonly IInternalAlertService             _alertSvc;

    public CreateTicketHandler(
        IGenericRepository<SupportTicket> ticketRepo,
        IGenericRepository<Client>        clientRepo,
        IGenericRepository<UserSystem>    userRepo,
        TicketHelper                      helper,
        TicketNumberService               ticketNumSvc,
        TicketBalanceoService             balanceoSvc,
        AuditService                      audit,
        IInternalAlertService             alertSvc)
    {
        _ticketRepo   = ticketRepo;
        _clientRepo   = clientRepo;
        _userRepo     = userRepo;
        _helper       = helper;
        _ticketNumSvc = ticketNumSvc;
        _balanceoSvc  = balanceoSvc;
        _audit        = audit;
        _alertSvc     = alertSvc;
    }

    public async Task<Result<TicketDetailDto>> Handle(CreateTicketCommand cmd, CancellationToken ct)
    {
        var dto = cmd.Dto;

        Client? client = null;
        if (dto.ClientId.HasValue)
        {
            client = await _clientRepo.GetByIdAsync(dto.ClientId.Value);
            if (client is null) return Result<TicketDetailDto>.Failure("El cliente indicado no existe.");
        }

        if (!Enum.TryParse<TicketType>(dto.Type, out var type))
            return Result<TicketDetailDto>.Failure("Tipo de ticket inválido.");
        if (!Enum.TryParse<TicketPriority>(dto.Priority, out var priority))
            return Result<TicketDetailDto>.Failure("Prioridad inválida.");

        UserSystem? assignedUser = null;
        if (dto.AssignedToUserId.HasValue)
        {
            assignedUser = await _userRepo.GetByIdAsync(dto.AssignedToUserId.Value);
            if (assignedUser is null) return Result<TicketDetailDto>.Failure("El técnico indicado no existe.");
            if (assignedUser.Role != UserRole.Tecnico && assignedUser.Role != UserRole.Admin)
                return Result<TicketDetailDto>.Failure("Solo se puede asignar a Técnico o Admin.");
        }

        DateTime? dueDate = dto.SlaDurationHours.HasValue
            ? DateTime.UtcNow.AddHours(dto.SlaDurationHours.Value)
            : await _helper.GetDueDateFromSlaAsync(priority);

        var ticketNumber = await _ticketNumSvc.NextAsync();

        bool autoAssigned = false;
        if (!dto.AssignedToUserId.HasValue && dto.AutoAssign == true)
        {
            var autoId = await _balanceoSvc.GetTecnicoMenorCargaAsync(dto.SupportGroup);
            if (autoId.HasValue)
            {
                dto.AssignedToUserId = autoId;
                assignedUser = await _userRepo.GetByIdAsync(autoId.Value);
                autoAssigned = true;
            }
        }

        var ticket = new SupportTicket
        {
            ClientId         = dto.ClientId,
            ProspectName     = dto.ClientId.HasValue ? null : dto.ProspectName?.Trim(),
            ProspectPhone    = dto.ClientId.HasValue ? null : dto.ProspectPhone?.Trim(),
            Subject          = dto.Subject.Trim(),
            Type             = type,
            Priority         = priority,
            Status           = TicketStatus.Abierto,
            Origin           = dto.Origin != null && Enum.TryParse<TicketOrigin>(dto.Origin, out var origin)
                               ? origin : TicketOrigin.Manual,
            Description      = dto.Description.Trim(),
            SupportGroup     = dto.SupportGroup?.Trim(),
            AssignedToUserId = dto.AssignedToUserId,
            CreatedByUserId  = cmd.ActorId,
            CreatedAt        = DateTime.UtcNow,
            DueDate          = dueDate,
            TicketNumber     = ticketNumber,
            SlaDeadline      = dueDate,
            AutoAssigned     = autoAssigned,
            ImageUrl         = dto.ImageUrl,
        };

        await _ticketRepo.AddAsync(ticket);

        var auditTarget = client is not null
            ? $"{client.TbnCode} - {client.FullName}"
            : $"Prospecto: {ticket.ProspectName}";
        await _audit.LogAsync("Tickets", "TICKET_CREATED",
            $"Ticket '{ticket.Subject}' creado para {auditTarget}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp, cmd.UserAgent, entityId: ticket.Id);

        string? waWarning = null;
        if (assignedUser is not null)
            waWarning = await _helper.SendNotificationAsync(ticket, assignedUser, client);

        var clientLabel = client is not null ? $" — {client.TbnCode}" : "";
        await _alertSvc.CreateForRoleAsync(
            role:    "Admin",
            type:    "TICKET_NUEVO",
            title:   $"Nuevo ticket #{ticket.TicketNumber}",
            message: $"{ticket.Subject}{clientLabel}",
            link:    $"/tickets/{ticket.Id}");

        var detail = await _helper.BuildDetailAsync(ticket.Id);
        if (detail is null) return Result<TicketDetailDto>.Failure("Ticket no encontrado.");
        return Result<TicketDetailDto>.Success(detail with { WhatsAppWarning = waWarning });
    }
}
