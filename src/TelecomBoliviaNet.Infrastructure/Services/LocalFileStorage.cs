using Microsoft.AspNetCore.Hosting;
using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Infrastructure.Services;

public class LocalFileStorage : IFileStorage
{
    private readonly string _uploadsRoot;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".pdf", ".webp" };

    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        { ".jpg",  "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png",  "image/png"  },
        { ".webp", "image/webp" },
        { ".pdf",  "application/pdf" },
    };

    public LocalFileStorage(IWebHostEnvironment env)
    {
        _uploadsRoot = Path.Combine(env.WebRootPath ?? "wwwroot", "uploads");
        Directory.CreateDirectory(_uploadsRoot);
    }

    public async Task<string> SaveAsync(Stream stream, string fileName, string folder = "receipts")
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"Tipo no permitido: {ext}");

        var folderPath = Path.Combine(_uploadsRoot, folder);
        Directory.CreateDirectory(folderPath);

        var safeName = Path.GetFileNameWithoutExtension(fileName)
                           .Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
        var unique   = $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}";
        var fullPath = Path.Combine(folderPath, unique);

        using var fs = File.Create(fullPath);
        await stream.CopyToAsync(fs);
        return $"/uploads/{folder}/{unique}";
    }

    public async Task<(byte[] Bytes, string ContentType)> ReadAsync(string relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl))
            throw new FileNotFoundException("URL vacía.");

        var relativePath = relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var rawPath  = Path.Combine(Directory.GetParent(_uploadsRoot)!.FullName, relativePath);
        var fullPath = Path.GetFullPath(rawPath);

        // BUG FIX: Prevenir path traversal — el path resuelto debe estar dentro de uploadsRoot
        var allowedRoot = Path.GetFullPath(_uploadsRoot);
        if (!fullPath.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !fullPath.Equals(allowedRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path fuera del directorio permitido.");

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"No encontrado: {relativeUrl}");

        var ext         = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = ContentTypes.GetValueOrDefault(ext, "application/octet-stream");
        var bytes       = await File.ReadAllBytesAsync(fullPath);
        return (bytes, contentType);
    }

    public Task DeleteAsync(string relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return Task.CompletedTask;
        var relativePath = relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(Directory.GetParent(_uploadsRoot)!.FullName, relativePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }
}
