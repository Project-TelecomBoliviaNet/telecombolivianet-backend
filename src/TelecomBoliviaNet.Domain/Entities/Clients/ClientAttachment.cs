using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Clients;

/// <summary>
/// US-CLI-ADJUNTOS — Documentos y archivos adjuntos de un cliente.
/// Tipos admitidos: CI, Contrato, Foto, Comprobante, Otro.
/// El archivo se almacena en IFileStorage y aquí guardamos la URL.
/// </summary>
public class ClientAttachment : Entity
{
    public Guid        ClientId      { get; set; }
    public Client?     Client        { get; set; }

    public string      FileName      { get; set; } = string.Empty;   // nombre original
    public string      StoragePath   { get; set; } = string.Empty;   // ruta en IFileStorage
    public string      ContentType   { get; set; } = string.Empty;   // image/jpeg, application/pdf …
    public long        FileSizeBytes { get; set; }
    public string      TipoDoc       { get; set; } = "Otro";         // CI | Contrato | Foto | Comprobante | Otro

    public string?     Descripcion   { get; set; }

    public Guid        SubidoPorId   { get; set; }
    public UserSystem? SubidoPor     { get; set; }
    public DateTime    SubidoAt      { get; set; } = DateTime.UtcNow;

    public bool        IsDeleted     { get; set; } = false;
    public DateTime?   DeletedAt     { get; set; }
    public Guid?       DeletedById   { get; set; }
}

public static class TipoDocumento
{
    public const string CI          = "CI";
    public const string Contrato    = "Contrato";
    public const string Foto        = "Foto";
    public const string Comprobante = "Comprobante";
    public const string Otro        = "Otro";

    public static readonly string[] Todos = [CI, Contrato, Foto, Comprobante, Otro];
}
