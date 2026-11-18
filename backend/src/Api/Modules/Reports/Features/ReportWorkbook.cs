using ClosedXML.Excel;

namespace Api.Modules.Reports.Features;

/// <summary>
/// Excel version of the summary report. The workbook is a user-facing document for Argentine businesses,
/// so its sheet names, headings and labels are in Spanish. Amounts are real numbers (not text) so they can be summed.
/// </summary>
internal static class ReportWorkbook
{
    private const string MoneyFormat = "#,##0.00";

    private static readonly Dictionary<string, string> Methods = new()
    {
        ["Cash"] = "Efectivo",
        ["Card"] = "Tarjeta",
        ["Transfer"] = "Transferencia",
        ["Other"] = "Otro",
    };

    private static readonly Dictionary<string, string> Statuses = new()
    {
        ["Scheduled"] = "Programado",
        ["Completed"] = "Completado",
        ["Cancelled"] = "Cancelado",
        ["NoShow"] = "No vino",
    };

    public static byte[] Build(SummaryReportResponse report)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.AddWorksheet("Resumen");
        summary.Cell(1, 1).Value = "Reporte";
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(2, 1).Value = "Desde";
        summary.Cell(2, 2).Value = report.From.ToString("dd/MM/yyyy");
        summary.Cell(3, 1).Value = "Hasta";
        summary.Cell(3, 2).Value = report.To.ToString("dd/MM/yyyy");
        summary.Cell(4, 1).Value = "Moneda";
        summary.Cell(4, 2).Value = report.Currency;
        summary.Cell(5, 1).Value = "Cobrado (neto de anulaciones)";
        Money(summary.Cell(5, 2), report.Collected);
        summary.Cell(6, 1).Value = "Valor de turnos completados";
        Money(summary.Cell(6, 2), report.CompletedValue);
        summary.Columns().AdjustToContents();

        Table(workbook, "Cobrado por medio", ["Medio de pago", "Total"],
            report.ByMethod.Select(m => new object[] { Methods.GetValueOrDefault(m.Method, m.Method), m.Total }));
        Table(workbook, "Turnos por estado", ["Estado", "Turnos"],
            report.AppointmentsByStatus.Select(s => new object[] { Statuses.GetValueOrDefault(s.Status, s.Status), s.Count }));
        Table(workbook, "Por profesional", ["Profesional", "Turnos completados", "Valor"],
            report.ByProfessional.Select(p => new object[] { p.Name, p.Count, p.Value }));
        Table(workbook, "Por servicio", ["Servicio", "Turnos completados", "Valor"],
            report.ByService.Select(s => new object[] { s.Name, s.Count, s.Value }));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Table(XLWorkbook workbook, string name, string[] headers, IEnumerable<object[]> rows)
    {
        var sheet = workbook.AddWorksheet(name);
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
            sheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                switch (row[c])
                {
                    case decimal amount:
                        Money(cell, amount);
                        break;
                    case int count:
                        cell.Value = count;
                        break;
                    default:
                        cell.Value = row[c].ToString();
                        break;
                }
            }

            r++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void Money(IXLCell cell, decimal amount)
    {
        cell.Value = amount;
        cell.Style.NumberFormat.Format = MoneyFormat;
    }
}
