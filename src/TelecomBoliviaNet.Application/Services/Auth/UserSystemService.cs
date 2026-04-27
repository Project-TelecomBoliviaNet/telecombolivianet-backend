#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;


namespace TelecomBoliviaNet.Application.Services.Auth;

public class UserSystemService
{
    private readonly IGenericRepository<UserSystem>         _repo;
    private readonly IGenericRepository<PasswordResetToken> _resetRepo;
    private readonly AuditService                           _audit;
    private readonly ILogger<UserSystemService>             _logger;
    private readonly IPasswordHasher                        _hasher;   // CORRECCIÓN Problema #9

    public UserSystemService(
        IGenericRepository<UserSystem>         repo,
        IGenericRepository<PasswordResetToken> resetRepo,
        AuditService                           audit,
        ILogger<UserSystemService>             logger,
        IPasswordHasher                        hasher)
    {
        _repo      = repo;
        _resetRepo = resetRepo;
        _audit     = audit;
        _logger    = logger;
        _hasher    = hasher;
    }

    // ── US-05 · Listar usuarios ───────────────────────────────────────────────

    public async Task<PagedResult<UserSystemDto>> GetAllAsync(
        int page, int pageSize, string? roleFilter = null, string? search = null)
    {
        var query = _repo.GetAll().AsQueryable();

        // US-ROL-CRUD: filtrar por rol
        if (!string.IsNullOrWhiteSpace(roleFilter) && Enum.TryParse<UserRole>(roleFilter, out var role))
            query = query.Where(u => u.Role == role);

        // US-USR-01: búsqueda por nombre o email
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(q) ||
                u.Email.ToLower().Contains(q));
        }

        query = query.OrderBy(u => u.FullName);
        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<UserSystemDto>(
            items.Select(MapToDto),
            total, page, pageSize);
    }

    public async Task<UserSystemDto?> GetByIdAsync(Guid id)
    {
        var user = await _repo.GetByIdAsync(id);
        return user is null ? null : MapToDto(user);
    }

    // ── US-05 · Crear usuario ─────────────────────────────────────────────────

    public async Task<Result<UserSystemDto>> CreateAsync(
        CreateUserDto dto, Guid adminId, string adminName, string ip)
    {
        var exists = await _repo.AnyAsync(u => u.Email == dto.Email);
        if (exists)
            return Result<UserSystemDto>.Failure($"Ya existe un usuario con el correo {dto.Email}.");

        if (!Enum.TryParse<UserRole>(dto.Role, out var role))
            return Result<UserSystemDto>.Failure("Rol inválido.");

        var user = new UserSystem
        {
            FullName               = dto.FullName.Trim(),
            Email                  = dto.Email.Trim().ToLower(),
            PasswordHash           = _hasher.Hash(dto.TemporaryPassword), // BUG FIX: usar _hasher en lugar de BCrypt directo
            Role                   = role,
            Status                 = UserStatus.Activo,
            RequiresPasswordChange = true,
            CreatedAt              = DateTime.UtcNow
        };

        await _repo.AddAsync(user);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync("Usuarios", "USER_CREATED",
            $"Usuario creado: {user.Email} con rol {user.Role}.",
            userId: adminId, userName: adminName, ip: ip,
            newData: JsonSerializer.Serialize(new { user.Email, user.Role }));

        return Result<UserSystemDto>.Success(MapToDto(user));
    }

    // ── US-05 · Editar usuario ────────────────────────────────────────────────

    public async Task<Result<UserSystemDto>> UpdateAsync(
        Guid id, UpdateUserDto dto, Guid adminId, string adminName, string ip)
    {
        var user = await _repo.GetByIdAsync(id);
        if (user is null)
            return Result<UserSystemDto>.Failure("Usuario no encontrado.");

        var emailInUse = await _repo.AnyAsync(u => u.Email == dto.Email && u.Id != id);
        if (emailInUse)
            return Result<UserSystemDto>.Failure($"El correo {dto.Email} ya está en uso.");

        if (!Enum.TryParse<UserRole>(dto.Role, out var role))
            return Result<UserSystemDto>.Failure("Rol inválido.");

        var prev = JsonSerializer.Serialize(new
            { user.FullName, user.Email, Role = user.Role.ToString() });

        user.FullName  = dto.FullName.Trim();
        user.Email     = dto.Email.Trim().ToLower();
        if (dto.Phone is not null)
            user.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        user.Role      = role;
        user.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_UPDATED",
            $"Usuario actualizado: {user.Email}.",
            userId: adminId, userName: adminName, ip: ip,
            prevData: prev,
            newData: JsonSerializer.Serialize(new { user.FullName, user.Email, Role = user.Role.ToString() }));

        return Result<UserSystemDto>.Success(MapToDto(user));
    }

    // ── US-05 · Desactivar cuenta ─────────────────────────────────────────────

    public async Task<Result> DeactivateAsync(Guid id, Guid adminId, string adminName, string ip)
    {
        var user = await _repo.GetByIdAsync(id);
        if (user is null) return Result.Failure("Usuario no encontrado.");
        if (user.Status == UserStatus.Inactivo) return Result.Failure("La cuenta ya está desactivada.");

        var prev = user.Status.ToString();
        user.Status    = UserStatus.Inactivo;
        user.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_DEACTIVATED",
            $"Cuenta desactivada: {user.Email}.",
            userId: adminId, userName: adminName, ip: ip,
            prevData: $"{{\"status\":\"{prev}\"}}",
            newData:  "{\"status\":\"Inactivo\"}");

        return Result.Success();
    }

    // ── US-05 · Reactivar cuenta ──────────────────────────────────────────────

    public async Task<Result> ReactivateAsync(Guid id, Guid adminId, string adminName, string ip)
    {
        var user = await _repo.GetByIdAsync(id);
        if (user is null) return Result.Failure("Usuario no encontrado.");
        if (user.Status == UserStatus.Activo) return Result.Failure("La cuenta ya está activa.");

        user.Status    = UserStatus.Activo;
        user.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_REACTIVATED",
            $"Cuenta reactivada: {user.Email}.",
            userId: adminId, userName: adminName, ip: ip);

        return Result.Success();
    }

    // ── US-06 · Desbloquear cuenta ────────────────────────────────────────────

    public async Task<Result> UnlockAsync(Guid id, Guid adminId, string adminName, string ip)
    {
        var user = await _repo.GetByIdAsync(id);
        if (user is null) return Result.Failure("Usuario no encontrado.");
        if (user.Status != UserStatus.Bloqueado) return Result.Failure("La cuenta no está bloqueada.");

        user.Status               = UserStatus.Activo;
        user.FailedLoginAttempts  = 0;
        user.UpdatedAt            = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "ACCOUNT_UNLOCKED",
            $"Cuenta desbloqueada por admin: {user.Email}.",
            userId: adminId, userName: adminName, ip: ip,
            prevData: "{\"status\":\"Bloqueado\"}",
            newData:  "{\"status\":\"Activo\",\"failedAttempts\":0}");

        return Result.Success();
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    // ═══════════════════════════════════════════════════════════════════════
    // M7 — US-USR-01 · Baja lógica + force reset
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result> SoftDeleteAsync(
        Guid id, string justificacion, Guid adminId, string adminName, string ip)
    {
        var user = await _repo.GetByIdAsync(id);
        if (user is null) return Result.Failure("Usuario no encontrado.");
        if (user.IsDeleted) return Result.Failure("El usuario ya está eliminado.");

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.Status    = UserStatus.Inactivo;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_SOFT_DELETED",
            $"Usuario dado de baja: {user.Email} razón={justificacion}",
            userId: adminId, userName: adminName, ip: ip);
        return Result.Success();
    }

    public async Task<Result> ForceResetPasswordAsync(
        Guid id, string tempPassword, Guid adminId, string adminName, string ip)
    {
        var user = await _repo.GetByIdAsync(id);
        if (user is null) return Result.Failure("Usuario no encontrado.");

        user.PasswordHash             = _hasher.Hash(tempPassword);  // CORRECCIÓN #9
        user.RequiresPasswordChange   = true;
        user.FailedLoginAttempts      = 0;
        user.UpdatedAt                = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        await _audit.LogAsync("Usuarios", "USER_PASSWORD_FORCE_RESET",
            $"Contraseña reseteada por admin: {user.Email}",
            userId: adminId, userName: adminName, ip: ip);
        return Result.Success();
    }

    public async Task<UserSystemDetailDto?> GetDetailAsync(Guid id)
    {
        var user = await _repo.GetAll().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id);
        return user is null ? null : MapToDetailDto(user);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // M7 — US-USR-RECOVERY · Recuperación de contraseña
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result<ForgotPasswordResultDto>> ForgotPasswordAsync(string email)
    {
        var user = await _repo.GetAll()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

        // Siempre responder igual para no revelar si el email existe
        const string genericMsg = "Si el correo existe, recibirás el código de recuperación.";

        if (user is null)
            return Result<ForgotPasswordResultDto>.Success(
                new ForgotPasswordResultDto(genericMsg, "—", null));

        if (user.Status == UserStatus.Inactivo)
            return Result<ForgotPasswordResultDto>.Success(
                new ForgotPasswordResultDto(genericMsg, "—", null));

        // Invalidar tokens previos no usados
        var prevTokens = await _resetRepo.GetAll()
            .Where(t => t.UserId == user.Id && !t.Used && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var pt in prevTokens)
        {
            pt.Used = true;
            await _resetRepo.UpdateAsync(pt);
        }

        // Generar nuevo token
        var token = Guid.NewGuid().ToString("N"); // 32 chars hex
        var resetToken = new PasswordResetToken
        {
            UserId    = user.Id,
            Token     = token,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Channel   = !string.IsNullOrEmpty(user.Phone) ? "WhatsApp" : "Email",
            SentTo    = !string.IsNullOrEmpty(user.Phone)
                ? MaskPhone(user.Phone)
                : MaskEmail(user.Email),
        };
        await _resetRepo.AddAsync(resetToken);
        await _resetRepo.SaveChangesAsync();

        // En producción: aquí se enviaría por WhatsApp/email via INotifPublisher
        // Por ahora se loguea el token (en dev) y se retorna el canal
        _logger.LogInformation("PASSWORD_RESET_TOKEN for {Email}: {Token}", user.Email, token);

        return Result<ForgotPasswordResultDto>.Success(
            new ForgotPasswordResultDto(genericMsg, resetToken.Channel, resetToken.SentTo));
    }

    public async Task<Result> ResetPasswordAsync(string token, string newPassword, string confirmPassword)
    {
        if (newPassword != confirmPassword)
            return Result.Failure("Las contraseñas no coinciden.");

        if (newPassword.Length < 8)
            return Result.Failure("La contraseña debe tener al menos 8 caracteres.");

        var resetToken = await _resetRepo.GetAll()
            .FirstOrDefaultAsync(t => t.Token == token);

        if (resetToken is null || resetToken.Used)
            return Result.Failure("Token inválido o ya utilizado.");

        if (resetToken.IsExpired)
            return Result.Failure("El token ha expirado. Solicita uno nuevo.");

        var user = await _repo.GetByIdAsync(resetToken.UserId);
        if (user is null) return Result.Failure("Usuario no encontrado.");

        user.PasswordHash           = _hasher.Hash(newPassword);  // CORRECCIÓN #9
        user.RequiresPasswordChange = false;
        user.FailedLoginAttempts    = 0;
        user.UpdatedAt              = DateTime.UtcNow;
        await _repo.UpdateAsync(user);

        resetToken.Used = true;
        await _resetRepo.UpdateAsync(resetToken);

        return Result.Success();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // M7 — US-ROL-PERMISOS · Matriz de permisos
    // ═══════════════════════════════════════════════════════════════════════

    public List<RolePermissionsDto> GetPermissionMatrix() =>
    [
        new("Admin", "Administrador", "Acceso total al sistema",
            Modulos: ["Dashboard", "Clientes", "Facturación", "Pagos", "Tickets",
                      "Instalaciones", "Notificaciones", "Configuración", "Usuarios",
                      "Chatbot", "Reportes", "Auditoría"],
            Politicas: ["AdminOnly", "AdminOrTecnico", "AdminOrOperador", "AllRoles"]),

        new("Operador", "Operador de Cobros", "Gestión de cobros y consulta de clientes",
            Modulos: ["Dashboard (lectura)", "Clientes (consulta)", "Pagos",
                      "Facturación (consulta)", "Mi Caja"],
            Politicas: ["AdminOrOperador", "AllRoles"]),

        new("Tecnico", "Técnico", "Gestión de tickets e instalaciones",
            Modulos: ["Dashboard (lectura)", "Tickets", "Instalaciones",
                      "Clientes (consulta)"],
            Politicas: ["AdminOrTecnico", "AllRoles"]),

        new("SocioLectura", "Socio / Lectura", "Solo lectura en dashboard y reportes",
            Modulos: ["Dashboard (lectura)", "Reportes"],
            Politicas: ["AllRoles"]),
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static string MaskPhone(string phone) =>
        phone.Length > 4 ? "*****" + phone[^4..] : "****";

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***@" + (at >= 0 ? email[at..] : "");
        return email[0] + new string('*', Math.Min(at - 1, 3)) + email[at..];
    }

    private static UserSystemDetailDto MapToDetailDto(UserSystem u) => new(
        u.Id, u.FullName, u.Email, u.Role.ToString(),
        RoleLabelOf(u.Role), u.Status.ToString(),
        u.RequiresPasswordChange, u.FailedLoginAttempts,
        u.LastLoginAt, u.CreatedAt, u.Phone, u.IsDeleted, u.DeletedAt);

    private static string RoleLabelOf(UserRole r) => r switch
    {
        UserRole.Admin       => "Administrador",
        UserRole.Operador    => "Operador de Cobros",
        UserRole.Tecnico     => "Técnico",
        UserRole.SocioLectura => "Socio / Lectura",
        _ => r.ToString()
    };

    private static UserSystemDto MapToDto(UserSystem u) => new(
        Id:                     u.Id,
        FullName:               u.FullName,
        Email:                  u.Email,
        Role:                   u.Role.ToString(),
        Status:                 u.Status.ToString(),
        RequiresPasswordChange: u.RequiresPasswordChange,
        FailedLoginAttempts:    u.FailedLoginAttempts,
        LastLoginAt:            u.LastLoginAt,
        CreatedAt:              u.CreatedAt,
        Phone:                  u.Phone
    );
}
