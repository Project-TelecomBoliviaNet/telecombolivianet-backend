using System.Linq.Expressions;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Interfaces;

public interface IGenericRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(Guid id);
    IQueryable<T> GetAll();
    /// <summary>
    /// BUG FIX: Retorna IQueryable con AsNoTracking para queries de solo lectura
    /// (listados, reportes, dashboards). Evita cargar entidades al ChangeTracker
    /// innecesariamente cuando no se van a modificar.
    /// </summary>
    IQueryable<T> GetAllReadOnly();
    Task AddAsync(T entity);

    /// <summary>
    /// Agrega múltiples entidades sin llamar SaveChangesAsync individualmente.
    /// Debe seguirse de una llamada explícita a SaveChangesAsync() para persistir.
    /// Usar en operaciones bulk (ej: facturación retroactiva) para un solo round-trip a la BD.
    /// </summary>
    Task AddRangeAsync(IEnumerable<T> entities);

    /// <summary>
    /// Persiste todos los cambios pendientes en el contexto EF Core en una única transacción.
    /// Usar en conjunto con AddRangeAsync o UpdateRange para operaciones bulk.
    /// </summary>
    Task SaveChangesAsync();

    Task UpdateAsync(T entity);

    /// <summary>
    /// CORRECCIÓN (Fix #9, #13): Actualiza múltiples entidades en un único round-trip.
    /// Evita el patrón anti-performance de llamar UpdateAsync en loop.
    /// </summary>
    Task UpdateRangeAsync(IEnumerable<T> entities);

    Task DeleteAsync(Guid id);
    /// <summary>
    /// BUG FIX: Elimina múltiples entidades en un único round-trip.
    /// Evita el anti-patrón de llamar DeleteAsync en loop (N queries).
    /// </summary>
    Task DeleteRangeAsync(IEnumerable<T> entities);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate);
}
