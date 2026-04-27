using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;
#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Clients;

/// <summary>
/// US-CLI-ADJUNTOS — Gestión de documentos adjuntos por cliente.
/// Tipos: CI, Contrato, Foto, Comprobante, Otro.
/// Límite: 20 adjuntos por cliente. Tamaño máx: 10 MB por archivo.
/// </summary>
public class ClientAttachmentService : IClientAttachmentService
{
    private readonly IGenericRepository<ClientAttachment>   _repo;
    private readonly IGenericRepository<Client>             _clientRepo;
    private readonly IFileStorage                           _storage;
    private readonly AuditService                           _audit;
    private readonly ILogger<ClientAttachmentService>       _logger;

    private const int  MaxAttachments    = 20;
    private const long MaxFileSizeBytes  = 10 * 1024 * 1024; // 10 MB
    private static readonly string[] AllowedContentTypes =
    [
        "image/jpeg", "image/png", "image/webp",
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ];

    public ClientAttachmentService(
        IGenericRepository<ClientAttachment> repo,
        IGenericRepository<Client>           clientRepo,
        IFileStorage                         storage,
        AuditService                         audit,
        ILogger<ClientAttachmentService>     logger)
    {
        _repo       = repo;
        _clientRepo = clientRepo;
        _storage    = storage;
        _audit      = audit;
        _logger     = logger;
    }

    // ── Listar adjuntos ───────────────────────────────────────────────────────

    public async Task<List<ClientAttachmentDto>> GetByClientAsync(Guid clientId)
    {
        var list = await _repo.GetAll()
            .Include(a => a.SubidoPor)
            .Where(a => a.ClientId == clientId && !a.IsDeleted)
            .OrderByDescending(a => a.SubidoAt)
            .ToListAsync();

        return list.Select(a => ToDto(a)).ToList();
    }

    // ── Subir nuevo adjunto ───────────────────────────────────────────────────

    public async Task<Result<ClientAttachmentDto>> UploadAsync(
        Guid clientId, string fileName, string contentType, long sizeBytes,
        Stream fileStream, string tipoDoc, string? descripcion,
        Guid actorId, string actorName, string ip)
    {
        // Validaciones
        if (!TipoDocumento.Todos.Contains(tipoDoc))
            return Result<ClientAttachmentDto>.Failure(
                $"TipoDoc inválido. Usa: {string.Join(", ", TipoDocumento.Todos)}");

        if (!AllowedContentTypes.Contains(contentType))
            return Result<ClientAttachmentDto>.Failure(
                "Tipo de archivo no permitido. Solo JPG, PNG, WebP, PDF y DOCX.");

        if (sizeBytes > MaxFileSizeBytes)
            return Result<ClientAttachmentDto>.Failure("El archivo supera el límite de 10 MB.");

        var count = await _repo.GetAll()
            .CountAsync(a => a.ClientId == clientId && !a.IsDeleted);
        if (count >= MaxAttachments)
            return Result<ClientAttachmentDto>.Failure(
                $"El cliente ya tiene {MaxAttachments} adjuntos. Elimina alguno antes de subir uno nuevo.");

        var client = await _clientRepo.GetByIdAsync(clientId);
        if (client is null) return Result<ClientAttachmentDto>.Failure("Cliente no encontrado.");

        // Guardar en storage — BUG FIX: eliminar primera asignación muerta y variable ext no usada
        var folder      = $"clients/{clientId}/attachments";
        var storagePath = await _storage.SaveAsync(fileStream, fileName, folder);

        var attachment = new ClientAttachment
        {
            ClientId      = clientId,
            FileName      = fileName,
            StoragePath   = storagePath,
            ContentType   = contentType,
            FileSizeBytes = sizeBytes,
            TipoDoc       = tipoDoc,
            Descripcion   = descripcion,
            SubidoPorId   = actorId,
            SubidoAt      = DateTime.UtcNow,
        };
        await _repo.AddAsync(attachment);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync("Clientes", "CLIENT_ATTACHMENT_UPLOADED",
            $"Adjunto subido: {client.TbnCode} archivo={fileName} tipo={tipoDoc}",
            userId: actorId, userName: actorName, ip: ip);

        return Result<ClientAttachmentDto>.Success(ToDto(attachment, actorName));
    }

    // ── Eliminar adjunto (soft delete) ─────────────────────────────────────────

    public async Task<r> DeleteAsync(
        Guid attachmentId, Guid actorId, string actorName, string ip)
    {
        var att = await _repo.GetByIdAsync(attachmentId);
        if (att is null || att.IsDeleted)
            return Result.Failure("Adjunto no encontrado.");

        att.IsDeleted   = true;
        att.DeletedAt   = DateTime.UtcNow;
        att.DeletedById = actorId;
        await _repo.UpdateAsync(att);

        // Opcional: eliminar del storage
        try { await _storage.DeleteAsync(att.StoragePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Storage delete falló para {Path} — el registro ya está soft-deleted", att.StoragePath); }

        await _audit.LogAsync("Clientes", "CLIENT_ATTACHMENT_DELETED",
            $"Adjunto eliminado: {att.FileName} (clientId={att.ClientId})",
            userId: actorId, userName: actorName, ip: ip);

        return Result.Success();
    }

    // ── Descargar (devuelve stream + contentType) ─────────────────────────────

    public async Task<Result<(Stream Stream, string ContentType, string FileName)>> DownloadAsync(
        Guid attachmentId)
    {
        var att = await _repo.GetByIdAsync(attachmentId);
        if (att is null || att.IsDeleted)
            return Result<(Stream, string, string)>.Failure("Adjunto no encontrado.");

        var (bytes, contentType) = await _storage.ReadAsync(att.StoragePath);
        var stream = new System.IO.MemoryStream(bytes);
        return Result<(Stream, string, string)>.Success((stream, contentType, att.FileName));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClientAttachmentDto ToDto(ClientAttachment a, string? overrideName = null) =>
        new(a.Id, a.FileName, a.TipoDoc, a.ContentType, a.FileSizeBytes,
            a.Descripcion, a.StoragePath,
            overrideName ?? a.SubidoPor?.FullName ?? "—",
            a.SubidoAt);
}
