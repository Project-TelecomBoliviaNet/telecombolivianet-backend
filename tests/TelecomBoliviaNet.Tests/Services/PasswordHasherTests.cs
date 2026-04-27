using FluentAssertions;
using TelecomBoliviaNet.Infrastructure.Security;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests de BcryptPasswordHasher.
/// Verifica que la CORRECCIÓN Problema #9 funciona correctamente.
/// </summary>
public class PasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_DebeGenerarHashDiferenteAlTextoOriginal()
    {
        var plain = "MiContraseña123!";
        var hash  = _hasher.Hash(plain);
        hash.Should().NotBe(plain);
    }

    [Fact]
    public void Hash_DosLlamadasMismoPassword_DebenProducirHashesDiferentes()
    {
        // BCrypt usa salt aleatorio por diseño
        var hash1 = _hasher.Hash("password");
        var hash2 = _hasher.Hash("password");
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Verify_PasswordCorrecto_DebeRetornarTrue()
    {
        var plain = "ContraseñaCorrecta!";
        var hash  = _hasher.Hash(plain);
        _hasher.Verify(plain, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_PasswordIncorrecto_DebeRetornarFalse()
    {
        var hash = _hasher.Hash("PasswordOriginal");
        _hasher.Verify("PasswordEquivocado", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_HashMalformado_DebeRetornarFalseNoExcepcion()
    {
        // Garantiza que el try/catch en Verify funciona
        _hasher.Verify("cualquier", "hash_invalido_no_bcrypt").Should().BeFalse();
    }

    [Fact]
    public void Hash_PasswordVacio_DebeHashear()
    {
        // La validación de longitud mínima es responsabilidad del servicio/controller,
        // no del hasher. El hasher solo hashea lo que recibe.
        var hash = _hasher.Hash(string.Empty);
        hash.Should().NotBeNullOrEmpty();
        _hasher.Verify(string.Empty, hash).Should().BeTrue();
    }
}
