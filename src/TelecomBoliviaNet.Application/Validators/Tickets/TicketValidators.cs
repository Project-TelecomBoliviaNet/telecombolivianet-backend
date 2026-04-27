using FluentValidation;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Domain.Entities.Tickets;

namespace TelecomBoliviaNet.Application.Validators.Tickets;

public class CreateTicketValidator : AbstractValidator<CreateTicketDto>
{
    private static readonly string[] ValidTypes    = Enum.GetNames<TicketType>();
    private static readonly string[] ValidPriority = Enum.GetNames<TicketPriority>();
    public CreateTicketValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty().WithMessage("El cliente es obligatorio.");
        RuleFor(x => x.Subject).NotEmpty().WithMessage("El asunto es obligatorio.")
            .MaximumLength(200).WithMessage("El asunto no puede superar 200 caracteres.");
        RuleFor(x => x.Type).NotEmpty().WithMessage("El tipo es obligatorio.")
            .Must(t => ValidTypes.Contains(t)).WithMessage("Tipo de ticket inválido.");
        RuleFor(x => x.Priority).NotEmpty().WithMessage("La prioridad es obligatoria.")
            .Must(p => ValidPriority.Contains(p)).WithMessage("Prioridad inválida.");
        RuleFor(x => x.Description).NotEmpty().WithMessage("La descripción es obligatoria.")
            .MaximumLength(1000).WithMessage("La descripción no puede superar 1000 caracteres.");
        RuleFor(x => x.SlaDurationHours)
            .GreaterThan(0).When(x => x.SlaDurationHours.HasValue)
            .WithMessage("Las horas SLA deben ser mayores a 0.");
    }
}

public class UpdateTicketValidator : AbstractValidator<UpdateTicketDto>
{
    private static readonly string[] ValidPriority = Enum.GetNames<TicketPriority>();
    public UpdateTicketValidator()
    {
        RuleFor(x => x.Subject).MaximumLength(200).When(x => x.Subject is not null)
            .WithMessage("El asunto no puede superar 200 caracteres.");
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null)
            .WithMessage("La descripción no puede superar 1000 caracteres.");
        RuleFor(x => x.Priority).Must(p => ValidPriority.Contains(p!)).When(x => x.Priority is not null)
            .WithMessage("Prioridad inválida.");
        RuleFor(x => x.RootCause).MaximumLength(4000).When(x => x.RootCause is not null)
            .WithMessage("La causa raíz no puede superar 4000 caracteres.");
    }
}

public class ChangeTicketStatusValidator : AbstractValidator<ChangeTicketStatusDto>
{
    private static readonly string[] ValidStatus = Enum.GetNames<TicketStatus>();
    public ChangeTicketStatusValidator()
    {
        RuleFor(x => x.Status).NotEmpty().WithMessage("El estado es obligatorio.")
            .Must(s => ValidStatus.Contains(s)).WithMessage("Estado inválido.");
        RuleFor(x => x.ResolutionMessage)
            .NotEmpty().When(x => x.Status == "Resuelto")
            .WithMessage("Se requiere mensaje de resolución al marcar como Resuelto.")
            .MaximumLength(2000).When(x => x.ResolutionMessage is not null)
            .WithMessage("El mensaje de resolución no puede superar 2000 caracteres.");
    }
}

public class AssignTicketValidator : AbstractValidator<AssignTicketDto>
{
    public AssignTicketValidator()
    {
        RuleFor(x => x.TechnicianId).NotEmpty().WithMessage("El técnico es obligatorio.");
    }
}

public class AddCommentValidator : AbstractValidator<AddCommentDto>
{
    private static readonly string[] ValidTypes = Enum.GetNames<CommentType>();
    public AddCommentValidator()
    {
        RuleFor(x => x.Type).NotEmpty().Must(t => ValidTypes.Contains(t))
            .WithMessage("Tipo inválido: RespuestaCliente | NotaInterna | CausaRaiz");
        RuleFor(x => x.Body).NotEmpty().WithMessage("El contenido es obligatorio.")
            .MaximumLength(4000).WithMessage("El comentario no puede superar 4000 caracteres.");
    }
}

public class AddWorkLogValidator : AbstractValidator<AddWorkLogDto>
{
    public AddWorkLogValidator()
    {
        RuleFor(x => x).Must(x => x.Hours * 60 + x.Minutes > 0)
            .WithMessage("El tiempo debe ser mayor a 0 minutos.");
        RuleFor(x => x.Hours).GreaterThanOrEqualTo(0).WithMessage("Las horas no pueden ser negativas.");
        RuleFor(x => x.Minutes).InclusiveBetween(0, 59).WithMessage("Los minutos deben estar entre 0 y 59.");
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null)
            .WithMessage("Las notas no pueden superar 500 caracteres.");
    }
}

public class ScheduleVisitValidator : AbstractValidator<ScheduleVisitDto>
{
    public ScheduleVisitValidator()
    {
        RuleFor(x => x.ScheduledAt).NotEmpty().WithMessage("La fecha de visita es obligatoria.")
            .GreaterThan(DateTime.UtcNow.AddMinutes(-5)).WithMessage("La fecha de visita debe ser futura.");
        RuleFor(x => x.Observations).MaximumLength(1000).When(x => x.Observations is not null)
            .WithMessage("Las observaciones no pueden superar 1000 caracteres.");
    }
}
