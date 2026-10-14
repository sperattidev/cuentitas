using System.Net;
using ClosedXML.Excel;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;

namespace Cuentitas.Core.Tests;

/// <summary>Responde siempre lo mismo (o falla), para no depender de internet.</summary>
internal sealed class FakeHandler(string? body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        if (body is null) throw new HttpRequestException("sin conexión");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}

public class QuoteTests
{
    // Respuesta real de dolarapi.com (recortada).
    internal const string Sample = """
        [
          {"moneda":"USD","casa":"oficial","nombre":"Oficial","compra":1485,"venta":1535,"fechaActualizacion":"2026-10-14T13:00:00.000Z"},
          {"moneda":"USD","casa":"blue","nombre":"Blue","compra":1530,"venta":1550,"fechaActualizacion":"2026-10-14T16:00:00.000Z"},
          {"moneda":"USD","casa":"bolsa","nombre":"Bolsa","compra":1538.3,"venta":1539.6,"fechaActualizacion":"2026-10-14T16:00:00.000Z"},
          {"moneda":"USD","casa":"contadoconliqui","nombre":"CCL","compra":1614.1,"venta":1614.8,"fechaActualizacion":"2026-10-14T16:00:00.000Z"},
          {"moneda":"USD","casa":"cripto","nombre":"Cripto","compra":1611.03,"venta":1614.51,"fechaActualizacion":"2026-10-14T16:00:00.000Z"},
          {"moneda":"USD","casa":"tarjeta","nombre":"Tarjeta","compra":1930.5,"venta":1995.5,"fechaActualizacion":"2026-10-14T13:00:00.000Z"},
          {"moneda":"USD","casa":"blue","nombre":"roto","compra":null,"venta":"x"}
        ]
        """;

    [Fact]
    public void Parse_maps_argentine_dollar_types_and_skips_others()
    {
        var quotes = QuoteService.Parse(Sample);
        Assert.Equal(5, quotes.Count);
        Assert.Equal(1539.6m, quotes.Single(q => q.Type == DollarType.Mep).Sell);
        Assert.Equal(1550m, quotes.Single(q => q.Type == DollarType.Blue).Sell);
        Assert.DoesNotContain(quotes, q => q.Type == DollarType.Otro);
    }

    [Fact]
    public async Task Refresh_caches_and_failure_keeps_last_snapshot()
    {
        await using var db = await TestDb.CreateAsync();
        var ok = new QuoteService(db.Settings, new HttpClient(new FakeHandler(QuoteTests.Sample)), db.Changes);
        Assert.NotNull(await ok.RefreshAsync());

        var offline = new QuoteService(db.Settings, new HttpClient(new FakeHandler(null)), db.Changes);
        Assert.Null(await offline.RefreshAsync());
        var cached = await offline.GetCachedAsync();
        Assert.Equal(1539.6m, cached!.Get(DollarType.Mep)!.Sell);
        Assert.Equal(DollarType.Mep, await offline.GetValuationTypeAsync());

        // Recién descargado: GetFresh no vuelve a pedir.
        var handler = new FakeHandler(QuoteTests.Sample);
        var fresh = new QuoteService(db.Settings, new HttpClient(handler), db.Changes);
        await fresh.GetFreshAsync();
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Goal_rate_prefers_override_then_quote_then_purchases()
    {
        await using var db = await TestDb.CreateAsync();
        var quotes = new QuoteService(db.Settings, new HttpClient(new FakeHandler(QuoteTests.Sample)), db.Changes);
        var goals = new GoalService(db.Factory, db.Accounts, db.Settings, db.Changes, quotes);
        Assert.Null(await goals.GetUsdRateAsync(DateTime.Today));

        await quotes.RefreshAsync();
        await quotes.SetValuationTypeAsync(DollarType.Blue);
        Assert.Equal(1550m, await goals.GetUsdRateAsync(DateTime.Today));

        await goals.SetUsdRateOverrideAsync(1400m);
        Assert.Equal(1400m, await goals.GetUsdRateAsync(DateTime.Today));
    }
}

public class BudgetTests
{
    private static readonly DateTime Today = new(2026, 10, 10); // día 10 de 31

    [Fact]
    public async Task Status_levels_projection_and_suggestion()
    {
        await using var db = await TestDb.CreateAsync();
        var budgets = new BudgetService(db.Factory, db.Changes);
        var account = (await db.Accounts.GetActiveAsync()).First().Id;
        var cats = (await db.Categories.GetAsync()).ToDictionary(c => c.Name);

        async Task Spend(string cat, long amount, DateTime date) => await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = amount, AccountId = account, CategoryId = cats[cat].Id, Date = date });

        await budgets.SetAsync(cats["Delivery"].Id, 50_000_00);
        await budgets.SetAsync(cats["Supermercado"].Id, 400_000_00);
        await budgets.SetAsync(cats["Salidas"].Id, 100_000_00);
        await budgets.SetAsync(cats["Salidas"].Id, 30_000_00); // actualiza, no duplica

        await Spend("Delivery", 60_000_00, Today.AddDays(-2));      // 120 % → pasado
        await Spend("Supermercado", 100_000_00, Today.AddDays(-5)); // 25 %, proyecta 310.000
        await Spend("Salidas", 25_000_00, Today.AddDays(-1));       // 83 % → aviso
        await Spend("Salidas", 99_000_00, Today.AddDays(5));        // futuro: no cuenta

