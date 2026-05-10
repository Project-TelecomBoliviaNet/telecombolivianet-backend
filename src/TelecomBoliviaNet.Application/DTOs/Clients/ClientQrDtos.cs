namespace TelecomBoliviaNet.Application.DTOs.Clients;

public record ClientQrDto(
    Guid      Id,
    string    ImageUrl,
    DateTime? ExpiresAt,
    bool      IsActive,
    bool      AlertSent,
    DateTime  UploadedAt,
    string    UploadedByName,
    int?      DaysUntilExpiry   // null si no expira; negativo si ya expiró
);

public class UpdateClientQrDto
{
    /// <summary>Días desde hoy hasta que expire el QR. Null = sin expiración.</summary>
    public int? ExpiresInDays { get; set; }
}
