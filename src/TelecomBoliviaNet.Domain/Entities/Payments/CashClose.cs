using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Payments;

/// <summary>
/// US-PAG-CAJA — Cierre de turno del operador de cobros.
/// Un operador puede tener un turno activo a la vez.
/// </summary>
public class CashClose : Entity
{
    public Guid        UserId     { get; set; }
    public UserSystem? User       { get; set; }

    public DateTime    StartedAt  { get; set; } = DateTime.UtcNow;
    public DateTime?   ClosedAt   { get; set; }

    public decimal     TotalAmount { get; set; }
    /// <summary>JSON: List&lt;CashCloseChannelDetail&gt;</summary>
    public string      DetailJson  { get; set; } = "[]";

    public int    PagosValidados  { get; set; }
    public int    PagosRechazados { get; set; }
    /// <summary>Ruta relativa del PDF generado, si existe.</summary>
    public string? PdfPath        { get; set; }
}

public record CashCloseChannelDetail(
    string  Method,
    int     Cantidad,
    decimal Monto
);
