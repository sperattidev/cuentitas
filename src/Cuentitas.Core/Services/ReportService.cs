using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

/// <summary>Un mes de la serie histórica (pesos, centavos).</summary>
public sealed record MonthPoint(DateTime Month, long Income, long Expense, long Optional, long NetWorth)
{
    public long Net => Income - Expense;
    public long Necessary => Expense - Optional;
}

/// <summary>Gasto de una categoría en el período vs el anterior.</summary>
public sealed record CategoryChange(Category Category, long Current, long Previous)
{
    public long Delta => Current - Previous;

    /// <summary>Variación nominal (null si antes no había gasto).</summary>
    public double? Change => Previous > 0 ? (double)Current / Previous - 1 : null;

    /// <summary>Variación descontando la inflación del mes: (actual / (anterior × (1 + i))) − 1.</summary>
    public double? RealChange(double monthlyInflation) =>
        Previous > 0 ? Current / (Previous * (1 + monthlyInflation)) - 1 : null;
}

/// <summary>Comparación de un mes con el anterior. En el mes en curso compara "a la misma altura del mes".</summary>
public sealed record MonthComparison(
    DateTime Month, DateTime From, DateTime To, DateTime PreviousFrom, DateTime PreviousTo,
    long Expense, long PreviousExpense, IReadOnlyList<CategoryChange> Categories)
{
    public bool IsPartial { get; init; }
    public double? Change => PreviousExpense > 0 ? (double)Expense / PreviousExpense - 1 : null;
}

/// <summary>Datos para la pantalla Reportes. Sólo pesos (las cuentas en dólares se ven en Cuentas).</summary>
public sealed class ReportService(IDbContextFactory<CuentitasDbContext> factory, AccountService accounts)
{
    /// <summary>Últimos <paramref name="months"/> meses (incluido el actual, hasta hoy), del más viejo al más nuevo.</summary>
    public async Task<List<MonthPoint>> GetMonthlySeriesAsync(DateTime today, int months = 12)
    {
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var from = thisMonth.AddMonths(-(months - 1));
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Date >= from && t.Date <= today.Date && t.Type != TxType.Transfer && t.Account!.Currency == Currency.ARS)
            .Select(t => new { t.Date, t.Type, t.Amount, t.Necessity })
            .ToListAsync();

        var result = new List<MonthPoint>();
        for (var m = from; m <= thisMonth; m = m.AddMonths(1))
        {
            var inMonth = rows.Where(r => r.Date.Year == m.Year && r.Date.Month == m.Month).ToList();
            var asOf = m == thisMonth ? today.Date : m.AddMonths(1).AddDays(-1);
            var worth = (await accounts.GetAllWithBalancesAsync(asOf: asOf))
                .Where(b => b.Account.IncludeInTotal && b.Account.Currency == Currency.ARS)
                .Sum(b => b.Balance);
            result.Add(new MonthPoint(m,
                inMonth.Where(r => r.Type == TxType.Income).Sum(r => r.Amount),
                inMonth.Where(r => r.Type == TxType.Expense).Sum(r => r.Amount),
                inMonth.Where(r => r.Type == TxType.Expense && r.Necessity == Necessity.Optional).Sum(r => r.Amount),
                worth));
        }
        return result;
    }

    /// <summary>
    /// Gastos por categoría del mes vs el mes anterior. Si <paramref name="month"/> es el mes en curso,
    /// se compara del 1 a hoy contra el mismo tramo del mes anterior (si no, el mes en curso siempre "baja").
    /// </summary>
    public async Task<MonthComparison> CompareMonthAsync(DateTime today, DateTime month)
    {
        var from = new DateTime(month.Year, month.Month, 1);
        var partial = from.Year == today.Year && from.Month == today.Month;
        var to = partial ? today.Date : from.AddMonths(1).AddDays(-1);
        var prevFrom = from.AddMonths(-1);
        var prevTo = partial
            ? prevFrom.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(prevFrom.Year, prevFrom.Month)) - 1)
            : from.AddDays(-1);

        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Type == TxType.Expense && t.CategoryId != null && t.Account!.Currency == Currency.ARS &&
                        ((t.Date >= from && t.Date <= to) || (t.Date >= prevFrom && t.Date <= prevTo)))
            .Select(t => new { t.Date, t.Amount, CategoryId = t.CategoryId!.Value })
            .ToListAsync();
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id);

        var changes = rows.GroupBy(r => r.CategoryId)
            .Select(g => new CategoryChange(categories[g.Key],
                g.Where(r => r.Date >= from).Sum(r => r.Amount),
                g.Where(r => r.Date < from).Sum(r => r.Amount)))
            .OrderByDescending(c => Math.Max(c.Current, c.Previous))
            .ToList();

        return new MonthComparison(from, from, to, prevFrom, prevTo,
            changes.Sum(c => c.Current), changes.Sum(c => c.Previous), changes)
        { IsPartial = partial };
    }
}
