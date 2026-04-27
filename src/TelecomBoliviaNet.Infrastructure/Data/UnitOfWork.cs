using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Infrastructure.Data;

/// <summary>
/// Implementación de IUnitOfWork sobre AppDbContext + EF Core.
/// Envuelve BeginTransactionAsync / CommitAsync / RollbackAsync sobre la conexión de la BD.
/// Registrado como Scoped en DI — mismo scope que AppDbContext y los repositorios.
///
/// CORRECCIÓN (Bug #1 de segundo review):
/// CommitAsync NO llama SaveChangesAsync porque GenericRepository.AddAsync/UpdateAsync
/// ya lo hacen internamente. Llamarlo aquí producía un segundo SaveChanges vacío sobre
/// la misma transacción, lo cual es inofensivo pero confuso y puede causar problemas
/// si EF Core detecta entidades en estado inesperado.
/// La responsabilidad de CommitAsync es solo confirmar la transacción de BD.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext      _context;
    private IDbContextTransaction?     _transaction;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        // BUG FIX: si ya existe una transacción activa, participar en ella en lugar de lanzar.
        // Esto permite que servicios anidados llamen BeginTransactionAsync sin romper el flujo.
        if (_transaction is not null)
            return;
        _transaction = await _context.Database.BeginTransactionAsync(ct);
    }

    /// <summary>
    /// Confirma la transacción activa.
    /// NO llama SaveChangesAsync — los repositorios ya lo hacen en cada operación.
    /// </summary>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No hay transacción activa para confirmar.");
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }
}
