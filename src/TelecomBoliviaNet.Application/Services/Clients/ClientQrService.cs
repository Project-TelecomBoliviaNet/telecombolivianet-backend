using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Clients;

/// <summary>
/// Gestiona los QR de pago de clientes.
/// Subida, consulta, desactivación y alertas de vencimiento.
/// </summary>
public class ClientQrService
{
    private readonly IGenericRepository<ClientQr>   _qrRepo;
    private readonly IGenericRepository<Client>      _clientRepo;
    private readonly IGenericRepository<UserSystem>  _userRepo;
    private readonly IFileStorage                    _fileStorage;
    private readonly AuditService                    _audit;
    private readonly ILogger<ClientQrService>        _logger;

    // Días de antelación para enviar alerta de vencimiento
    private const int AlertDaysBefore = 5;

    public ClientQrService(
        IGenericRepository<ClientQr>  qrRepo,
        IGenericRepository<Client>    clientRepo,
        IGenericRepository<UserSystem> userRepo,
        IFileStorage                  fileStorage,
        AuditService                  audit,
        ILogger<ClientQrService>      logger)
    {
        _qrRepo      = qrRepo;
        _clientRepo  = clientRepo;
        _userRepo    = userRepo;
        _fileStorage = fileStorage;
        _audit       = audit;
        _logger      = logger;
    }

    // ── Subir QR (POST /api/clients/{id}/qr) ─────────────────────────────────

    public async Task<Result<ClientQrDto>> UploadQrAsync(
        Guid clientId,
        IFormFile file,
        UpdateClientQrDto meta,
        Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetByIdAsync(clientId);
        if (client is null)
            return Result<ClientQrDto>.Failure("Cliente no encontrado.");

        // Validar archivo
        if (file is null || file.Length == 0)
            return Result<ClientQrDto>.Failure("Debes adjuntar una imagen.");
        if (file.Length > 3 * 1024 * 1024)
            return Result<ClientQrDto>.Failure("La imagen no puede superar 3 MB.");

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(file.ContentType.ToLower()))
            return Result<ClientQrDto>.Failure("Formato no permitido. Use JPG, PNG o WebP.");

        // Desactivar QR anterior si existe
        var anterior = await _qrRepo.GetAll()
            .Where(q => q.ClientId == clientId && q.IsActive)
            .FirstOrDefaultAsync();

        if (anterior is not null)
        {
            anterior.IsActive           = false;
            anterior.DeactivatedReason  = "Reemplazado por nuevo QR";
            await _qrRepo.UpdateAsync(anterior);
        }

        // Guardar imagen
        using var stream = file.OpenReadStream();
        var fileName = $"qr_{clientId}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
        var imageUrl = await _fileStorage.SaveAsync(stream, fileName, "qr");

        // Calcular expiración
        DateTime? expiresAt = meta.ExpiresInDays.HasValue
            ? DateTime.UtcNow.AddDays(meta.ExpiresInDays.Value)
            : null;

        var qr = new ClientQr
        {
            ClientId     = clientId,
            ImageUrl     = imageUrl,
            ExpiresAt    = expiresAt,
            IsActive     = true,
            AlertSent    = false,
            UploadedAt   = DateTime.UtcNow,
            UploadedById = actorId,
        };
        await _qrRepo.AddAsync(qr);
        await _qrRepo.SaveChangesAsync();

        await _audit.LogAsync("Clientes", "CLIENT_QR_UPLOADED",
            $"QR subido para cliente {client.TbnCode} — {client.FullName}" +
            (expiresAt.HasValue ? $" — expira {expiresAt:dd/MM/yyyy}" : " — sin expiración"),
            actorId, actorName, ip);

