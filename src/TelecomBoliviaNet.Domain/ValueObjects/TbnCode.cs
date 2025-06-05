using System.Text.RegularExpressions;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.ValueObjects;

/// <summary>
/// Código de cliente TelecomBoliviaNet: TBN-XXXX (4 dígitos).
/// Ejemplo válido: TBN-0001, TBN-0152.
/// Normaliza a mayúsculas automáticamente.
/// </summary>
public sealed record TbnCode
{
    private static readonly Regex _pattern =
        new(@"^TBN-\d{4}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Value { get; }

    public TbnCode(string value)
    {
        if (!_pattern.IsMatch(value ?? string.Empty))
            throw new DomainException(
                $"Código TBN inválido: '{value}'. Formato esperado: TBN-XXXX (4 dígitos, ej: TBN-0001).");

        Value = (value ?? string.Empty).ToUpperInvariant();
    }

    public static implicit operator string(TbnCode t) => t.Value;
    public static implicit operator TbnCode(string s) => new(s);
    public override string ToString() => Value;
}
