using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TelecomBoliviaNet.Domain.Entities.Auth;

namespace TelecomBoliviaNet.Application.Services.Auth;

public class JwtTokenService
{
    private readonly IConfiguration          _config;
    private readonly ILogger<JwtTokenService> _logger;

    public JwtTokenService(IConfiguration config, ILogger<JwtTokenService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string GenerateToken(UserSystem user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var hours = int.Parse(_config["Jwt:ExpirationHours"] ?? "8");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name,           user.FullName),
            new Claim(ClaimTypes.Email,          user.Email),
            new Claim(ClaimTypes.Role,           user.Role.ToString()),
            new Claim("requiresPasswordChange",  user.RequiresPasswordChange.ToString().ToLower())
        };

        var token = new JwtSecurityToken(
            issuer:             _config["Jwt:Issuer"],
            audience:           _config["Jwt:Audience"],
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(hours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // CORRECCIÓN (Fix #16): ReadJwtToken lanza ArgumentException/SecurityTokenMalformedException
    // si el token no es un JWT válido (ocurre si alguien pasa un token arbitrario al logout).
    // Devolvemos UtcNow como fallback seguro: el token se blacklistea con expiración inmediata.
    public DateTime GetExpiration(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.ValidTo == DateTime.MinValue ? DateTime.UtcNow : jwt.ValidTo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWT malformado en GetExpiration — se expira inmediatamente");
            return DateTime.UtcNow;
        }
    }

    public string ExtractRawToken(string authorizationHeader)
        => authorizationHeader.Replace("Bearer ", "").Trim();
}
