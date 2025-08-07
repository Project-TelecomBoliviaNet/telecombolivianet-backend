namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// Abstracción para generación de números correlativos de factura.
/// Permite mockear en tests unitarios sin depender de PostgreSQL sequences.
/// </summary>
public interface IInvoiceNumberService
{
    Task<string> NextInvoiceNumberAsync(bool isExtraordinary = false);
}
