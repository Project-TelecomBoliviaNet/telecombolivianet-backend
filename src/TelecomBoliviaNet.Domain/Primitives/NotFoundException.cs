namespace TelecomBoliviaNet.Domain.Primitives;

/// <summary>
/// Excepción lanzada cuando un recurso solicitado no existe en el sistema.
/// GlobalExceptionHandler la mapea a HTTP 404.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }

    public static NotFoundException For(string resource, object id)
        => new($"{resource} con id '{id}' no encontrado.");
}
