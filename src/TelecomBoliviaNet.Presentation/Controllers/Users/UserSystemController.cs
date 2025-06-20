using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Users.Commands;
using TelecomBoliviaNet.Application.Users.Queries;
using TelecomBoliviaNet.Presentation.Controllers;

namespace TelecomBoliviaNet.Presentation.Controllers.Users;

[Route("api/users")]
[Authorize(Policy = "AdminOnly")]
public class UserSystemController : BaseController
{
    private readonly IMediator                 _mediator;
    private readonly IValidator<CreateUserDto> _createValidator;
    private readonly IValidator<UpdateUserDto> _updateValidator;

    public UserSystemController(
        IMediator                  mediator,
        IValidator<CreateUserDto>  createValidator,
        IValidator<UpdateUserDto>  updateValidator)
    {
        _mediator        = mediator;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int     page       = 1,
        [FromQuery] int     pageSize   = 20,
        [FromQuery] string? roleFilter = null,
        [FromQuery] string? search     = null)
        => OkResult(await _mediator.Send(new GetUsersListQuery(page, pageSize, roleFilter, search)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var dto = await _mediator.Send(new GetUserByIdQuery(id));
        return dto is null ? NotFoundResult("Usuario no encontrado.") : OkResult(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        var validation = await _createValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return BadRequest(new { success = false, errors = validation.Errors.Select(e => e.ErrorMessage) });

        var result = await _mediator.Send(
            new CreateUserCommand(dto, CurrentUserId, CurrentUserName, ClientIp));

        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return StatusCode(201, new { success = true, data = result.Value });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        var validation = await _updateValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return BadRequest(new { success = false, errors = validation.Errors.Select(e => e.ErrorMessage) });

        var result = await _mediator.Send(
            new UpdateUserCommand(id, dto, CurrentUserId, CurrentUserName, ClientIp));

        if (!result.IsSuccess) return BadRequestResult(result.ErrorMessage);
        return OkResult(result.Value!);
    }

    [HttpPut("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _mediator.Send(
            new DeactivateUserCommand(id, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess
            ? OkMessage("Cuenta desactivada correctamente.")
            : BadRequestResult(result.ErrorMessage);
    }

    [HttpPut("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var result = await _mediator.Send(
            new ReactivateUserCommand(id, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess
            ? OkMessage("Cuenta reactivada correctamente.")
            : BadRequestResult(result.ErrorMessage);
    }

    [HttpPut("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id)
    {
        var result = await _mediator.Send(
            new UnlockUserCommand(id, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess
            ? OkMessage("Cuenta desbloqueada correctamente.")
            : BadRequestResult(result.ErrorMessage);
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions()
        => OkResult(await _mediator.Send(new GetPermissionMatrixQuery()));

    [HttpGet("{id:guid}/detail")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        var dto = await _mediator.Send(new GetUserDetailQuery(id));
        return dto is null ? NotFoundResult("Usuario no encontrado.") : OkResult(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, [FromBody] DeleteUserDto dto)
    {
        if (id == CurrentUserId)
            return BadRequestResult("No puedes eliminar tu propio usuario.");

        var result = await _mediator.Send(
            new SoftDeleteUserCommand(id, dto.Justificacion, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess ? OkMessage("Usuario dado de baja.") : BadRequestResult(result.ErrorMessage);
    }

    [HttpPost("{id:guid}/force-reset-password")]
    public async Task<IActionResult> ForceResetPassword(Guid id, [FromBody] ForceResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NewTemporaryPassword) || dto.NewTemporaryPassword.Length < 8)
            return BadRequestResult("La contraseña temporal debe tener al menos 8 caracteres.");

        var result = await _mediator.Send(
            new ForceResetPasswordCommand(id, dto.NewTemporaryPassword, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess
            ? OkMessage("Contraseña reseteada. El usuario deberá cambiarla al iniciar sesión.")
            : BadRequestResult(result.ErrorMessage);
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
            return BadRequestResult("El token de activación es obligatorio.");

        var result = await _mediator.Send(new ConfirmEmailCommand(dto.Token));
        return result.IsSuccess
            ? OkMessage("Cuenta activada correctamente. Ya puedes iniciar sesión.")
            : BadRequestResult(result.ErrorMessage);
    }

    [HttpPost("{id:guid}/resend-activation")]
    public async Task<IActionResult> ResendActivation(Guid id)
    {
        var result = await _mediator.Send(
            new ResendActivationCommand(id, CurrentUserId, CurrentUserName, ClientIp));
        return result.IsSuccess
            ? OkMessage("Email de activación reenviado correctamente.")
            : BadRequestResult(result.ErrorMessage);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var result = await _mediator.Send(new ForgotPasswordCommand(dto.Email));
        return result.IsSuccess
            ? OkResult(result.Value)
            : BadRequestResult(result.ErrorMessage);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var result = await _mediator.Send(
            new ResetPasswordCommand(dto.Token, dto.NewPassword, dto.ConfirmPassword));
        return result.IsSuccess
            ? OkMessage("Contraseña actualizada exitosamente. Ya puedes iniciar sesión.")
            : BadRequestResult(result.ErrorMessage);
    }
}
