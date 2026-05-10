using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Infrastructure.Services;

/// <summary>
/// Implementación de IFileStorage usando Supabase Storage REST API.
/// Se activa cuando Supabase:Url está configurado en el entorno.
/// En otro caso, el DI usa LocalFileStorage como fallback.
/// </summary>
public class SupabaseFileStorage : IFileStorage
{
    // HttpClient estático: seguro para uso de larga duración, evita socket exhaustion.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly string _baseUrl;   // https://xxx.supabase.co
    private readonly string _key;       // anon o service-role key
    private readonly string _bucket;    // nombre del bucket público

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".pdf", ".webp", ".docx", ".txt" };

    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        { ".jpg",  "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png",  "image/png"  },
        { ".webp", "image/webp" },
        { ".pdf",  "application/pdf" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".txt",  "text/plain" },
    };

    public SupabaseFileStorage(IConfiguration config)
    {
        _baseUrl = (config["Supabase:Url"] ?? "").TrimEnd('/');
        _key     =  config["Supabase:Key"]    ?? throw new InvalidOperationException("Supabase:Key no configurado.");
        _bucket  =  config["Supabase:Bucket"] ?? "telecombolivianet-uploads";
    }

    public async Task<string> SaveAsync(Stream stream, string fileName, string folder = "receipts")
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"Tipo de archivo no permitido: {ext}");

        var safeName = Path.GetFileNameWithoutExtension(fileName)
                          .Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
        var unique      = $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}";
        var storagePath = $"{folder}/{unique}";
        var mimeType    = ContentTypes.GetValueOrDefault(ext, "application/octet-stream");

        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/storage/v1/object/{_bucket}/{storagePath}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        request.Content = content;

        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Supabase Storage upload error {(int)response.StatusCode}: {body}");
        }

        return $"{_baseUrl}/storage/v1/object/public/{_bucket}/{storagePath}";
    }

    public async Task<(byte[] Bytes, string ContentType)> ReadAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new FileNotFoundException("URL de archivo vacía.");

        // Acepta URL completa de Supabase o path relativo legado
        var fetchUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{_baseUrl}/storage/v1/object/public/{_bucket}/{url.TrimStart('/')}";

        using var response = await Http.GetAsync(fetchUrl);
        if (!response.IsSuccessStatusCode)
            throw new FileNotFoundException($"Archivo no encontrado en Supabase: {url}");

        var bytes       = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return (bytes, contentType);
    }

    public async Task DeleteAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // Extrae el path relativo desde la URL completa de Supabase
        var marker = $"/object/public/{_bucket}/";
        var idx    = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var storagePath = idx >= 0 ? url[(idx + marker.Length)..] : url.TrimStart('/');

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{_baseUrl}/storage/v1/object/{_bucket}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        request.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { prefixes = new[] { storagePath } }),
            System.Text.Encoding.UTF8,
            "application/json");

        await Http.SendAsync(request);
    }
}
