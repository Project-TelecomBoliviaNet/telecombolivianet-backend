using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Invoices;

/// <summary>
/// CORRECCIÓN Bug #3: Delegado a ISequenceGenerator (PostgreSQL nativo).
/// Ya no usa SemaphoreSlim ni la tabla InvoiceSequences — esa tabla se mantiene
/// solo para compatibilidad de migraciones previas.
/// </summary>
public class InvoiceNumberService : IInvoiceNumberService
{
    private readonly ISequenceGenerator _seq;

    public InvoiceNumberService(ISequenceGenerator seq) => _seq = seq;

    public Task<string> NextInvoiceNumberAsync(bool isExtraordinary = false)
        => _seq.NextInvoiceNumberAsync(isExtraordinary);
}
