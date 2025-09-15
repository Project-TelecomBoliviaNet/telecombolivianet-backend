using MediatR;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Tickets.Commands;

public record AddCommentCommand(
    Guid          TicketId,
    AddCommentDto Dto,
    Guid          ActorId,
    string        ActorName,
    string        ClientIp,
    string?       UserAgent = null) : IRequest<Result<TicketCommentDto>>;

public class AddCommentHandler : IRequestHandler<AddCommentCommand, Result<TicketCommentDto>>
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly IGenericRepository<TicketComment> _commentRepo;
    private readonly IGenericRepository<UserSystem>    _userRepo;
    private readonly AuditService                      _audit;

    public AddCommentHandler(
        IGenericRepository<SupportTicket> ticketRepo,
        IGenericRepository<TicketComment> commentRepo,
        IGenericRepository<UserSystem>    userRepo,
        AuditService                      audit)
    {
        _ticketRepo  = ticketRepo;
        _commentRepo = commentRepo;
        _userRepo    = userRepo;
        _audit       = audit;
    }

    public async Task<Result<TicketCommentDto>> Handle(AddCommentCommand cmd, CancellationToken ct)
    {
        var ticket = await _ticketRepo.GetByIdAsync(cmd.TicketId);
        if (ticket is null) return Result<TicketCommentDto>.Failure("Ticket no encontrado.");
        if (ticket.Status == TicketStatus.Cerrado)
            return Result<TicketCommentDto>.Failure("No se puede comentar en un ticket cerrado.");
        if (!Enum.TryParse<CommentType>(cmd.Dto.Type, out var commentType))
            return Result<TicketCommentDto>.Failure("Tipo de comentario inválido.");

        var author  = await _userRepo.GetByIdAsync(cmd.ActorId);
        var comment = new TicketComment
        {
            TicketId  = cmd.TicketId, AuthorId = cmd.ActorId,
            Type      = commentType, Body = cmd.Dto.Body.Trim(), CreatedAt = DateTime.UtcNow,
        };
        await _commentRepo.AddAsync(comment);
        await _commentRepo.SaveChangesAsync();

        await _audit.LogAsync("Tickets", "COMMENT_ADDED",
            $"Comentario ({commentType}) en ticket {cmd.TicketId}",
            cmd.ActorId, cmd.ActorName, cmd.ClientIp, cmd.UserAgent);

        return Result<TicketCommentDto>.Success(new TicketCommentDto(
            comment.Id, comment.Type.ToString(), comment.Body,
            author?.FullName ?? cmd.ActorName, cmd.ActorId, comment.CreatedAt));
    }
}
