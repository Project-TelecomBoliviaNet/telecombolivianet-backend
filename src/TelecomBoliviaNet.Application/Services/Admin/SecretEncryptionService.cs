using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TelecomBoliviaNet.Application.Services.Admin;

/// <summary>
/// BUG F FIX: Cifrado AES-256-GCM para secrets almacenados en BD (WhatsApp token, etc).
///
/// Por qué AES-256-GCM:
///  - Authenticated encryption: detecta cualquier tampering del ciphertext (AEAD).
///  - Nonce aleatorio de 12 bytes por cada cifrado → mismo plaintext → ciphertexts distintos.
///  - GCM es el estándar recomendado por NIST para cifrado simétrico autenticado.
///
/// La clave maestra (`SECRET_ENCRYPTION_KEY`) se lee exclusivamente de variables de
/// entorno — NUNCA de la BD ni de appsettings, para separar los datos cifrados de
/// la clave que los protege.
///
/// Formato de valores cifrados en BD: "ENC:v1:{base64(nonce+tag+ciphertext)}"
/// El prefijo "ENC:v1:" permite detectar si un valor ya está cifrado y facilita
/// la migración de versiones de cifrado en el futuro.
/// </summary>
public class SecretEncryptionService
{
    private readonly byte[]                          _key;
    private readonly ILogger<SecretEncryptionService> _logger;

    private const string Prefix       = "ENC:v1:";
    private const int    NonceSize    = 12;   // AES-GCM nonce: 12 bytes (96 bits) — estándar NIST
    private const int    TagSize      = 16;   // Authentication tag: 16 bytes (128 bits)

    public SecretEncryptionService(
        IConfiguration                   config,
        ILogger<SecretEncryptionService> logger)
    {
        _logger = logger;

        var rawKey = config["SecretEncryption:Key"]
            ?? throw new InvalidOperationException(
                "SECRET_ENCRYPTION_KEY no está configurado. " +
                "Generar con: openssl rand -base64 32 " +
                "y configurar en la variable de entorno SecretEncryption__Key.");

        // Derivar exactamente 32 bytes (256 bits) de la clave raw usando SHA-256
        // para tolerar claves de cualquier longitud sin truncar ni rellenar manualmente.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
    }

    /// <summary>
    /// Cifra un valor con AES-256-GCM. Si ya está cifrado (prefijo ENC:v1:), lo retorna tal cual.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (plaintext.StartsWith(Prefix))    return plaintext; // ya cifrado

        var nonce      = new byte[NonceSize];
        var tag        = new byte[TagSize];
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext     = new byte[plaintextBytes.Length];

        RandomNumberGenerator.Fill(nonce); // nonce criptográficamente aleatorio por cada cifrado

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Concatenar nonce + tag + ciphertext en un único blob base64
        var blob = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce,      0, blob, 0,                         NonceSize);
        Buffer.BlockCopy(tag,        0, blob, NonceSize,                 TagSize);
        Buffer.BlockCopy(ciphertext, 0, blob, NonceSize + TagSize,       ciphertext.Length);

        return Prefix + Convert.ToBase64String(blob);
    }

    /// <summary>
    /// Descifra un valor cifrado con Encrypt(). Si no tiene el prefijo ENC:v1:, lo retorna tal cual
    /// (compatibilidad con valores legacy no cifrados que puedan existir en BD).
    /// </summary>
    public string Decrypt(string cipherValue)
    {
        if (string.IsNullOrEmpty(cipherValue))         return cipherValue;
        if (!cipherValue.StartsWith(Prefix))           return cipherValue; // valor legacy no cifrado

        try
        {
            var blob       = Convert.FromBase64String(cipherValue[Prefix.Length..]);
            var nonce      = blob[..NonceSize];
            var tag        = blob[NonceSize..(NonceSize + TagSize)];
            var ciphertext = blob[(NonceSize + TagSize)..];
            var plaintext  = new byte[ciphertext.Length];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error descifrando secret de BD. " +
                "Posible corrupción de datos o clave incorrecta.");
            // Retornar cadena vacía en vez de propagar — el caller mostrará campo vacío
            // y el admin podrá re-ingresar el valor correcto.
            return string.Empty;
        }
    }

    /// <summary>Indica si un valor almacenado en BD está cifrado.</summary>
    public static bool IsEncrypted(string value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(Prefix);
}
