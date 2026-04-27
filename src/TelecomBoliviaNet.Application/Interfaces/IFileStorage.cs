namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// Contrato para almacenamiento de archivos (comprobantes, QRs, etc).
/// La implementación local guarda en wwwroot/uploads.
/// En producción puede cambiarse por S3, Azure Blob, etc.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Guarda el archivo y devuelve la URL pública relativa.
    /// Ejemplo: "/uploads/receipts/TBN-0001_20250115_143022.jpg"
    /// El parámetro folder permite separar por tipo (receipts, qr, etc).
    /// </summary>
    Task<string> SaveAsync(Stream stream, string fileName, string folder = "receipts");

    /// <summary>Lee un archivo y devuelve sus bytes y content-type.</summary>
    Task<(byte[] Bytes, string ContentType)> ReadAsync(string relativeUrl);

    /// <summary>Elimina un archivo por su URL relativa. No lanza si no existe.</summary>
    Task DeleteAsync(string relativeUrl);
}
