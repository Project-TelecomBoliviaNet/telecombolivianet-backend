namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// CORRECCIÓN Problema #9: abstracción del hashing de contraseñas.
///
/// ANTES: BCrypt.Net.BCrypt.HashPassword() se llamaba directamente en
/// UserSystemService (Application layer), acoplando la capa de aplicación
/// a un detalle de infraestructura (librería de hashing).
///
/// AHORA: interfaz en Application, implementación en Infrastructure.
/// Permite cambiar el algoritmo (BCrypt → Argon2) sin tocar servicios.
/// Facilita el testing con implementaciones mock.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string plainPassword);
    bool   Verify(string plainPassword, string hash);
}
