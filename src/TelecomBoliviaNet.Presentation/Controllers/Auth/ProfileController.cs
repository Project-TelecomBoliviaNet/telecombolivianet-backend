using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Auth;

/// <summary>
/// Endpoints accesibles según las políticas de rol.
/// Sirve para demostrar y probar los guards de US-10.
/// </summary>
[Route("api/profile")]
public class ProfileController : BaseController
{
    private readonly UserSystemService _userService;

    public ProfileController(UserSystemService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// US-10 · Política AllRoles — accesible para Admin, Técnico y SocioLectura.
    /// Devuelve el perfil del usuario autenticado.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetProfile()
    {
        var user = await _userService.GetByIdAsync(CurrentUserId);
        if (user is null) return NotFoundResult("Usuario no encontrado.");
        return OkResult(user);
    }

    /// <summary>
    /// US-10 · Política AdminOrTecnico — accesible para Admin y Técnico.
    /// Devuelve las acciones disponibles según el rol. Los Socios reciben HTTP 403.
    /// </summary>
    [HttpGet("permissions")]
    [Authorize(Policy = "AdminOrTecnico")]
    public IActionResult GetPermissions()
    {
        var permissions = CurrentUserRole switch
        {
            "Admin" => new[]
            {
                "ver_dashboard", "gestionar_clientes", "gestionar_usuarios",
                "verificar_pagos", "enviar_notificaciones", "ver_reportes",
                "ver_audit_log", "configurar_sistema", "gestionar_tickets"
            },
            "Tecnico" => new[]
            {
                "ver_clientes_asignados", "actualizar_tickets_propios",
                "registrar_clientes", "gestionar_tickets"
            },
            _ => Array.Empty<string>()
        };

        return OkResult(new
        {
            Role        = CurrentUserRole,
            UserId      = CurrentUserId,
            Permissions = permissions
        });
    }

    /// <summary>
    /// US-10 · Política AdminOnly — solo Admin.
    /// Los Técnicos y Socios reciben HTTP 403.
    /// </summary>
    [HttpGet("admin-only")]
    [Authorize(Policy = "AdminOnly")]
    public IActionResult AdminOnly()
    {
        return OkResult(new
        {
            Message = "Acceso confirmado: solo los administradores pueden ver esto.",
            UserId  = CurrentUserId,
            Role    = CurrentUserRole
        });
    }
}