        var status = (await budgets.GetStatusAsync(Today, Today)).ToDictionary(s => s.Category.Name);
        Assert.Equal(3, status.Count);
        Assert.Equal(BudgetLevel.Over, status["Delivery"].Level);
        Assert.Equal(BudgetLevel.Warning, status["Salidas"].Level);
        Assert.Equal(25_000_00, status["Salidas"].Spent);
        Assert.Equal(BudgetLevel.Ok, status["Supermercado"].Level);
        Assert.Equal(310_000_00, status["Supermercado"].ProjectedEndOfMonth);
        Assert.Equal(2, (await budgets.GetAlertsAsync(Today)).Count);

        // Sugerencia: promedio de los meses completos anteriores, redondeado a $ 1.000 hacia arriba.
        await Spend("Comida afuera", 10_500_00, new DateTime(2026, 8, 5));
        await Spend("Comida afuera", 20_000_00, new DateTime(2026, 9, 5));
        Assert.Equal(16_000_00, await budgets.SuggestAsync(cats["Comida afuera"].Id, Today));

        await Assert.ThrowsAsync<ArgumentException>(() => budgets.SetAsync(cats["Sueldo"].Id, 1));
        await Assert.ThrowsAsync<ArgumentException>(() => budgets.SetAsync(cats["Delivery"].Id, 0));
        await budgets.DeleteAsync(cats["Delivery"].Id);
        Assert.Equal(2, (await budgets.GetStatusAsync(Today, Today)).Count);
    }
}

public class ReportTests
{
    private static readonly DateTime Today = new(2026, 10, 14);

    [Fact]
    public async Task Monthly_series_and_partial_month_comparison()
    {
        await using var db = await TestDb.CreateAsync();
        var reports = new ReportService(db.Factory, db.Accounts);
        var account = (await db.Accounts.GetActiveAsync()).First().Id;
        var cats = (await db.Categories.GetAsync()).ToDictionary(c => c.Name);

        async Task Add(TxType type, long amount, string cat, DateTime date) => await db.Transactions.AddAsync(new Transaction
            { Type = type, Amount = amount, AccountId = account, CategoryId = cats[cat].Id, Date = date });

        await Add(TxType.Income, 1_000_000_00, "Sueldo", new DateTime(2026, 9, 1));
        await Add(TxType.Expense, 40_000_00, "Delivery", new DateTime(2026, 9, 10)); // antes del 14 de sept.
        await Add(TxType.Expense, 90_000_00, "Delivery", new DateTime(2026, 9, 25)); // después: no entra en la comparación parcial
        await Add(TxType.Income, 1_000_000_00, "Sueldo", new DateTime(2026, 10, 1));
        await Add(TxType.Expense, 60_000_00, "Delivery", new DateTime(2026, 10, 5));

        var series = await reports.GetMonthlySeriesAsync(Today, 3);
        Assert.Equal([new DateTime(2026, 8, 1), new DateTime(2026, 9, 1), new DateTime(2026, 10, 1)], series.Select(s => s.Month));
        Assert.Equal(130_000_00, series[1].Expense);
        Assert.Equal(130_000_00, series[1].Optional); // delivery es prescindible
        Assert.Equal(870_000_00, series[1].NetWorth);
        Assert.Equal(1_810_000_00, series[2].NetWorth);

        var cmp = await reports.CompareMonthAsync(Today, Today);
        Assert.True(cmp.IsPartial);
        Assert.Equal(new DateTime(2026, 9, 14), cmp.PreviousTo);
        var delivery = cmp.Categories.Single(c => c.Category.Name == "Delivery");
        Assert.Equal(60_000_00, delivery.Current);
        Assert.Equal(40_000_00, delivery.Previous);
        Assert.Equal(0.5, delivery.Change!.Value, 3);
        Assert.Equal(1.5 / 1.02 - 1, delivery.RealChange(0.02)!.Value, 3);

        var full = await reports.CompareMonthAsync(Today, new DateTime(2026, 9, 1));
        Assert.False(full.IsPartial);
        Assert.Equal(130_000_00, full.Expense);
    }
}

public class ExportTests
{
    [Fact]
    public async Task Excel_and_csv_contain_movements_with_signed_amounts()
    {
        await using var db = await TestDb.CreateAsync();
        var account = (await db.Accounts.GetActiveAsync()).First().Id;
        var cats = (await db.Categories.GetAsync()).ToDictionary(c => c.Name);
        var today = new DateTime(2026, 10, 14);
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 1_234_56, AccountId = account, CategoryId = cats["Delivery"].Id, Date = today, Note = "Pizza; con \"amigos\"" });
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Income, Amount = 500_000_00, AccountId = account, CategoryId = cats["Sueldo"].Id, Date = today.AddDays(-3) });

        var export = new ExportService(db.Transactions, db.Accounts, new ReportService(db.Factory, db.Accounts));
        var dir = Path.Combine(Path.GetTempPath(), "cuentitas-export-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var xlsx = Path.Combine(dir, "x.xlsx");
            await export.ExportExcelAsync(xlsx, today.AddMonths(-1), today, today);
            using (var book = new XLWorkbook(xlsx))
            {
                Assert.Equal(["Movimientos", "Resumen mensual", "Cuentas"], book.Worksheets.Select(w => w.Name));
                var sheet = book.Worksheet("Movimientos");
                Assert.Equal("Ingreso", sheet.Cell(2, 2).GetString()); // orden cronológico
                Assert.Equal(-1234.56m, sheet.Cell(3, 7).GetValue<decimal>());
            }

            var csv = Path.Combine(dir, "x.csv");
            await export.ExportCsvAsync(csv, today.AddMonths(-1), today);
            var lines = await File.ReadAllLinesAsync(csv);
            Assert.StartsWith("Fecha;Tipo;", lines[0]);
            Assert.Contains("14/10/2026;Gasto;", lines[2]);
            Assert.Contains("-1234,56", lines[2]);
            Assert.Contains("\"Pizza; con \"\"amigos\"\"\"", lines[2]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
