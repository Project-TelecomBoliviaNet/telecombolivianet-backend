namespace TelecomBoliviaNet.Application.DTOs.Auth;

// ── Login ────────────────────────────────────────────────────────────────────

public record LoginDto(string Email, string Password);

public record LoginResponseDto(
    string Token,
    /// <summary>
    /// Refresh token opaco (256 bits). El cliente debe almacenarlo de forma segura
    /// (httpOnly cookie recomendada, o storage protegido). Enviarlo al endpoint
    /// POST /api/auth/refresh cuando el access token expire para obtener uno nuevo.
    /// </summary>
    string RefreshToken,
    string UserId,
    string FullName,
    string Email,
    string Role,
    bool RequiresPasswordChange,
    string RedirectUrl
);

/// <summary>Request body para POST /api/auth/refresh.</summary>
public record RefreshTokenDto(string RefreshToken);

/// <summary>Respuesta de /api/auth/refresh con nuevos tokens rotados.</summary>
public record RefreshResponseDto(string Token, string RefreshToken);

// ── Gestión de usuarios ───────────────────────────────────────────────────────

public record CreateUserDto(
    string FullName,
    string Email,
    string TemporaryPassword,
    string Role
);

public record UpdateUserDto(
    string FullName,
    string Email,
    string Role,
    /// <summary>Número WhatsApp (con código país: 59170000000). Requerido para técnicos que reciben notificaciones de tickets.</summary>
    string? Phone
);

public record ChangePasswordDto(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword
);

public record UserSystemDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    string Status,
    bool RequiresPasswordChange,
    int FailedLoginAttempts,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    /// <summary>Número WhatsApp del técnico/admin para notificaciones de tickets.</summary>
    string? Phone
);

// ── Audit log ─────────────────────────────────────────────────────────────────

public record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string UserName,
    string Module,
    string Action,
    string Description,
    string? IpAddress,
    string? PreviousData,
    string? NewData,
    DateTime CreatedAt
);

public record AuditLogFilterDto(
    Guid? UserId,
    string? Action,
    DateTime? From,
    DateTime? To,
    int PageNumber = 1,
    int PageSize = 50
);


// ════════════════════════════════════════════════════════════════════════════
// M7 — US-ROL-PERMISOS · Matriz de permisos por rol
// ════════════════════════════════════════════════════════════════════════════

public record RolePermissionsDto(
    string   Role,
    string   Label,
    string   Descripcion,
    string[] Modulos,    // lista de módulos a los que tiene acceso
    string[] Politicas   // lista de políticas de autorización
);

// ════════════════════════════════════════════════════════════════════════════
// M7 — US-USR-RECOVERY · Recuperación de contraseña
// ════════════════════════════════════════════════════════════════════════════

public record ForgotPasswordDto(string Email);

public record ResetPasswordDto(
    string Token,
    string NewPassword,
    string ConfirmPassword
);

public record ForgotPasswordResultDto(
    string Message,
    string Channel,   // "WhatsApp" | "Email"
    string? SentTo    // número/email parcialmente enmascarado
);

// ════════════════════════════════════════════════════════════════════════════
// M7 — US-USR-01 · Gestión completa de usuario
// ════════════════════════════════════════════════════════════════════════════

public record UserSystemDetailDto(
    Guid      Id,
    string    FullName,
    string    Email,
    string    Role,
    string    RoleLabel,
    string    Status,
    bool      RequiresPasswordChange,
    int       FailedLoginAttempts,
    DateTime? LastLoginAt,
    DateTime  CreatedAt,
    string?   Phone,
    bool      IsDeleted,
    DateTime? DeletedAt
);

public record DeleteUserDto(string Justificacion);

public record ForceResetPasswordDto(string NewTemporaryPassword);
