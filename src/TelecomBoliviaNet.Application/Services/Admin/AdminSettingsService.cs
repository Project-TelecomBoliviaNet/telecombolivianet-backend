using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Admin;

/// <summary>
/// Servicio para leer y actualizar configuracion de sistema en runtime.
///
/// Persiste en tabla SystemConfig de la BD (compatible con multi-réplica y
/// contenedores con filesystem efímero).
///
/// BUG F FIX: Los secrets (WhatsApp token, PhoneNumberId) ahora se cifran con
/// AES-256-GCM antes de almacenarse en BD usando SecretEncryptionService.
/// La clave de cifrado viene exclusivamente de la variable de entorno
/// SecretEncryption__Key — nunca de la BD ni de appsettings.
/// Al leer, los valores se descifran automáticamente antes de retornarlos.
/// En el audit log y en la respuesta API, el token se enmascara como "***".
/// </summary>
public class AdminSettingsService
{
    private readonly IGenericRepository<SystemConfig> _configRepo;
    private readonly AuditService                     _audit;
    private readonly SecretEncryptionService          _encryption;

    // Conjunto de claves que contienen secrets y deben cifrarse en BD
    private static readonly HashSet<string> SecretKeys = new()
    {
        SystemConfigKeys.WhatsAppToken,
        SystemConfigKeys.WhatsAppPhoneNumberId,
    };

    public AdminSettingsService(
        IGenericRepository<SystemConfig> configRepo,
        AuditService                     audit,
        SecretEncryptionService          encryption)
    {
        _configRepo = configRepo;
        _audit      = audit;
        _encryption = encryption;
    }

    // Leer configuracion actual desde BD — descifra secrets antes de retornar
    public async Task<AdminSettingsDto> GetCurrentAsync()
    {
        var configs = await _configRepo.GetAll().ToListAsync();

        // Descifra el valor si es un secret, lo retorna tal cual si no lo es
        string Get(string key, string def)
        {
            var raw = configs.FirstOrDefault(c => c.Key == key)?.Value ?? def;
            return SecretKeys.Contains(key) ? _encryption.Decrypt(raw) : raw;
        }

        return new AdminSettingsDto(
            WhatsAppToken:          Get(SystemConfigKeys.WhatsAppToken,          string.Empty),
            WhatsAppPhoneNumberId:  Get(SystemConfigKeys.WhatsAppPhoneNumberId,  string.Empty),
            WhatsAppApiVersion:     Get(SystemConfigKeys.WhatsAppApiVersion,      "v19.0"),
            SlaHorasAnticipacion:   int.TryParse(Get(SystemConfigKeys.SlaHorasAnticipacion, "4"),  out var h) ? h : 4,
            SlaHoraInicioLaboral:   int.TryParse(Get(SystemConfigKeys.SlaHoraInicioLaboral, "7"),  out var i) ? i : 7,
            SlaHoraFinLaboral:      int.TryParse(Get(SystemConfigKeys.SlaHoraFinLaboral,    "22"), out var f) ? f : 22,
            MaxFailedLoginAttempts: int.TryParse(Get(SystemConfigKeys.MaxFailedLoginAttempts, "5"), out var m) ? m : 5
        );
    }

    // Guardar configuracion en BD — cifra secrets antes de persistir
    public async Task<Result<bool>> SaveAsync(
        AdminSettingsDto dto, Guid actorId, string actorName, string ip)
    {
        if (dto.SlaHorasAnticipacion is < 1 or > 24)
            return Result<bool>.Failure("HorasAnticipacion debe estar entre 1 y 24.");
        if (dto.SlaHoraInicioLaboral is < 0 or > 23)
            return Result<bool>.Failure("HoraInicioLaboral debe estar entre 0 y 23.");
        if (dto.SlaHoraFinLaboral is < 1 or > 23)
            return Result<bool>.Failure("HoraFinLaboral debe estar entre 1 y 23.");
        if (dto.SlaHoraInicioLaboral >= dto.SlaHoraFinLaboral)
            return Result<bool>.Failure("HoraInicioLaboral debe ser menor que HoraFinLaboral.");
        if (dto.MaxFailedLoginAttempts is < 3 or > 10)
            return Result<bool>.Failure("MaxFailedLoginAttempts debe estar entre 3 y 10.");

        // BUG F FIX: cifrar secrets antes de guardar en BD.
        // Valores no-secret se almacenan en texto plano (son config pública).
        var updates = new Dictionary<string, string>
        {
            // BUG FIX: no cifrar strings vacíos — Encrypt("") genera un blob AES inútil en BD.
            // Si el admin no ingresa el valor, conservar cadena vacía en texto plano.
            [SystemConfigKeys.WhatsAppToken]         = string.IsNullOrEmpty(dto.WhatsAppToken)
                ? string.Empty : _encryption.Encrypt(dto.WhatsAppToken),
            [SystemConfigKeys.WhatsAppPhoneNumberId] = string.IsNullOrEmpty(dto.WhatsAppPhoneNumberId)
                ? string.Empty : _encryption.Encrypt(dto.WhatsAppPhoneNumberId),
            [SystemConfigKeys.WhatsAppApiVersion]     = dto.WhatsAppApiVersion,
            [SystemConfigKeys.SlaHorasAnticipacion]   = dto.SlaHorasAnticipacion.ToString(),
            [SystemConfigKeys.SlaHoraInicioLaboral]   = dto.SlaHoraInicioLaboral.ToString(),
            [SystemConfigKeys.SlaHoraFinLaboral]      = dto.SlaHoraFinLaboral.ToString(),
            [SystemConfigKeys.MaxFailedLoginAttempts] = dto.MaxFailedLoginAttempts.ToString(),
        };

        var existing = await _configRepo.GetAll().ToListAsync();

        foreach (var (key, value) in updates)
        {
            var record = existing.FirstOrDefault(c => c.Key == key);
            if (record is not null)
            {
                record.Value       = value;
                record.UpdatedAt   = DateTime.UtcNow;
                record.UpdatedById = actorId;
                await _configRepo.UpdateAsync(record);
            }
            else
            {
                await _configRepo.AddAsync(new SystemConfig
                {
                    Key         = key,
                    Value       = value,
                    IsSecret    = SecretKeys.Contains(key),
                    UpdatedAt   = DateTime.UtcNow,
                    UpdatedById = actorId,
                });
                await _configRepo.SaveChangesAsync();
            }
        }

        // Audit log: nunca registrar el valor del token — enmascarar como "***"
        await _audit.LogAsync("Sistema", "SETTINGS_UPDATED",
            "Configuracion del sistema actualizada via BD",
            userId: actorId, userName: actorName, ip: ip,
            newData: System.Text.Json.JsonSerializer.Serialize(new
            {
                dto.WhatsAppPhoneNumberId,
                dto.WhatsAppApiVersion,
                dto.SlaHorasAnticipacion,
                dto.SlaHoraInicioLaboral,
                dto.SlaHoraFinLaboral,
                dto.MaxFailedLoginAttempts,
                WhatsAppToken = dto.WhatsAppToken.Length > 0 ? "***" : "(vacio)"
            }));

        return Result<bool>.Success(true);
    }
}

public record AdminSettingsDto(
    string WhatsAppToken,
    string WhatsAppPhoneNumberId,
    string WhatsAppApiVersion,
    int    SlaHorasAnticipacion,
    int    SlaHoraInicioLaboral,
    int    SlaHoraFinLaboral,
    int    MaxFailedLoginAttempts
);
