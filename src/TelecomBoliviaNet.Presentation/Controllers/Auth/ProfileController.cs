using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.Users.Queries;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Auth;

[Route("api/profile")]
public class ProfileController : BaseController
{
    private readonly IMediator _mediator;

    public ProfileController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = "AllRoles")]
    public async Task<IActionResult> GetProfile()
    {
        var user = await _mediator.Send(new GetUserByIdQuery(CurrentUserId));
        return user is null ? NotFoundResult("Usuario no encontrado.") : OkResult(user);
    }

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

        return OkResult(new { Role = CurrentUserRole, UserId = CurrentUserId, Permissions = permissions });
    }

    [HttpGet("admin-only")]
    [Authorize(Policy = "AdminOnly")]
    public IActionResult AdminOnly() =>
        OkResult(new
        {
            Message = "Acceso confirmado: solo los administradores pueden ver esto.",
            UserId  = CurrentUserId,
            Role    = CurrentUserRole
        });
}
