using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Users;

[Route("api/users")]
[Authorize(Policy = "AdminOnly")]
public class UserSystemController : BaseController
{
    private readonly UserSystemService         _service;
    private readonly IValidator<CreateUserDto> _createValidator;
    private readonly IValidator<UpdateUserDto> _updateValidator;

    public UserSystemController(
        UserSystemService          service,
        IValidator<CreateUserDto>  createValidator,
        IValidator<UpdateUserDto>  updateValidator)
    {
        _service         = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>US-05 · Listar todos los usuarios del sistema (paginado).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetAllAsync(page, pageSize);
        return OkResult(result);
    }

    /// <summary>US-05 · Obtener un usuario por ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var dto = await _service.GetByIdAsync(id);
        if (dto is null) return NotFoundResult("Usuario no encontrado.");
        return OkResult(dto);
    }

    /// <summary>US-05 · Crear un nuevo usuario del sistema.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        var validation = await _createValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return BadRequest(new
            {
                success = false,
                errors  = validation.Errors.Select(e => e.ErrorMessage)
            });

        var result = await _service.CreateAsync(
            dto, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess)
            return BadRequestResult(result.ErrorMessage);

        return StatusCode(201, new { success = true, data = result.Value });
    }

    /// <summary>US-05 · Editar nombre, correo o rol de un usuario.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        var validation = await _updateValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return BadRequest(new
            {
                success = false,
                errors  = validation.Errors.Select(e => e.ErrorMessage)
            });

        var result = await _service.UpdateAsync(
            id, dto, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess)
            return BadRequestResult(result.ErrorMessage);

        return OkResult(result.Value!);
    }

    /// <summary>US-05 · Desactivar cuenta (no elimina datos históricos).</summary>
    [HttpPut("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _service.DeactivateAsync(
            id, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess)
            return BadRequestResult(result.ErrorMessage);

        return OkMessage("Cuenta desactivada correctamente.");
    }

    /// <summary>US-05 · Reactivar cuenta desactivada.</summary>
    [HttpPut("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var result = await _service.ReactivateAsync(
            id, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess)
            return BadRequestResult(result.ErrorMessage);

        return OkMessage("Cuenta reactivada correctamente.");
    }

    /// <summary>US-06 · Desbloquear cuenta bloqueada por intentos fallidos.</summary>
    [HttpPut("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id)
    {
        var result = await _service.UnlockAsync(
            id, CurrentUserId, CurrentUserName, ClientIp);

        if (!result.IsSuccess)
            return BadRequestResult(result.ErrorMessage);

        return OkMessage("Cuenta desbloqueada correctamente.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // M7 — US-ROL-PERMISOS · Matriz de permisos
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet("permissions")]
    public IActionResult GetPermissions()
        => OkResult(_service.GetPermissionMatrix());

    // ════════════════════════════════════════════════════════════════════════
    // M7 — US-USR-01 · Detalle + baja lógica + force reset
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet("{id:guid}/detail")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        var dto = await _service.GetDetailAsync(id);
        return dto is null ? NotFoundResult("Usuario no encontrado.") : OkResult(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, [FromBody] DeleteUserDto dto)
    {
        if (id == CurrentUserId)
            return BadRequestResult("No puedes eliminar tu propio usuario.");

        var result = await _service.SoftDeleteAsync(id, dto.Justificacion,
            CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkMessage("Usuario dado de baja.") : BadRequestResult(result.ErrorMessage);
    }

    [HttpPost("{id:guid}/force-reset-password")]
    public async Task<IActionResult> ForceResetPassword(Guid id, [FromBody] ForceResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NewTemporaryPassword) || dto.NewTemporaryPassword.Length < 8)
            return BadRequestResult("La contraseña temporal debe tener al menos 8 caracteres.");

        var result = await _service.ForceResetPasswordAsync(id, dto.NewTemporaryPassword,
            CurrentUserId, CurrentUserName, ClientIp);
        return result.IsSuccess ? OkMessage("Contraseña reseteada. El usuario deberá cambiarla al iniciar sesión.") : BadRequestResult(result.ErrorMessage);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M7 — US-USR-RECOVERY · Recuperación por email (público)
    // ════════════════════════════════════════════════════════════════════════

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var result = await _service.ForgotPasswordAsync(dto.Email);
        // Siempre 200 para no revelar si el email existe
        return result.IsSuccess ? OkResult(result.Value) : OkResult(new ForgotPasswordResultDto("Si el correo existe, recibirás el código de recuperación.", "—", null));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var result = await _service.ResetPasswordAsync(dto.Token, dto.NewPassword, dto.ConfirmPassword);
        return result.IsSuccess ? OkMessage("Contraseña actualizada exitosamente. Ya puedes iniciar sesión.") : BadRequestResult(result.ErrorMessage);
    }
}
