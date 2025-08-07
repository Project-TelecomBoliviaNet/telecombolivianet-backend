using TelecomBoliviaNet.Application.DTOs.Invoices;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface IBillingJob
{
    /// <summary>
    /// Genera facturas de mensualidad para todos los clientes activos/suspendidos
    /// del mes y año indicados. Omite los que ya tienen factura ese mes.
    /// Aplica crédito a favor y asigna número correlativo.
    /// </summary>
    Task<BillingJobResultDto> GenerateMonthlyInvoicesAsync(int year, int month);

    /// <summary>
    /// Marca como Vencidas todas las facturas Pendientes cuya DueDate ya pasó
    /// y envía notificación WhatsApp por cliente.
    /// </summary>
    Task<int> MarkOverdueInvoicesAsync();
}
