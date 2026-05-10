namespace TelecomBoliviaNet.Domain.Entities.Tickets;

/// <summary>
/// US-TKT-CORRELATIVO — Secuencia para numeración TK-AAAA-NNNN.
/// Reinicia cada año. Una sola fila (Id=1).
/// </summary>
public class TicketSequence
{
    public int Id        { get; set; } = 1;
    public int Year      { get; set; }
    public int LastValue { get; set; } = 0;
}
