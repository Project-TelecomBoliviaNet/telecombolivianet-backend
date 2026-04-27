using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Tickets;

/// <summary>
/// CORRECCIÓN Bug #3: Delegado a ISequenceGenerator (PostgreSQL nativo).
/// Ya no usa SemaphoreSlim ni la tabla TicketSequences.
/// </summary>
public class TicketNumberService
{
    private readonly ISequenceGenerator _seq;

    public TicketNumberService(ISequenceGenerator seq) => _seq = seq;

    public Task<string> NextAsync() => _seq.NextTicketNumberAsync();
}
