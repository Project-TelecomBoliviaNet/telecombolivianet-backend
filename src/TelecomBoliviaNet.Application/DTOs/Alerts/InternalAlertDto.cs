namespace TelecomBoliviaNet.Application.DTOs.Alerts;

public record InternalAlertDto(
    Guid     Id,
    string   Type,
    string   Title,
    string   Message,
    string   Link,
    DateTime CreatedAt
);
