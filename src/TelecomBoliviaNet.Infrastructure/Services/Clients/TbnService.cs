using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Services.Clients;

/// <summary>
/// Genera el próximo código TBN de forma atómica usando una transacción,
/// garantizando que nunca se reutilice un número aunque haya concurrencia.
/// </summary>
public class TbnService : ITbnService
{
    private readonly AppDbContext _context;

    public TbnService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Genera el próximo código TBN (ej: TBN-0042) dentro de una transacción.
    /// Incrementa la secuencia y devuelve el código formateado.
    /// </summary>
    public async Task<string> GenerateNextAsync()
    {
        // Bloqueo pesimista para evitar colisiones en alta concurrencia
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var seq = await _context.TbnSequences
                .FromSqlRaw("SELECT * FROM \"TbnSequences\" WHERE \"Id\" = 1 FOR UPDATE")
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("La secuencia TBN no está inicializada.");

            seq.LastValue++;
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return $"{seq.Prefix}-{seq.LastValue:D4}";
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Devuelve el código que se generaría a continuación (solo lectura, para previsualizar).
    /// </summary>
    public async Task<string> PeekNextAsync()
    {
        var seq = await _context.TbnSequences.FindAsync(1)
            ?? throw new InvalidOperationException("La secuencia TBN no está inicializada.");
        return $"{seq.Prefix}-{(seq.LastValue + 1):D4}";
    }
}
