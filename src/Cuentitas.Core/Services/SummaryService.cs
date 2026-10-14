using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

/// <summary>Totales de un período para una moneda. Las transferencias no cuentan como ingreso ni gasto.</summary>
public sealed record PeriodTotals(Currency Currency, long Income, long Expense, long OptionalExpense)
{
    public long Net => Income - Expense;

    /// <summary>Porcentaje del ingreso que se ahorró (0..1), o null si no hubo ingresos.</summary>
    public double? SavingsRate => Income > 0 ? (double)Net / Income : null;
}

public sealed record CategoryTotal(Category Category, long Total);

public sealed class SummaryService(IDbContextFactory<CuentitasDbContext> factory, AccountService accounts)
{
    /// <summary>Patrimonio por moneda: suma de saldos de cuentas activas que cuentan en el total.</summary>
    public async Task<Dictionary<Currency, long>> GetNetWorthAsync()
    {
        var balances = await accounts.GetAllWithBalancesAsync();
        var result = Enum.GetValues<Currency>().ToDictionary(c => c, _ => 0L);
        foreach (var b in balances.Where(b => b.Account.IncludeInTotal))
            result[b.Account.Currency] += b.Balance;
        return result;
    }

    /// <summary>Ingresos, gastos y gastos prescindibles entre dos fechas (inclusive), por moneda.</summary>
    public async Task<Dictionary<Currency, PeriodTotals>> GetTotalsAsync(DateTime from, DateTime to)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Date >= from.Date && t.Date < to.Date.AddDays(1) && t.Type != TxType.Transfer)
            .Select(t => new { t.Type, t.Amount, t.Necessity, t.Account!.Currency })
            .ToListAsync();

        return Enum.GetValues<Currency>().ToDictionary(c => c, c =>
        {
            var inCur = rows.Where(r => r.Currency == c).ToList();
            return new PeriodTotals(c,
                Income: inCur.Where(r => r.Type == TxType.Income).Sum(r => r.Amount),
                Expense: inCur.Where(r => r.Type == TxType.Expense).Sum(r => r.Amount),
                OptionalExpense: inCur.Where(r => r.Type == TxType.Expense && r.Necessity == Necessity.Optional)
                    .Sum(r => r.Amount));
        });
    }

    /// <summary>Gastos por categoría en el período, de mayor a menor, para una moneda.</summary>
    public async Task<List<CategoryTotal>> GetExpensesByCategoryAsync(DateTime from, DateTime to,
        Currency currency = Currency.ARS)
    {
        await using var db = await factory.CreateDbContextAsync();
        var totals = await db.Transactions.AsNoTracking()
            .Where(t => t.Date >= from.Date && t.Date < to.Date.AddDays(1)
                        && t.Type == TxType.Expense && t.CategoryId != null && t.Account!.Currency == currency)
            .GroupBy(t => t.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync();

        var ids = totals.Select(t => t.CategoryId).ToList();
        var categories = await db.Categories.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        return totals
            .Select(t => new CategoryTotal(categories[t.CategoryId], t.Total))
            .OrderByDescending(t => t.Total)
            .ToList();
    }

    public static (DateTime From, DateTime To) MonthRange(int year, int month)
    {
        var from = new DateTime(year, month, 1);
        return (from, from.AddMonths(1).AddDays(-1));
    }
}
