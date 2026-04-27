namespace TelecomBoliviaNet.Application.Interfaces;

public interface IBillingJob
{
    /// <summary>
    /// Genera facturas de mensualidad para todos los clientes activos/suspendidos
    /// del mes y año indicados. Omite los que ya tienen factura ese mes.
    /// </summary>
    Task<BillingJobResult> GenerateMonthlyInvoicesAsync(int year, int month);

    /// <summary>
    /// Marca como Vencidas todas las facturas Pendientes cuya DueDate ya pasó.
    /// </summary>
    Task<int> MarkOverdueInvoicesAsync();
}

public record BillingJobResult(
    int Generated,
    int AlreadyExisted,
    int SkippedCancelled,
    int Errors,
    string Summary
);
