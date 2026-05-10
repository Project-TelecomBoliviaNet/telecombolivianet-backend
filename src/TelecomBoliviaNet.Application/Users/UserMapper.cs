using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;

namespace TelecomBoliviaNet.Application.Users;

internal static class UserMapper
{
    internal static UserSystemDto ToDto(UserSystem u) => new(
        Id:                     u.Id,
        FullName:               u.FullName,
        Email:                  u.Email,
        Role:                   u.Role.ToString(),
        Status:                 u.Status.ToString(),
        RequiresPasswordChange: u.RequiresPasswordChange,
        FailedLoginAttempts:    u.FailedLoginAttempts,
        LastLoginAt:            u.LastLoginAt,
        CreatedAt:              u.CreatedAt,
        Phone:                  u.Phone);

    internal static UserSystemDetailDto ToDetailDto(UserSystem u) => new(
        u.Id, u.FullName, u.Email, u.Role.ToString(),
        RoleLabelOf(u.Role), u.Status.ToString(),
        u.RequiresPasswordChange, u.FailedLoginAttempts,
        u.LastLoginAt, u.CreatedAt, u.Phone, u.IsDeleted, u.DeletedAt);

    internal static string RoleLabelOf(UserRole r) => r switch
    {
        UserRole.Admin        => "Administrador",
        UserRole.Operador     => "Operador de Cobros",
        UserRole.Tecnico      => "Técnico",
        UserRole.SocioLectura => "Socio / Lectura",
        _                     => r.ToString()
    };

    internal static string MaskPhone(string phone) =>
        phone.Length > 4 ? "*****" + phone[^4..] : "****";

    internal static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***@" + (at >= 0 ? email[at..] : "");
        return email[0] + new string('*', Math.Min(at - 1, 3)) + email[at..];
    }
}
