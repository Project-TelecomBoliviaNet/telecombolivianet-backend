using MediatR;
using TelecomBoliviaNet.Application.DTOs.Auth;

namespace TelecomBoliviaNet.Application.Users.Queries;

public record GetPermissionMatrixQuery : IRequest<List<RolePermissionsDto>>;

public class GetPermissionMatrixHandler : IRequestHandler<GetPermissionMatrixQuery, List<RolePermissionsDto>>
{
    public Task<List<RolePermissionsDto>> Handle(GetPermissionMatrixQuery _, CancellationToken ct) =>
        Task.FromResult<List<RolePermissionsDto>>(
        [
            new("Admin", "Administrador", "Acceso total al sistema",
                Modulos:  ["Dashboard", "Clientes", "Facturación", "Pagos", "Tickets",
                           "Instalaciones", "Notificaciones", "Configuración", "Usuarios",
                           "Chatbot", "Reportes", "Auditoría"],
                Politicas: ["AdminOnly", "AdminOrTecnico", "AdminOrOperador", "AllRoles"]),

            new("Operador", "Operador de Cobros", "Gestión de cobros y consulta de clientes",
                Modulos:  ["Dashboard (lectura)", "Clientes (consulta)", "Pagos",
                           "Facturación (consulta)", "Mi Caja"],
                Politicas: ["AdminOrOperador", "AllRoles"]),

            new("Tecnico", "Técnico", "Gestión de tickets e instalaciones",
                Modulos:  ["Dashboard (lectura)", "Tickets", "Instalaciones",
                           "Clientes (consulta)"],
                Politicas: ["AdminOrTecnico", "AllRoles"]),

            new("SocioLectura", "Socio / Lectura", "Solo lectura en dashboard y reportes",
                Modulos:  ["Dashboard (lectura)", "Reportes"],
                Politicas: ["AllRoles"]),
        ]);
}
