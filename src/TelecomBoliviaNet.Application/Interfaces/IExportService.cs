using TelecomBoliviaNet.Application.DTOs.Invoices;

namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// Contrato para generación de archivos Excel y PDF exportables.
/// La implementación concreta reside en Infrastructure (usa ClosedXML y QuestPDF).
/// </summary>
public interface IExportService
{
    /// <summary>Exporta el listado mensual de facturas a Excel (.xlsx).</summary>
    byte[] ExportInvoicesToExcel(IEnumerable<InvoiceLegacyListItemDto> invoices, InvoiceMonthStatsDto stats);

    /// <summary>Exporta el listado mensual de facturas a PDF.</summary>
    byte[] ExportInvoicesToPdf(IEnumerable<InvoiceLegacyListItemDto> invoices, InvoiceMonthStatsDto stats);

    /// <summary>Exporta el reporte anual de pagos a Excel (.xlsx).</summary>
    byte[] ExportAnnualReportToExcel(AnnualReportExportDto report);

    /// <summary>Exporta el reporte anual de pagos a PDF.</summary>
    byte[] ExportAnnualReportToPdf(AnnualReportExportDto report);
}
