using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>CORRECCIÓN Problema #7: interfaz para ClientAttachmentService.</summary>
public interface IClientAttachmentService
{
    Task<List<ClientAttachmentDto>> GetByClientAsync(Guid clientId);
    Task<Result<ClientAttachmentDto>> UploadAsync(
        Guid clientId, string fileName, string contentType,
        long sizeBytes, Stream stream, string tipoDoc, string? descripcion,
        Guid actorId, string actorName, string ip);
    Task<Result> DeleteAsync(Guid attachmentId, Guid actorId, string actorName, string ip);
    Task<Result<(Stream Stream, string ContentType, string FileName)>> DownloadAsync(Guid attachmentId);
}
