using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TelecomBoliviaNet.Application.DTOs.Invoices;
using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Infrastructure.Services;

public class ExportService : IExportService
{
    static ExportService()
    {
        // Licencia community de QuestPDF (gratis para proyectos no comerciales)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ── Facturas mensuales — Excel (estilo ISP comercial) ────────────────────

    public byte[] ExportInvoicesToExcel(
        IEnumerable<InvoiceLegacyListItemDto> invoices,
        InvoiceMonthStatsDto stats)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Facturas");

        var list = invoices.ToList();

        // ── Bloque de encabezado de empresa ──────────────────────────────────
        ws.Cell(1, 1).Value = "TELECOMBOLIVIANET";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#1D4ED8");

        ws.Cell(2, 1).Value = "Proveedor de Servicios de Internet — Cochabamba, Bolivia";
        ws.Cell(2, 1).Style.Font.FontSize = 9;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#6B7280");

        ws.Cell(3, 1).Value = $"Reporte de Facturación — {MonthName(stats.Month)} {stats.Year}";
        ws.Cell(3, 1).Style.Font.Bold = true;
        ws.Cell(3, 1).Style.Font.FontSize = 12;

        ws.Cell(4, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Cell(4, 1).Style.Font.FontSize = 8;
        ws.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml("#9CA3AF");

        ws.Range(1, 1, 1, 9).Merge();
        ws.Range(2, 1, 2, 9).Merge();
        ws.Range(3, 1, 3, 9).Merge();
        ws.Range(4, 1, 4, 9).Merge();

        // ── Métricas clave ────────────────────────────────────────────────────
        int mRow = 6;
        var metrics = new (string Label, string Value, string Color)[]
        {
            ("Total facturado",   $"Bs. {stats.TotalBilled:N2}",    "#1D4ED8"),
            ("Total cobrado",     $"Bs. {stats.TotalCollected:N2}", "#16A34A"),
            ("Pendiente/Vencido", $"Bs. {stats.TotalPending:N2}",  "#D97706"),
            ("Cobranza",          $"{stats.CollectionRate}%",        "#7C3AED"),
        };
        for (int i = 0; i < metrics.Length; i++)
        {
            int col = i * 2 + 1;
            ws.Cell(mRow, col).Value = metrics[i].Label;
            ws.Cell(mRow, col).Style.Font.FontSize = 8;
            ws.Cell(mRow, col).Style.Font.FontColor = XLColor.FromHtml("#6B7280");
            ws.Cell(mRow + 1, col).Value = metrics[i].Value;
            ws.Cell(mRow + 1, col).Style.Font.Bold = true;
            ws.Cell(mRow + 1, col).Style.Font.FontSize = 11;
            ws.Cell(mRow + 1, col).Style.Font.FontColor = XLColor.FromHtml(metrics[i].Color);
        }

        // Línea separadora
        ws.Range(mRow + 2, 1, mRow + 2, 9).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Range(mRow + 2, 1, mRow + 2, 9).Style.Border.BottomBorderColor = XLColor.FromHtml("#E5E7EB");

        // ── Cabecera de tabla ─────────────────────────────────────────────────
        int row = mRow + 4;
        string[] headers = ["Código TBN", "Cliente", "Plan", "Tipo", "Estado", "Monto (Bs.)", "Notas", "Emisión", "Vencimiento"];
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 9;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        ws.Row(row).Height = 20;

        // ── Filas de datos ────────────────────────────────────────────────────
        bool isAlt = false;
        foreach (var inv in list)
        {
            row++;
            var rowBg = isAlt ? XLColor.FromHtml("#F9FAFB") : XLColor.White;
            isAlt = !isAlt;

            var statusBg = inv.Status switch
            {
                "Pagada"    => XLColor.FromHtml("#DCFCE7"),
                "Vencida"   => XLColor.FromHtml("#FEE2E2"),
                "Pendiente" => XLColor.FromHtml("#FEF9C3"),
                "Anulada"   => XLColor.FromHtml("#F3F4F6"),
                _           => XLColor.White
            };

            ws.Cell(row, 1).Value = inv.TbnCode;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#1D4ED8");

            ws.Cell(row, 2).Value = inv.ClientName;
            ws.Cell(row, 3).Value = inv.PlanName;
            ws.Cell(row, 4).Value = inv.Type == "Mensualidad" ? "Mensualidad" : "Instalación";
            ws.Cell(row, 5).Value = inv.Status;
            ws.Cell(row, 5).Style.Fill.BackgroundColor = statusBg;
            ws.Cell(row, 5).Style.Font.Bold = true;
            ws.Cell(row, 6).Value = inv.Amount;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 7).Value = inv.Notes ?? "";
            ws.Cell(row, 7).Style.Font.FontColor = XLColor.FromHtml("#6B7280");
            ws.Cell(row, 8).Value = inv.IssuedAt.ToString("dd/MM/yyyy");
            ws.Cell(row, 9).Value = inv.DueDate.ToString("dd/MM/yyyy");

            // Fondo alternado en columnas que no tienen color de estado
            foreach (int col in new[] { 1, 2, 3, 4, 6, 7, 8, 9 })
                ws.Cell(row, col).Style.Fill.BackgroundColor = rowBg;

            // Bordes inferiores
            ws.Range(row, 1, row, 9).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ws.Range(row, 1, row, 9).Style.Border.BottomBorderColor = XLColor.FromHtml("#E5E7EB");
        }

        // ── Totales ───────────────────────────────────────────────────────────
        row += 2;
        ws.Cell(row, 5).Value = "TOTAL FACTURADO:";
        ws.Cell(row, 5).Style.Font.Bold = true;
        ws.Cell(row, 5).Style.Font.FontColor = XLColor.FromHtml("#1D4ED8");
        ws.Cell(row, 6).Value = stats.TotalBilled;
        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(row, 6).Style.Font.Bold = true;
        ws.Cell(row, 6).Style.Font.FontColor = XLColor.FromHtml("#1D4ED8");

        row++;
        ws.Cell(row, 5).Value = "TOTAL COBRADO:";
        ws.Cell(row, 5).Style.Font.Bold = true;
        ws.Cell(row, 5).Style.Font.FontColor = XLColor.FromHtml("#16A34A");
        ws.Cell(row, 6).Value = stats.TotalCollected;
        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(row, 6).Style.Font.Bold = true;
        ws.Cell(row, 6).Style.Font.FontColor = XLColor.FromHtml("#16A34A");

        ws.Columns().AdjustToContents();
        // Ancho mínimo para columna de cliente
        if (ws.Column(2).Width < 25) ws.Column(2).Width = 25;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Facturas mensuales — PDF (estilo ISP comercial) ──────────────────────

    public byte[] ExportInvoicesToPdf(
        IEnumerable<InvoiceLegacyListItemDto> invoices,
        InvoiceMonthStatsDto stats)
    {
        var list      = invoices.ToList();
        var pagadas   = list.Count(i => i.Status == "Pagada");
        var pendientes = list.Count(i => i.Status == "Pendiente");
        var vencidas  = list.Count(i => i.Status == "Vencida");

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontSize(9).FontFamily("Arial"));

                // ── Encabezado profesional ───────────────────────────────────
                page.Header().Column(col =>
                {
                    // Banda de color superior
                    col.Item().Background("#1D4ED8").Padding(10).Row(row =>
                    {
                        row.RelativeItem().Column(inner =>
                        {
                            inner.Item().Text("TelecomBoliviaNet")
                                .Bold().FontSize(16).FontColor("#FFFFFF");
                            inner.Item().Text("Proveedor de Servicios de Internet — Cochabamba, Bolivia")
                                .FontSize(8).FontColor("#BFDBFE");
                        });
                        row.ConstantItem(200).AlignRight().Column(inner =>
                        {
                            inner.Item().Text($"REPORTE DE FACTURACIÓN")
                                .Bold().FontSize(11).FontColor("#FFFFFF").AlignRight();
                            inner.Item().Text($"{MonthName(stats.Month)} {stats.Year}")
                                .FontSize(10).FontColor("#BFDBFE").AlignRight();
                            inner.Item().Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(7).FontColor("#93C5FD").AlignRight();
                        });
                    });

                    // Fila de resumen de métricas
                    col.Item().Background("#F8FAFC").BorderBottom(1).BorderColor("#E2E8F0")
                        .Padding(8).Row(row =>
                    {
                        void Metric(string label, string value, string color)
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(label).FontSize(7).FontColor("#6B7280");
                                c.Item().Text(value).FontSize(11).Bold().FontColor(color);
                            });
                        }
                        Metric("Total facturado",  $"Bs. {stats.TotalBilled:N2}",     "#1D4ED8");
                        Metric("Total cobrado",    $"Bs. {stats.TotalCollected:N2}",  "#16A34A");
                        Metric("Pendiente/Vencido",$"Bs. {stats.TotalPending:N2}",   "#D97706");
                        Metric("Tasa de cobranza", $"{stats.CollectionRate}%",         "#7C3AED");
                        Metric("Pagadas",          $"{pagadas} facturas",              "#16A34A");
                        Metric("Pendientes",       $"{pendientes} facturas",           "#D97706");
                        Metric("Vencidas",         $"{vencidas} facturas",             "#DC2626");
                    });
                });

                // ── Tabla de facturas ────────────────────────────────────────
                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(55);  // TBN
                        cols.RelativeColumn(3);   // Cliente
                        cols.RelativeColumn(2);   // Plan
                        cols.ConstantColumn(68);  // Estado
                        cols.ConstantColumn(75);  // Monto
                        cols.ConstantColumn(65);  // Tipo
                        cols.ConstantColumn(62);  // Emisión
                        cols.ConstantColumn(62);  // Vencimiento
                    });

                    static IContainer HeaderCell(IContainer c) =>
                        c.Background("#1E3A8A").Padding(5);

                    table.Header(h =>
                    {
                        foreach (var label in new[] { "Código", "Cliente", "Plan", "Estado", "Monto (Bs.)", "Tipo", "Emisión", "Vencimiento" })
                            h.Cell().Element(HeaderCell).Text(label).FontColor("#FFFFFF").Bold().FontSize(8);
                    });

                    bool isAlt = false;
                    foreach (var inv in list)
                    {
                        var statusBg = inv.Status switch
                        {
                            "Pagada"    => "#DCFCE7",
                            "Vencida"   => "#FEE2E2",
                            "Pendiente" => "#FEF9C3",
                            "Anulada"   => "#F3F4F6",
                            _           => "#FFFFFF"
                        };
                        var rowBg = isAlt ? "#F9FAFB" : "#FFFFFF";
                        isAlt = !isAlt;

                        IContainer DataCell(IContainer c) =>
                            c.Background(rowBg).BorderBottom(1).BorderColor("#E5E7EB").Padding(4);

                        table.Cell().Element(DataCell)
                            .Text(inv.TbnCode).FontSize(8).Bold().FontColor("#1D4ED8");
                        table.Cell().Element(DataCell)
                            .Text(inv.ClientName).FontSize(8);
                        table.Cell().Element(DataCell)
                            .Text(inv.PlanName).FontSize(8).FontColor("#4B5563");
                        table.Cell().Background(statusBg).BorderBottom(1).BorderColor("#E5E7EB").Padding(4)
                            .Text(inv.Status).FontSize(8).Bold();
                        table.Cell().Element(DataCell)
                            .Text($"Bs. {inv.Amount:N2}").FontSize(8).Bold();
                        table.Cell().Element(DataCell)
                            .Text(inv.Type == "Mensualidad" ? "Mensualidad" : "Instalación").FontSize(8).FontColor("#6B7280");
                        table.Cell().Element(DataCell)
                            .Text(inv.IssuedAt.ToString("dd/MM/yyyy")).FontSize(8).FontColor("#6B7280");
                        table.Cell().Element(DataCell)
                            .Text(inv.DueDate.ToString("dd/MM/yyyy")).FontSize(8).FontColor("#6B7280");
                    }
                });

                // ── Pie de página ────────────────────────────────────────────
                page.Footer().BorderTop(1).BorderColor("#E2E8F0").PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text("TelecomBoliviaNet · Cochabamba, Bolivia · Documento generado automáticamente")
                        .FontSize(7).FontColor("#9CA3AF");
                    row.ConstantItem(100).AlignRight().Text(t =>
                    {
                        t.Span("Página ").FontSize(7).FontColor("#9CA3AF");
                        t.CurrentPageNumber().FontSize(7).FontColor("#9CA3AF");
                        t.Span(" de ").FontSize(7).FontColor("#9CA3AF");
                        t.TotalPages().FontSize(7).FontColor("#9CA3AF");
                    });
                });
            });
        }).GeneratePdf();
    }

    // ── Reporte anual — Excel (US-27) ─────────────────────────────────────────

    public byte[] ExportAnnualReportToExcel(AnnualReportExportDto report)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add($"Reporte {report.Year}");

        string[] months = ["Instalación", "Enero", "Febrero", "Marzo", "Abril",
                           "Mayo", "Junio", "Julio", "Agosto", "Septiembre",
                           "Octubre", "Noviembre", "Diciembre"];

        // Cabecera
        int row = 1;
        ws.Cell(row, 1).Value = $"Reporte Anual de Pagos — {report.Year}";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 13;
        ws.Range(row, 1, row, 16).Merge();

        row = 2;
        ws.Cell(row, 1).Value = "TBN";
        ws.Cell(row, 2).Value = "Cliente";
        ws.Cell(row, 3).Value = "Zona";
        ws.Cell(row, 4).Value = "Plan";
        for (int m = 0; m < months.Length; m++)
            ws.Cell(row, 5 + m).Value = months[m];

        ws.Row(row).Style.Font.Bold = true;
        ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
        ws.Row(row).Style.Font.FontColor = XLColor.White;

        // Datos
        foreach (var clientRow in report.Rows)
        {
            row++;
            ws.Cell(row, 1).Value = clientRow.TbnCode;
            ws.Cell(row, 2).Value = clientRow.FullName;
            ws.Cell(row, 3).Value = clientRow.Zone;
            ws.Cell(row, 4).Value = clientRow.PlanName;

            for (int c = 0; c < clientRow.Cells.Count && c < months.Length; c++)
            {
                var cell = clientRow.Cells[c];
                var xlCell = ws.Cell(row, 5 + c);

                xlCell.Value = cell.Status switch
                {
                    "Pagada"    => cell.PaidDate is not null ? $"✓ {cell.PaidDate}" : "✓ Pagada",
                    "Pendiente" => "Pendiente",
                    "Vencida"   => "VENCIDA",
                    "Anulada"   => "Anulada",
                    "NoAplica"  => "—",
                    _           => ""
                };

                xlCell.Style.Fill.BackgroundColor = cell.Status switch
                {
                    "Pagada"    => XLColor.FromHtml("#D1FAE5"),
                    "Vencida"   => XLColor.FromHtml("#FEE2E2"),
                    "Pendiente" => XLColor.FromHtml("#FEF3C7"),
                    "Anulada"   => XLColor.FromHtml("#F3F4F6"),
                    _           => XLColor.FromHtml("#F9FAFB")
                };
                xlCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Reporte anual — PDF (US-27) ───────────────────────────────────────────

    public byte[] ExportAnnualReportToPdf(AnnualReportExportDto report)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A3.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontSize(7).FontFamily("Arial"));

                page.Header()
                    .Text($"TelecomBoliviaNet — Reporte Anual de Pagos {report.Year}  |  " +
                          $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}")
                    .Bold().FontSize(11);

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(45);  // TBN
                        cols.RelativeColumn(2);   // Cliente
                        cols.RelativeColumn(1);   // Zona
                        // 13 meses
                        for (int i = 0; i < 13; i++) cols.RelativeColumn(1);
                    });

                    static IContainer Hdr(IContainer c) =>
                        c.Background("#1E3A8A").Padding(3);

                    table.Header(h =>
                    {
                        h.Cell().Element(Hdr).Text("TBN").FontColor("#FFF").Bold();
                        h.Cell().Element(Hdr).Text("Cliente").FontColor("#FFF").Bold();
                        h.Cell().Element(Hdr).Text("Zona").FontColor("#FFF").Bold();
                        foreach (var m in new[] { "Inst", "Ene", "Feb", "Mar", "Abr", "May",
                                                   "Jun",  "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" })
                            h.Cell().Element(Hdr).Text(m).FontColor("#FFF").Bold().AlignCenter();
                    });

                    foreach (var r in report.Rows)
                    {
                        IContainer DataCell(IContainer c) =>
                            c.BorderBottom(1).BorderColor("#E5E7EB").Padding(2);

                        table.Cell().Element(DataCell).Text(r.TbnCode);
                        table.Cell().Element(DataCell).Text(r.FullName);
                        table.Cell().Element(DataCell).Text(r.Zone);

                        foreach (var cell in r.Cells)
                        {
                            var bg = cell.Status switch
                            {
                                "Pagada"    => "#D1FAE5",
                                "Vencida"   => "#FEE2E2",
                                "Pendiente" => "#FEF3C7",
                                _           => "#F9FAFB"
                            };
                            var txt = cell.Status switch
                            {
                                "Pagada"    => "✓",
                                "Pendiente" => "P",
                                "Vencida"   => "V!",
                                "Anulada"   => "An",
                                _           => "—"
                            };
                            table.Cell().Background(bg)
                                .BorderBottom(1).BorderColor("#E5E7EB")
                                .Padding(2).AlignCenter().Text(txt);
                        }
                    }
                });

                page.Footer().AlignRight()
                    .Text(t =>
                    {
                        t.Span("Página ").FontSize(7);
                        t.CurrentPageNumber().FontSize(7);
                        t.Span(" / ").FontSize(7);
                        t.TotalPages().FontSize(7);
                    });
            });
        }).GeneratePdf();
    }

    private static string MonthName(int m) => m switch
    {
        1 => "Enero", 2 => "Febrero", 3 => "Marzo", 4 => "Abril",
        5 => "Mayo", 6 => "Junio", 7 => "Julio", 8 => "Agosto",
        9 => "Septiembre", 10 => "Octubre", 11 => "Noviembre", 12 => "Diciembre",
        _ => "Instalación"
    };
}
