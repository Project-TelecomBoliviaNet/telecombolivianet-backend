using System.Text.RegularExpressions;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.ValueObjects;

/// <summary>
/// Número de teléfono boliviano normalizado a formato internacional (591XXXXXXXX).
/// Acepta 8 dígitos locales (6x/7x) o el prefijo 591 ya incluido.
/// Invariante: siempre almacena 11 dígitos comenzando con "591".
/// </summary>
public sealed record PhoneNumber
{
    private static readonly Regex _digits = new(@"\D", RegexOptions.Compiled);

    public string Value { get; }

    public PhoneNumber(string value)
    {
        var digits = _digits.Replace(value ?? string.Empty, "");

        if (digits.Length == 8 && (digits[0] == '6' || digits[0] == '7'))
            digits = "591" + digits;

        if (digits.Length != 11 || !digits.StartsWith("591"))
            throw new DomainException(
                $"Número boliviano inválido: '{value}'. " +
                "Formato esperado: 8 dígitos comenzando en 6 o 7 (ej: 71234567).");

        Value = digits;
    }

    // Conversión implícita para compatibilidad con strings existentes
    public static implicit operator string(PhoneNumber p) => p.Value;
    public static implicit operator PhoneNumber(string s) => new(s);
    public override string ToString() => Value;
}
