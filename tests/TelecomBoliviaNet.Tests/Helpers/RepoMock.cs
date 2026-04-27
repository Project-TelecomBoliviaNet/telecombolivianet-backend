using System.Linq.Expressions;
using Moq;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Tests.Helpers;

/// <summary>
/// Helper para construir mocks de IGenericRepository&lt;T&gt; con datos en memoria.
/// Evita repetir el mismo boilerplate de setup en cada test.
///
/// Uso:
///   var repo = RepoMock.Of(invoice1, invoice2);
///   // repo.Object es un IGenericRepository&lt;Invoice&gt; con esos datos
/// </summary>
public static class RepoMock
{
    public static Mock<IGenericRepository<T>> Of<T>(params T[] items) where T : Entity
    {
        var list = items.ToList();
        var mock = new Mock<IGenericRepository<T>>();

        // GetAll() devuelve IQueryable sobre la lista en memoria
        mock.Setup(r => r.GetAll())
            .Returns(() => list.AsQueryable());

        // GetAllReadOnly() — mismo comportamiento que GetAll() en tests
        // (AsNoTracking no aplica en memoria; el mock es equivalente)
        mock.Setup(r => r.GetAllReadOnly())
            .Returns(() => list.AsQueryable());

        // GetByIdAsync — busca por Id
        mock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => list.FirstOrDefault(e => e.Id == id));

        // FirstOrDefaultAsync — evalúa el predicado en memoria
        mock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<T, bool>>>()))
            .ReturnsAsync((Expression<Func<T, bool>> pred) =>
                list.AsQueryable().FirstOrDefault(pred));

        // AnyAsync — evalúa el predicado en memoria
        mock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<T, bool>>>()))
            .ReturnsAsync((Expression<Func<T, bool>> pred) =>
                list.AsQueryable().Any(pred));

        // AddAsync — agrega a la lista en memoria
        mock.Setup(r => r.AddAsync(It.IsAny<T>()))
            .Callback<T>(e => list.Add(e))
            .Returns(Task.CompletedTask);

        // AddRangeAsync — agrega múltiples sin commit
        mock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<T>>()))
            .Callback<IEnumerable<T>>(entities => list.AddRange(entities))
            .Returns(Task.CompletedTask);

        // SaveChangesAsync — no-op en tests (ya se agregó en AddRangeAsync)
        mock.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // UpdateAsync — reemplaza el elemento por Id
        mock.Setup(r => r.UpdateAsync(It.IsAny<T>()))
            .Callback<T>(updated =>
            {
                var idx = list.FindIndex(e => e.Id == updated.Id);
                if (idx >= 0) list[idx] = updated;
            })
            .Returns(Task.CompletedTask);

        // BUG #4 FIX: UpdateRangeAsync faltaba — tests que llaman MarkOverdueInvoicesAsync
        // o RevokeAllForUserAsync fallaban con NotSupportedException (método no mockeado).
        mock.Setup(r => r.UpdateRangeAsync(It.IsAny<IEnumerable<T>>()))
            .Callback<IEnumerable<T>>(updates =>
            {
                foreach (var updated in updates)
                {
                    var idx = list.FindIndex(e => e.Id == updated.Id);
                    if (idx >= 0) list[idx] = updated;
                }
            })
            .Returns(Task.CompletedTask);

        // DeleteRangeAsync — elimina múltiples entidades
        mock.Setup(r => r.DeleteRangeAsync(It.IsAny<IEnumerable<T>>()))
            .Callback<IEnumerable<T>>(entities =>
            {
                foreach (var e in entities.ToList())
                    list.Remove(e);
            })
            .Returns(Task.CompletedTask);

        // DeleteAsync — elimina por Id
        mock.Setup(r => r.DeleteAsync(It.IsAny<Guid>()))
            .Callback<Guid>(id =>
            {
                var item = list.FirstOrDefault(e => e.Id == id);
                if (item != null) list.Remove(item);
            })
            .Returns(Task.CompletedTask);

        return mock;
    }

    /// <summary>Repo vacío — útil para repos de entidades que no necesitan datos iniciales.</summary>
    public static Mock<IGenericRepository<T>> Empty<T>() where T : Entity
        => Of<T>();
}
