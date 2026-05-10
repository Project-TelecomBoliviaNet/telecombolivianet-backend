namespace TelecomBoliviaNet.Domain.Entities.Auth;

/// <summary>
/// Roles del sistema. US-ROL-CRUD / US-ROL-PERMISOS.
/// - Admin:         acceso total
/// - Operador:      gestión de cobros y clientes, sin acceso a configuración ni usuarios
/// - Tecnico:       gestión de tickets e instalaciones
/// - SocioLectura:  solo lectura en dashboard y reportes
/// </summary>
public enum UserRole
{
    Admin,
    Operador,       // US-ROL-CRUD: nuevo rol de operador de cobros
    Tecnico,
    SocioLectura
}
