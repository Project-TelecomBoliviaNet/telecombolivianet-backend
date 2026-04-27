using TelecomBoliviaNet.Domain.Entities.Clients;

namespace TelecomBoliviaNet.Domain.Services;

/// <summary>
/// CORRECCIÓN Problema #8: lógica de dominio para facturas centralizada.
///
/// ANTES: la misma lógica de crédito/estado aparecía triplicada en:
///   - BillingService.GenerateMonthlyInvoicesAsync
///   - PaymentCreditService.RegisterPaymentWithCreditAsync
///   - InvoiceM3Service.AplicarCreditoAsync
///
/// AHORA: un único lugar de verdad en la capa Domain,
/// sin dependencias de infraestructura → fácilmente testeable.
/// </summary>
public static class InvoiceDomainService
{
    // ── Aplicar crédito ───────────────────────────────────────────────────────

    public static (decimal CreditoAplicado, decimal CreditoRestante)
        AplicarCredito(decimal creditoDisponible, decimal montoPendiente)
    {
        if (creditoDisponible <= 0 || montoPendiente <= 0)
            return (0m, creditoDisponible);

        var aplicar = Math.Min(creditoDisponible, montoPendiente);
        return (aplicar, creditoDisponible - aplicar);
    }

    // ── Calcular estado ───────────────────────────────────────────────────────

    public static InvoiceStatus CalcularEstado(decimal amount, decimal amountPaid)
    {
        if (amountPaid <= 0)      return InvoiceStatus.Emitida;
        if (amountPaid >= amount) return InvoiceStatus.Pagada;
        return InvoiceStatus.ParcialmentePagada;
    }

    public static InvoiceStatus EstadoInicialConCredito(decimal amount, decimal creditoAplicado)
    {
        if (creditoAplicado >= amount) return InvoiceStatus.Pagada;
        if (creditoAplicado > 0)       return InvoiceStatus.ParcialmentePagada;
        return InvoiceStatus.Emitida;
    }

    // ── Validar transición de estado ──────────────────────────────────────────

    private static readonly Dictionary<InvoiceStatus, InvoiceStatus[]> _transiciones = new()
    {
        [InvoiceStatus.Emitida]            = [InvoiceStatus.Enviada, InvoiceStatus.Pendiente, InvoiceStatus.Anulada],
        [InvoiceStatus.Enviada]            = [InvoiceStatus.Pendiente, InvoiceStatus.Anulada],
        [InvoiceStatus.Pendiente]          = [InvoiceStatus.Vencida, InvoiceStatus.Anulada],
        [InvoiceStatus.Vencida]            = [InvoiceStatus.Anulada],
        [InvoiceStatus.ParcialmentePagada] = [InvoiceStatus.Pagada, InvoiceStatus.Anulada],
        [InvoiceStatus.Pagada]             = [],
        [InvoiceStatus.Anulada]            = [],
    };

    public static bool TransicionEsValida(InvoiceStatus actual, InvoiceStatus nuevo)
        => _transiciones.TryGetValue(actual, out var permitidas)
        && permitidas.Contains(nuevo);

    public static InvoiceStatus[] GetTransicionesPermitidas(InvoiceStatus actual)
        => _transiciones.TryGetValue(actual, out var p) ? p : [];
}