        var actor = await _userRepo.GetByIdAsync(actorId);
        return Result<ClientQrDto>.Success(MapDto(qr, actor?.FullName ?? actorName));
    }

    // ── Obtener QR activo del cliente (GET /api/clients/{id}/qr) ─────────────

    public async Task<(ClientQrDto? Dto, byte[]? ImageBytes, string? ContentType)>
        GetActiveQrAsync(Guid clientId)
    {
        var now = DateTime.UtcNow;

        // CQS: query pura — los expirados se excluyen en SQL; la deactivación se delega
        // al job QrExpiryAlertJob o a DeactivateExpiredQrAsync (comando separado).
        var qr = await _qrRepo.GetAll()
            .Include(q => q.Client)
            .Where(q => q.ClientId == clientId
                     && q.IsActive
                     && (q.ExpiresAt == null || q.ExpiresAt.Value > now))
            .OrderByDescending(q => q.UploadedAt)
            .FirstOrDefaultAsync();

        if (qr is null) return (null, null, null);

        try
        {
            var (bytes, contentType) = await _fileStorage.ReadAsync(qr.ImageUrl);
            var actor = await _userRepo.GetByIdAsync(qr.UploadedById);
            return (MapDto(qr, actor?.FullName ?? "Sistema"), bytes, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo imagen QR del cliente {ClientId}", clientId);
            return (null, null, null);
        }
    }

    // ── Comando: desactivar QRs expirados (llamado por job periódico) ────────

    public async Task DeactivateExpiredQrsAsync()
    {
        var now     = DateTime.UtcNow;
        var expired = await _qrRepo.GetAll()
            .Where(q => q.IsActive && q.ExpiresAt.HasValue && q.ExpiresAt.Value <= now)
            .ToListAsync();

        foreach (var qr in expired)
        {
            qr.IsActive          = false;
            qr.DeactivatedReason = "Expirado automáticamente";
            await _qrRepo.UpdateAsync(qr);
        }

        if (expired.Count > 0)
            _logger.LogInformation("QrService: {Count} QRs desactivados por expiración", expired.Count);
    }

    // ── Historial de QRs del cliente ─────────────────────────────────────────

    public async Task<List<ClientQrDto>> GetHistorialAsync(Guid clientId)
    {
        var qrs = await _qrRepo.GetAll()
            .Where(q => q.ClientId == clientId)
            .OrderByDescending(q => q.UploadedAt)
            .ToListAsync();

        var actorIds = qrs.Select(q => q.UploadedById).Distinct().ToList();
        var actors   = await _userRepo.GetAll()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return qrs.Select(q => MapDto(q, actors.GetValueOrDefault(q.UploadedById, "Sistema"))).ToList();
    }

    // ── Job: detectar QRs próximos a vencer (llamado por QrExpiryAlertJob) ───

    public async Task<List<(Guid ClientId, string ClientName, string TbnCode, DateTime ExpiresAt)>>
        GetQrsExpiringSoonAsync()
    {
        var alertDate = DateTime.UtcNow.AddDays(AlertDaysBefore);

        var qrs = await _qrRepo.GetAll()
            .Include(q => q.Client)
            .Where(q =>
                q.IsActive &&
                !q.AlertSent &&
                q.ExpiresAt.HasValue &&
                q.ExpiresAt.Value <= alertDate &&
                q.ExpiresAt.Value > DateTime.UtcNow)
            .ToListAsync();

        return qrs.Select(q => (
            q.ClientId,
            q.Client?.FullName ?? "—",
            q.Client?.TbnCode  ?? "—",
            q.ExpiresAt!.Value
        )).ToList();
    }

    /// <summary>Marca el QR como alertado para no duplicar notificaciones.</summary>
    public async Task MarkAlertSentAsync(Guid clientId)
    {
        var qr = await _qrRepo.GetAll()
            .Where(q => q.ClientId == clientId && q.IsActive)
            .FirstOrDefaultAsync();

        if (qr is not null)
        {
            qr.AlertSent = true;
            await _qrRepo.UpdateAsync(qr);
        }
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    private static ClientQrDto MapDto(ClientQr q, string uploaderName)
    {
        int? daysUntilExpiry = q.ExpiresAt.HasValue
            ? (int)Math.Round((q.ExpiresAt.Value - DateTime.UtcNow).TotalDays)
            : null;

        return new ClientQrDto(
            q.Id, q.ImageUrl, q.ExpiresAt, q.IsActive,
            q.AlertSent, q.UploadedAt, uploaderName, daysUntilExpiry);
    }
}
