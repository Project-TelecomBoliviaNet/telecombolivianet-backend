using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;
#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Tickets;

/// <summary>US-TKT-ADJ — Gestión de adjuntos de tickets.</summary>
public class TicketAttachmentService
{
    private readonly IGenericRepository<TicketAttachment>   _repo;
    private readonly IGenericRepository<SupportTicket>      _ticketRepo;
    private readonly IFileStorage                           _storage;
    private readonly AuditService                           _audit;
    private readonly ILogger<TicketAttachmentService>       _logger;

    private const int  MaxAttachments   = 10;
    private const long MaxFileSizeBytes = 15 * 1024 * 1024; // 15 MB
    private static readonly string[] AllowedTypes =
        ["image/jpeg","image/png","image/webp","application/pdf","text/plain"];

    public TicketAttachmentService(
        IGenericRepository<TicketAttachment> repo,
        IGenericRepository<SupportTicket>    ticketRepo,
        IFileStorage                         storage,
        AuditService                         audit,
        ILogger<TicketAttachmentService>     logger)
    {
        _repo       = repo;
        _ticketRepo = ticketRepo;
        _storage    = storage;
        _audit      = audit;
        _logger     = logger;
    }

    public async Task<List<TicketAttachmentDto>> GetByTicketAsync(Guid ticketId)
    {
        var list = await _repo.GetAll()
            .Include(a => a.SubidoPor)
            .Where(a => a.TicketId == ticketId && !a.IsDeleted)
            .OrderByDescending(a => a.SubidoAt)
            .ToListAsync();
        return list.Select(a => ToDto(a)).ToList();
    }

    public async Task<Result<TicketAttachmentDto>> UploadAsync(
        Guid ticketId, string fileName, string contentType,
        long sizeBytes, Stream stream, string? descripcion,
        Guid actorId, string actorName, string ip)
    {
        if (!AllowedTypes.Contains(contentType))
            return Result<TicketAttachmentDto>.Failure(
                "Tipo de archivo no permitido. Solo JPG, PNG, WebP, PDF y TXT.");

        if (sizeBytes > MaxFileSizeBytes)
            return Result<TicketAttachmentDto>.Failure("El archivo supera el límite de 15 MB.");

        var count = await _repo.GetAll()
            .CountAsync(a => a.TicketId == ticketId && !a.IsDeleted);
        if (count >= MaxAttachments)
            return Result<TicketAttachmentDto>.Failure(
                $"El ticket ya tiene el máximo de {MaxAttachments} adjuntos.");

        var ticket = await _ticketRepo.GetByIdAsync(ticketId);
        if (ticket is null) return Result<TicketAttachmentDto>.Failure("Ticket no encontrado.");

        var folder = $"tickets/{ticketId}/attachments";
        var storagePath = await _storage.SaveAsync(stream, fileName, folder);

        var att = new TicketAttachment
        {
            TicketId      = ticketId,
            FileName      = fileName,
            StoragePath   = storagePath,
            ContentType   = contentType,
            FileSizeBytes = sizeBytes,
            Descripcion   = descripcion,
            SubidoPorId   = actorId,
            SubidoAt      = DateTime.UtcNow,
        };
        await _repo.AddAsync(att);

        await _audit.LogAsync("Tickets", "TICKET_ATTACHMENT_UPLOADED",
            $"Adjunto subido: {ticket.TicketNumber ?? ticketId.ToString()[..8]} archivo={fileName}",
            userId: actorId, userName: actorName, ip: ip);

        return Result<TicketAttachmentDto>.Success(ToDto(att, actorName));
    }

    public async Task<r> DeleteAsync(Guid attachId, Guid actorId, string actorName, string ip)
    {
        var att = await _repo.GetByIdAsync(attachId);
        if (att is null || att.IsDeleted) return Result.Failure("Adjunto no encontrado.");
        att.IsDeleted = true;
        att.DeletedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(att);
        try { await _storage.DeleteAsync(att.StoragePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Storage delete falló para {Path} — el registro ya está soft-deleted", att.StoragePath); }
        await _audit.LogAsync("Tickets", "TICKET_ATTACHMENT_DELETED",
            $"Adjunto eliminado: {att.FileName}", userId: actorId, userName: actorName, ip: ip);
        return Result.Success();
    }

    public async Task<Result<(Stream, string, string)>> DownloadAsync(Guid attachId)
    {
        var att = await _repo.GetByIdAsync(attachId);
        if (att is null || att.IsDeleted) return Result<(Stream, string, string)>.Failure("Adjunto no encontrado.");
        var (bytes, contentType) = await _storage.ReadAsync(att.StoragePath);
        var stream = new System.IO.MemoryStream(bytes);
        return Result<(Stream, string, string)>.Success((stream, contentType, att.FileName));
    }

    private static TicketAttachmentDto ToDto(TicketAttachment a, string? overrideName = null) =>
        new(a.Id, a.FileName, a.ContentType, a.FileSizeBytes, a.Descripcion,
            a.StoragePath, overrideName ?? a.SubidoPor?.FullName ?? "—", a.SubidoAt);
}

public record TicketAttachmentDto(
    Guid     Id,
    string   FileName,
    string   ContentType,
    long     FileSizeBytes,
    string?  Descripcion,
    string   StoragePath,
    string   SubidoPorNombre,
    DateTime SubidoAt
);
