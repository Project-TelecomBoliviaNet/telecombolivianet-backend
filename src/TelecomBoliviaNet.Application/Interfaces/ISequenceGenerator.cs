namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// CORRECCIÓN Bug #3: Abstracción para generación de números correlativos.
/// La implementación en Infrastructure usa secuencias nativas de PostgreSQL,
/// garantizando atomicidad incluso en entornos multi-instancia.
/// </summary>
public interface ISequenceGenerator
{
    /// <summary>
    /// Genera el siguiente número de factura. F-AAAA-NNNN o FE-AAAA-NNNN.
    /// Atómico a nivel de BD — seguro en múltiples instancias del backend.
    /// </summary>
    Task<string> NextInvoiceNumberAsync(bool isExtraordinary = false);

    /// <summary>Genera el siguiente número de ticket. TK-AAAA-NNNN.</summary>
    Task<string> NextTicketNumberAsync();

    /// <summary>Genera el siguiente número de recibo. REC-AAAA-NNNN.</summary>
    Task<string> NextReceiptNumberAsync();
}
