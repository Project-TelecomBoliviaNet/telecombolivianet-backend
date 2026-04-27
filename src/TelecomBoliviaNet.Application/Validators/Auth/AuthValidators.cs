using FluentValidation;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;

namespace TelecomBoliviaNet.Application.Validators.Auth;

// ── Login ────────────────────────────────────────────────────────────────────

public class LoginValidator : AbstractValidator<LoginDto>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress().WithMessage("El formato del correo electrónico no es válido.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es obligatoria.");
    }
}

// ── Crear usuario ─────────────────────────────────────────────────────────────

public class CreateUserValidator : AbstractValidator<CreateUserDto>
{
    private static readonly string[] ValidRoles = Enum.GetNames<UserRole>(); // BUG FIX: derivado del enum para incluir todos los roles válidos

    public CreateUserValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("El nombre completo es obligatorio.")
            .MaximumLength(150).WithMessage("El nombre no puede exceder 150 caracteres.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress().WithMessage("El formato del correo electrónico no es válido.")
            .MaximumLength(200).WithMessage("El correo no puede exceder 200 caracteres.");

        RuleFor(x => x.TemporaryPassword)
            .NotEmpty().WithMessage("La contraseña temporal es obligatoria.")
            .MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .Matches("[A-Z]").WithMessage("La contraseña debe contener al menos una mayúscula.")
            .Matches("[a-z]").WithMessage("La contraseña debe contener al menos una minúscula.")
            .Matches("[0-9]").WithMessage("La contraseña debe contener al menos un número.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("El rol es obligatorio.")
            .Must(r => ValidRoles.Contains(r))
            .WithMessage($"El rol debe ser uno de: {string.Join(", ", ValidRoles)}.");
    }
}

// ── Cambiar contraseña ────────────────────────────────────────────────────────

public class ChangePasswordValidator : AbstractValidator<ChangePasswordDto>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("La contraseña actual es obligatoria.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("La nueva contraseña es obligatoria.")
            .MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .Matches("[A-Z]").WithMessage("La contraseña debe contener al menos una mayúscula.")
            .Matches("[a-z]").WithMessage("La contraseña debe contener al menos una minúscula.")
            .Matches("[0-9]").WithMessage("La contraseña debe contener al menos un número.")
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("La nueva contraseña no puede ser igual a la contraseña actual.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("La confirmación de contraseña es obligatoria.")
            .Equal(x => x.NewPassword)
            .WithMessage("La confirmación no coincide con la nueva contraseña.");
    }
}

// ── Actualizar usuario ────────────────────────────────────────────────────────

public class UpdateUserValidator : AbstractValidator<UpdateUserDto>
{
    private static readonly string[] ValidRoles = Enum.GetNames<UserRole>(); // BUG FIX: derivado del enum para incluir todos los roles válidos

    public UpdateUserValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("El nombre completo es obligatorio.")
            .MaximumLength(150).WithMessage("El nombre no puede exceder 150 caracteres.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress().WithMessage("El formato del correo electrónico no es válido.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("El rol es obligatorio.")
            .Must(r => ValidRoles.Contains(r))
            .WithMessage($"El rol debe ser uno de: {string.Join(", ", ValidRoles)}.");

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("El número de teléfono no puede exceder 20 caracteres.")
            .Matches(@"^\+?[0-9]{7,20}$")
            .WithMessage("El número debe contener solo dígitos (con código de país, ej: 59170000000).")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}
