namespace TelecomBoliviaNet.Domain.Primitives;

public abstract class Entity
{
    // BUG FIX: init en lugar de set para bloquear mutación posterior del PK
    public Guid Id { get; init; } = Guid.NewGuid();
}
