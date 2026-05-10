namespace TelecomBoliviaNet.Domain.Interfaces;

/// <summary>
/// CORRECCIÓN (Fix #11): Abstracción de Unit of Work para permitir transacciones
/// atómicas en la capa Application sin depender directamente de EF Core o Infrastructure.
///
/// PlanChangeService.AprobarCambioAsync realiza múltiples operaciones (anular factura,
/// crear facturas proporcionales, actualizar cliente, actualizar solicitud, cerrar ticket).
/// Si alguna falla a mitad del proceso, la BD queda en estado inconsistente.
/// IUnitOfWork permite envolver todo en una única transacción de BD.
/// </summary>
public interface IUnitOfWork
{
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
