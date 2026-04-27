using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Repositories;

public class GenericRepository<T> : IGenericRepository<T> where T : Entity
{
    private readonly AppDbContext _context;
    private readonly DbSet<T> _set;

    public GenericRepository(AppDbContext context)
    {
        _context = context;
        _set     = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id)
        => await _set.FindAsync(id);

    public IQueryable<T> GetAll()
        => _set.AsQueryable();

    /// <inheritdoc/>
    public IQueryable<T> GetAllReadOnly()
        // BUG FIX: AsNoTracking evita registrar entidades en el ChangeTracker
        // para queries de solo lectura — reduce memoria y CPU en listados grandes.
        => _set.AsNoTracking().AsQueryable();

    public async Task AddAsync(T entity)
    {
        // BUG FIX: eliminado SaveChangesAsync() automático.
        // AddAsync solo acumula la entidad en el ChangeTracker.
        // El llamador debe llamar SaveChangesAsync() o CommitAsync() explícitamente,
        // lo que permite envolver múltiples AddAsync en una sola transacción atómica.
        await _set.AddAsync(entity);
    }

    /// <inheritdoc/>
    public async Task AddRangeAsync(IEnumerable<T> entities)
    {
        await _set.AddRangeAsync(entities);
        // No llama SaveChangesAsync — debe hacerse explícitamente por el llamador.
    }

    /// <inheritdoc/>
    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// CORRECCIÓN (Fix #1): Evita el SELECT adicional de FindAsync cuando la entidad
    /// ya fue cargada por el llamador (EF Core ya la tiene en el ChangeTracker).
    /// Si la entidad ya está siendo trackeada, actualiza sus valores directamente.
    /// Si no está en el tracker, la adjunta y la marca como Modified.
    /// Esto elimina el N+1 del patrón FindAsync → SetValues, y evita
    /// la KeyNotFoundException espuria en entidades ya trackeadas.
    /// </summary>
    public async Task UpdateAsync(T entity)
    {
        var tracked = _context.ChangeTracker.Entries<T>()
            .FirstOrDefault(e => e.Entity.Id == entity.Id);

        if (tracked is not null)
        {
            // Ya está en el tracker — actualizar valores sin SELECT adicional
            tracked.CurrentValues.SetValues(entity);
            tracked.State = EntityState.Modified;
        }
        else
        {
            // No está trackeada — verificar existencia y adjuntar
            var exists = await _set.AnyAsync(e => e.Id == entity.Id);
            if (!exists)
                throw new KeyNotFoundException(
                    $"{typeof(T).Name} con ID {entity.Id} no encontrado.");

            _context.Entry(entity).State = EntityState.Modified;
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// CORRECCIÓN (Fix #9, #13): Actualiza múltiples entidades en un único SaveChangesAsync.
    /// Usar en lugar de llamar UpdateAsync en loop para operaciones bulk.
    /// </summary>
    public async Task UpdateRangeAsync(IEnumerable<T> entities)
    {
        foreach (var entity in entities)
        {
            var tracked = _context.ChangeTracker.Entries<T>()
                .FirstOrDefault(e => e.Entity.Id == entity.Id);

            if (tracked is not null)
            {
                tracked.CurrentValues.SetValues(entity);
                tracked.State = EntityState.Modified;
            }
            else
            {
                _context.Entry(entity).State = EntityState.Modified;
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<T> entities)
    {
        // BUG FIX: un único RemoveRange + SaveChangesAsync en lugar de N DELETE individuales
        _set.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _set.FindAsync(id);
        if (entity is not null)
        {
            _set.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
        => await _set.FirstOrDefaultAsync(predicate);

    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate)
        => await _set.AnyAsync(predicate);
}
