namespace TelecomBoliviaNet.Domain.Entities.Clients;

/// <summary>
/// Secuencia correlativa para la generación del código TBN.
/// Solo existe un registro en esta tabla (id=1).
/// Se incrementa en 1 con cada cliente nuevo y nunca se reutiliza.
/// </summary>
public class TbnSequence
{
    public int Id           { get; set; } = 1;
    public int LastValue    { get; set; } = 0;
    public string Prefix    { get; set; } = "TBN";
}

/// <summary>
/// US-FAC-CORRELATIVO — Secuencia para facturas F-AAAA-NNNN (reinicia por año).
/// US-PAG-RECIBO — Secuencia para recibos REC-AAAA-NNNN.
/// US-FAC-02 — Secuencia para facturas extraordinarias FE-AAAA-NNNN.
/// </summary>
public class InvoiceSequence
{
    public int Id        { get; set; } = 1;  // siempre id=1, reinicia LastValue en año nuevo
    public int Year      { get; set; }
    public int LastValue { get; set; } = 0;
}

public class ReceiptSequence
{
    public int Id        { get; set; } = 1;
    public int Year      { get; set; }
    public int LastValue { get; set; } = 0;
}
