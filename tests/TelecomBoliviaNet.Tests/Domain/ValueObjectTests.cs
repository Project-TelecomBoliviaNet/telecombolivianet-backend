using FluentAssertions;
using TelecomBoliviaNet.Domain.Primitives;
using TelecomBoliviaNet.Domain.ValueObjects;
using Xunit;

namespace TelecomBoliviaNet.Tests.Domain;

// ══════════════════════════════════════════════════════════════════════════════
// FIX-18 — Value Objects: PhoneNumber, Money, TbnCode
// Tests puramente unitarios — sin dependencias externas ni IO.
// ══════════════════════════════════════════════════════════════════════════════

public class PhoneNumberTests
{
    // ── Casos válidos ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("71234567",    "59171234567")]  // 8 dígitos, prefijo 7x
    [InlineData("61234567",    "59161234567")]  // 8 dígitos, prefijo 6x
    [InlineData("59171234567", "59171234567")]  // ya tiene prefijo 591
    [InlineData("+59171234567","59171234567")]  // con + internacional (strip non-digits)
    [InlineData("7-123 456 7", "59171234567")] // con guiones/espacios — strip non-digits
    public void PhoneNumber_WithValidInput_NormalizesTo591Format(string input, string expected)
    {
        var phone = new PhoneNumber(input);
        phone.Value.Should().Be(expected);
    }

    [Fact]
    public void PhoneNumber_ImplicitConversion_ToStringReturnsValue()
    {
        PhoneNumber phone = "71234567";
        string s = phone;
        s.Should().Be("59171234567");
    }

    [Fact]
    public void PhoneNumber_ToString_ReturnsNormalizedValue()
    {
        var phone = new PhoneNumber("71234567");
        phone.ToString().Should().Be("59171234567");
    }

    [Fact]
    public void PhoneNumber_RecordEquality_SameValueAreEqual()
    {
        var a = new PhoneNumber("71234567");
        var b = new PhoneNumber("59171234567");
        a.Should().Be(b);
    }

    // ── Casos inválidos ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("91234567")]    // empieza en 9 (no válido en Bolivia)
    [InlineData("1234567")]     // solo 7 dígitos
    [InlineData("")]            // vacío
    [InlineData("abcdefgh")]    // solo letras
    [InlineData("123456789012")] // 12 dígitos
    public void PhoneNumber_WithInvalidInput_ThrowsDomainException(string input)
    {
        var act = () => new PhoneNumber(input);
        act.Should().Throw<DomainException>()
           .WithMessage("*boliviano inválido*");
    }

    [Fact]
    public void PhoneNumber_WithNull_ThrowsDomainException()
    {
        var act = () => new PhoneNumber(null!);
        act.Should().Throw<DomainException>();
    }
}

public class MoneyTests
{
    // ── Creación válida ────────────────────────────────────────────────────────

    [Fact]
    public void Money_WithZero_CreatesSuccessfully()
    {
        var m = new Money(0m);
        m.Amount.Should().Be(0m);
        m.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Money_WithPositiveAmount_CreatesSuccessfully()
    {
        var m = new Money(150.50m);
        m.Amount.Should().Be(150.50m);
        m.IsZero.Should().BeFalse();
    }

    [Fact]
    public void Money_RoundsAwayFromZero_ToTwoDecimals()
    {
        new Money(10.555m).Amount.Should().Be(10.56m);  // redondeo bancario
        new Money(10.554m).Amount.Should().Be(10.55m);
    }

    [Fact]
    public void Money_Zero_StaticProperty_ReturnsZeroMoney()
    {
        Money.Zero.Amount.Should().Be(0m);
        Money.Zero.IsZero.Should().BeTrue();
    }

    // ── Operadores ─────────────────────────────────────────────────────────────

    [Fact]
    public void Money_Addition_ReturnsSumOfAmounts()
    {
        var a = new Money(100m);
        var b = new Money(50.25m);
        (a + b).Amount.Should().Be(150.25m);
    }

    [Fact]
    public void Money_Subtraction_WhenResultPositive_Succeeds()
    {
        var a = new Money(200m);
        var b = new Money(50m);
        (a - b).Amount.Should().Be(150m);
    }

    [Fact]
    public void Money_Subtraction_WhenResultNegative_ThrowsDomainException()
    {
        var a = new Money(50m);
        var b = new Money(100m);
        var act = () => _ = a - b;
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Money_RecordEquality_SameAmountAreEqual()
    {
        new Money(100m).Should().Be(new Money(100m));
        new Money(100m).Should().NotBe(new Money(200m));
    }

    // ── Invariante negativo ────────────────────────────────────────────────────

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    [InlineData(-999999)]
    public void Money_WithNegativeAmount_ThrowsDomainException(decimal amount)
    {
        var act = () => new Money(amount);
        act.Should().Throw<DomainException>()
           .WithMessage("*negativo*");
    }

    [Fact]
    public void Money_ToString_FormatsWithBsPrefix()
    {
        new Money(150m).ToString().Should().Contain("150");
        new Money(150m).ToString().Should().Contain("Bs");
    }
}

public class TbnCodeTests
{
    // ── Casos válidos ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("TBN-0001", "TBN-0001")]
    [InlineData("TBN-9999", "TBN-9999")]
    [InlineData("tbn-0152", "TBN-0152")]  // normaliza a mayúsculas
    [InlineData("TBN-0000", "TBN-0000")]
    public void TbnCode_WithValidFormat_CreatesAndNormalizesToUppercase(string input, string expected)
    {
        var code = new TbnCode(input);
        code.Value.Should().Be(expected);
    }

    [Fact]
    public void TbnCode_ImplicitConversion_ToStringReturnsValue()
    {
        TbnCode code = "TBN-0001";
        string s = code;
        s.Should().Be("TBN-0001");
    }

    [Fact]
    public void TbnCode_RecordEquality_SameValueAreEqual()
    {
        new TbnCode("TBN-0001").Should().Be(new TbnCode("TBN-0001"));
        new TbnCode("TBN-0001").Should().NotBe(new TbnCode("TBN-0002"));
    }

    // ── Casos inválidos ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("TBN-123")]      // 3 dígitos
    [InlineData("TBN-12345")]    // 5 dígitos
    [InlineData("123456")]       // sin prefijo TBN-
    [InlineData("TBN0001")]      // sin guión
    [InlineData("")]
    [InlineData("ABC-0001")]     // prefijo distinto
    public void TbnCode_WithInvalidFormat_ThrowsDomainException(string input)
    {
        var act = () => new TbnCode(input);
        act.Should().Throw<DomainException>()
           .WithMessage("*TBN inválido*");
    }

    [Fact]
    public void TbnCode_WithNull_ThrowsDomainException()
    {
        var act = () => new TbnCode(null!);
        act.Should().Throw<DomainException>();
    }
}
