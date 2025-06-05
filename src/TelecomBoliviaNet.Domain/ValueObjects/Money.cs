using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.ValueObjects;

/// <summary>
/// Representa un monto monetario en bolivianos (Bs).
/// Invariante: siempre ≥ 0, redondeado a 2 decimales (redondeo bancario AwayFromZero).
/// </summary>
public sealed record Money
{
    public decimal Amount { get; }

    private Money() { } // Para EF Core

    public Money(decimal amount)
    {
        if (amount < 0)
            throw new DomainException("El monto monetario no puede ser negativo.");

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    public static Money Zero => new(0m);

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);

    public static Money operator -(Money a, Money b)
    {
        var result = a.Amount - b.Amount;
        if (result < 0)
            throw new DomainException(
                $"La resta resulta en monto negativo ({a.Amount} - {b.Amount}).");
        return new(result);
    }

    public bool IsZero => Amount == 0m;

    public override string ToString() => $"Bs. {Amount:F2}";
}
