using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Cuentitas.Core.Domain;

namespace Cuentitas.Core.Services;

/// <summary>
/// Exporta movimientos a Excel (.xlsx) y CSV. Los montos se convierten a número con decimales sólo acá,
/// para que Excel pueda sumarlos (en la base siguen siendo centavos, ADR-003).
/// </summary>
public sealed class ExportService(TransactionService transactions, AccountService accounts, ReportService reports)
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    private static readonly string[] Headers =
    [
        "Fecha", "Tipo", "Cuenta", "Moneda", "Categoría", "Necesidad", "Monto",
        "Hacia", "Monto recibido", "Moneda recibida", "Tipo de dólar", "Nota", "Cuota", "Vencimiento que paga",
    ];

    /// <summary>Movimientos entre dos fechas (inclusive), del más viejo al más nuevo.</summary>
    private async Task<List<Transaction>> LoadAsync(DateTime from, DateTime to) =>
        (await transactions.QueryAsync(new TxFilter { From = from, To = to }))
        .OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();

    private static object?[] Row(Transaction t) =>
    [
        t.Date.Date,
        t.Type switch { TxType.Income => "Ingreso", TxType.Expense => "Gasto", _ => "Transferencia" },
        t.Account?.Name,
        t.Account?.Currency.ToString(),
        t.Category?.Name,
        t.Necessity switch { Necessity.Necessary => "Necesario", Necessity.Optional => "Prescindible", _ => "" },
        // Con signo: los gastos y lo que sale en transferencias, negativos.
        (t.Type == TxType.Income ? t.Amount : -t.Amount) / 100m,
        t.ToAccount?.Name,
        t.ToAmount is { } to && t.Type == TxType.Transfer ? to / 100m : null,
        t.ToAccount?.Currency.ToString(),
        t.DollarType?.ToString(),
        t.Note,
        t.InstallmentNumber is { } n ? $"{n}/{t.InstallmentCount}" : "",
        t.RecurringItem is { } r && t.DueDate is { } due ? $"{r.Name} ({due:dd/MM/yyyy})" : "",
    ];

    public async Task ExportExcelAsync(string path, DateTime from, DateTime to, DateTime today)
    {
        var list = await LoadAsync(from, to);
        using var book = new XLWorkbook();

        // 1. Movimientos
        var sheet = book.AddWorksheet("Movimientos");
        for (var c = 0; c < Headers.Length; c++) sheet.Cell(1, c + 1).Value = Headers[c];
        for (var i = 0; i < list.Count; i++)
        {
            var values = Row(list[i]);
            for (var c = 0; c < values.Length; c++) sheet.Cell(i + 2, c + 1).Value = XLCellValue.FromObject(values[c]);
        }
        sheet.Column(1).Style.DateFormat.Format = "dd/mm/yyyy";
        sheet.Column(7).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(9).Style.NumberFormat.Format = "#,##0.00";
        StyleTable(sheet, list.Count + 1, Headers.Length);

        // 2. Resumen mensual (pesos)
        var summary = book.AddWorksheet("Resumen mensual");
        string[] sh = ["Mes", "Ingresos", "Gastos", "Necesarios", "Prescindibles", "Ahorro", "Patrimonio a fin de mes"];
        for (var c = 0; c < sh.Length; c++) summary.Cell(1, c + 1).Value = sh[c];
        var series = await reports.GetMonthlySeriesAsync(today, 12);
        for (var i = 0; i < series.Count; i++)
        {
            var p = series[i];
            summary.Cell(i + 2, 1).Value = p.Month.ToString("MMMM yyyy", Ar);
            summary.Cell(i + 2, 2).Value = p.Income / 100m;
            summary.Cell(i + 2, 3).Value = p.Expense / 100m;
            summary.Cell(i + 2, 4).Value = p.Necessary / 100m;
            summary.Cell(i + 2, 5).Value = p.Optional / 100m;
            summary.Cell(i + 2, 6).Value = p.Net / 100m;
            summary.Cell(i + 2, 7).Value = p.NetWorth / 100m;
        }
        summary.Range(2, 2, series.Count + 1, 7).Style.NumberFormat.Format = "#,##0.00";
        StyleTable(summary, series.Count + 1, sh.Length);

        // 3. Cuentas
        var acc = book.AddWorksheet("Cuentas");
        string[] ah = ["Cuenta", "Tipo", "Moneda", "Saldo a hoy", "Cuotas y pagos futuros", "Archivada"];
        for (var c = 0; c < ah.Length; c++) acc.Cell(1, c + 1).Value = ah[c];
        var balances = await accounts.GetAllWithBalancesAsync(includeArchived: true, asOf: today);
        for (var i = 0; i < balances.Count; i++)
        {
            var b = balances[i];
            acc.Cell(i + 2, 1).Value = b.Account.Name;
            acc.Cell(i + 2, 2).Value = b.Account.Type.ToString();
            acc.Cell(i + 2, 3).Value = b.Account.Currency.ToString();
            acc.Cell(i + 2, 4).Value = b.Balance / 100m;
            acc.Cell(i + 2, 5).Value = b.Future / 100m;
            acc.Cell(i + 2, 6).Value = b.Account.Archived ? "Sí" : "No";
        }
        acc.Range(2, 4, balances.Count + 1, 5).Style.NumberFormat.Format = "#,##0.00";
        StyleTable(acc, balances.Count + 1, ah.Length);

        book.SaveAs(path);
    }

    /// <summary>CSV para Excel en español: separador ";", coma decimal, UTF-8 con BOM.</summary>
    public async Task ExportCsvAsync(string path, DateTime from, DateTime to)
    {
        var list = await LoadAsync(from, to);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', Headers.Select(Escape)));
        foreach (var t in list)
        {
            sb.AppendLine(string.Join(';', Row(t).Select(v => v switch
            {
                null => "",
                DateTime d => d.ToString("dd/MM/yyyy", Ar),
                decimal m => m.ToString("0.00", Ar),
                _ => Escape(v.ToString() ?? ""),
            })));
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    internal static string Escape(string s) =>
        s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    private static void StyleTable(IXLWorksheet sheet, int lastRow, int lastColumn)
    {
        var header = sheet.Range(1, 1, 1, lastColumn);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F9D8A");
        header.Style.Font.FontColor = XLColor.White;
        sheet.SheetView.FreezeRows(1);
        if (lastRow > 1) sheet.Range(1, 1, lastRow, lastColumn).SetAutoFilter();
        sheet.Columns(1, lastColumn).AdjustToContents(1, Math.Min(lastRow, 500));
    }
}
