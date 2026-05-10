namespace TelecomBoliviaNet.Domain.Primitives;

/// <summary>
/// Excepción lanzada cuando se viola una regla de negocio.
/// GlobalExceptionHandler la mapea a HTTP 400 con el mensaje como Title.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
