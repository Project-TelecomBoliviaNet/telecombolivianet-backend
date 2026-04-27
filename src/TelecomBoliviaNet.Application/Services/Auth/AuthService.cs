using Microsoft.Extensions.Configuration;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Auth;

public class AuthService
{
    private readonly IGenericRepository<UserSystem>     _userRepo;
    private readonly IGenericRepository<TokenBlacklist> _tokenRepo;
    private readonly JwtTokenService                    _jwtService;
    private readonly RefreshTokenService                _refreshService;
    private readonly AuditService                       _audit;
    private readonly IConfiguration                     _config;
    // BUG FIX: inyectar IPasswordHasher para no llamar BCrypt directamente
    private readonly IPasswordHasher                    _hasher;

    public AuthService(
        IGenericRepository<UserSystem>     userRepo,
        IGenericRepository<TokenBlacklist> tokenRepo,
        JwtTokenService                    jwtService,
        RefreshTokenService                refreshService,
        AuditService                       audit,
        IConfiguration                     config,
        IPasswordHasher                    hasher)
    {
        _userRepo       = userRepo;
        _tokenRepo      = tokenRepo;
        _jwtService     = jwtService;
        _refreshService = refreshService;
        _audit          = audit;
        _config         = config;
        _hasher         = hasher;
    }

    // ── US-01 · Login ────────────────────────────────────────────────────────

    public async Task<Result<LoginResponseDto>> LoginAsync(LoginDto dto, string ip)
    {
        var user = await _userRepo.FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null)
        {
            await _audit.LogAsync("Auth", "LOGIN_FAILED",
                $"Email no registrado: {dto.Email}", ip: ip);
            return Result<LoginResponseDto>.Failure("Credenciales incorrectas.");
        }

        if (user.Status == UserStatus.Inactivo)
            return Result<LoginResponseDto>.Failure("Cuenta desactivada. Contacte al administrador.");

        if (user.Status == UserStatus.Bloqueado)
            return Result<LoginResponseDto>.Failure("Cuenta bloqueada. Contacte al administrador.");

        if (!_hasher.Verify(dto.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            var maxAttempts = int.Parse(_config["Security:MaxFailedLoginAttempts"] ?? "5");

            if (user.FailedLoginAttempts >= maxAttempts)
            {
                user.Status = UserStatus.Bloqueado;
                await _audit.LogAsync("Auth", "ACCOUNT_BLOCKED",
                    $"Cuenta bloqueada tras {maxAttempts} intentos fallidos.",
                    userId: user.Id, userName: user.FullName, ip: ip,
                    prevData: "{\"status\":\"Activo\"}",
                    newData:  "{\"status\":\"Bloqueado\"}");
            }

            await _userRepo.UpdateAsync(user);
            await _audit.LogAsync("Auth", "LOGIN_FAILED",
                $"Contrasena incorrecta. Intento #{user.FailedLoginAttempts}.",
                userId: user.Id, userName: user.FullName, ip: ip);

            return Result<LoginResponseDto>.Failure("Credenciales incorrectas.");
        }

        // Login exitoso
        user.FailedLoginAttempts = 0;
        user.LastLoginAt         = DateTime.UtcNow;
        await _userRepo.UpdateAsync(user);

        var accessToken  = _jwtService.GenerateToken(user);
        var refreshToken = await _refreshService.GenerateAsync(user.Id, ip);

        await _audit.LogAsync("Auth", "LOGIN_SUCCESS", "Inicio de sesion exitoso.",
            userId: user.Id, userName: user.FullName, ip: ip);

        var redirect = user.Role switch
        {
            UserRole.Admin        => "/dashboard",
            UserRole.Tecnico      => "/tickets",
            UserRole.SocioLectura => "/dashboard",
            _                     => "/dashboard"
        };

        return Result<LoginResponseDto>.Success(new LoginResponseDto(
            Token:                  accessToken,
            RefreshToken:           refreshToken,
            UserId:                 user.Id.ToString(),
            FullName:               user.FullName,
            Email:                  user.Email,
            Role:                   user.Role.ToString(),
            RequiresPasswordChange: user.RequiresPasswordChange,
            RedirectUrl:            redirect
        ));
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────────

    public async Task<Result<RefreshResponseDto>> RefreshAsync(string rawRefreshToken, string ip)
    {
        var (entity, newRawToken) = await _refreshService.RotateAsync(rawRefreshToken, ip);

        if (entity is null || newRawToken is null)
            return Result<RefreshResponseDto>.Failure("Refresh token invalido, expirado o revocado.");

        var user = await _userRepo.GetByIdAsync(entity.UserId);
        if (user is null || user.Status != UserStatus.Activo)
            return Result<RefreshResponseDto>.Failure("Usuario no encontrado o inactivo.");

        var newAccessToken = _jwtService.GenerateToken(user);

        return Result<RefreshResponseDto>.Success(
            new RefreshResponseDto(Token: newAccessToken, RefreshToken: newRawToken));
    }

    // ── US-03 · Logout ───────────────────────────────────────────────────────

    public async Task<r> LogoutAsync(
        string rawToken, string? rawRefreshToken,
        Guid userId, string userName, string ip)
    {
        var expiration = _jwtService.GetExpiration(rawToken);
        await _tokenRepo.AddAsync(new TokenBlacklist
        {
            Token         = rawToken,
            UserId        = userId,
            ExpiresAt     = expiration,
            InvalidatedAt = DateTime.UtcNow
        });
        await _tokenRepo.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
            await _refreshService.RevokeAllForUserAsync(userId, "Logout explicito");

        await _audit.LogAsync("Auth", "LOGOUT", "Cierre de sesion.",
            userId: userId, userName: userName, ip: ip);

        return Result.Success();
    }

    // ── Validar blacklist (middleware) ────────────────────────────────────────

    public async Task<bool> IsTokenBlacklistedAsync(string rawToken)
        => await _tokenRepo.AnyAsync(t => t.Token == rawToken);

    // ── US-07 / US-08 · Cambio de contrasena ─────────────────────────────────

    public async Task<r> ChangePasswordAsync(Guid userId, ChangePasswordDto dto, string ip)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null) return Result.Failure("Usuario no encontrado.");

        if (!_hasher.Verify(dto.CurrentPassword, user.PasswordHash))
            return Result.Failure("La contrasena actual es incorrecta.");

        if (_hasher.Verify(dto.NewPassword, user.PasswordHash))
            return Result.Failure("La nueva contrasena no puede ser igual a la actual.");

        user.PasswordHash           = _hasher.Hash(dto.NewPassword); // BUG FIX
        user.RequiresPasswordChange = false;
        user.UpdatedAt              = DateTime.UtcNow;
        await _userRepo.UpdateAsync(user);

        await _audit.LogAsync("Auth", "PASSWORD_CHANGED", "Contrasena cambiada.",
            userId: user.Id, userName: user.FullName, ip: ip);

        return Result.Success();
    }
}
