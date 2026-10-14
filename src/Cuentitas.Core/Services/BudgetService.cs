using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

public enum BudgetLevel
{
    /// <summary>Menos del 80 %.</summary>
    Ok,

    /// <summary>80 % a 100 %.</summary>
    Warning,

    /// <summary>Te pasaste.</summary>
    Over,
}

/// <summary>Cómo va un presupuesto en un mes. Montos en centavos de pesos.</summary>
public sealed record BudgetStatus(Budget Budget, Category Category, long Spent, long? ProjectedEndOfMonth)
{
    public long Limit => Budget.MonthlyLimit;
    public double Ratio => Limit <= 0 ? 0 : (double)Spent / Limit;
    public long Remaining => Limit - Spent;

    public BudgetLevel Level => Ratio switch
    {
        > 1 => BudgetLevel.Over,
        >= BudgetService.WarningRatio => BudgetLevel.Warning,
        _ => BudgetLevel.Ok,
    };

    /// <summary>Al ritmo de este mes, ¿te vas a pasar?</summary>
    public bool WillExceed => ProjectedEndOfMonth > Limit && Level != BudgetLevel.Over;
}

/// <summary>Presupuestos mensuales por categoría (sólo gastos en pesos). Ver docs/MODELO_DE_DATOS.md § Presupuestos.</summary>
public sealed class BudgetService(IDbContextFactory<CuentitasDbContext> factory, DataChanges changes)
{
    public const double WarningRatio = 0.8;

    /// <summary>Crea o actualiza el presupuesto de una categoría.</summary>
    public async Task SetAsync(int categoryId, long monthlyLimit)
    {
        if (monthlyLimit <= 0) throw new ArgumentException("El presupuesto tiene que ser mayor a cero.");
        await using var db = await factory.CreateDbContextAsync();
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId)
                       ?? throw new ArgumentException("La categoría no existe.");
        if (category.Kind != CategoryKind.Expense) throw new ArgumentException("Sólo las categorías de gasto tienen presupuesto.");

        var existing = await db.Budgets.FirstOrDefaultAsync(b => b.CategoryId == categoryId);
        if (existing is null) db.Budgets.Add(new Budget { CategoryId = categoryId, MonthlyLimit = monthlyLimit });
        else existing.MonthlyLimit = monthlyLimit;
        await db.SaveChangesAsync();
        changes.Notify();
    }

    public async Task DeleteAsync(int categoryId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Budgets.Where(b => b.CategoryId == categoryId).ExecuteDeleteAsync();
        changes.Notify();
    }

    /// <summary>
    /// Estado de cada presupuesto en el mes de <paramref name="month"/>. Si es el mes en curso, cuenta
    /// hasta hoy y proyecta el cierre (gasto ÷ días transcurridos × días del mes).
    /// </summary>
    public async Task<List<BudgetStatus>> GetStatusAsync(DateTime today, DateTime month)
    {
        var from = new DateTime(month.Year, month.Month, 1);
        var end = from.AddMonths(1).AddDays(-1);
        var current = from.Year == today.Year && from.Month == today.Month;
        var to = current ? today.Date : end;

        await using var db = await factory.CreateDbContextAsync();
        var budgets = await db.Budgets.AsNoTracking().Include(b => b.Category).ToListAsync();
        if (budgets.Count == 0) return [];

        var ids = budgets.Select(b => b.CategoryId).ToList();
        var spent = await db.Transactions.AsNoTracking()
            .Where(t => t.Type == TxType.Expense && t.CategoryId != null && ids.Contains(t.CategoryId.Value) &&
                        t.Date >= from && t.Date <= to && t.Account!.Currency == Currency.ARS)
            .GroupBy(t => t.CategoryId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum);

        var elapsed = (to - from).Days + 1;
        var days = DateTime.DaysInMonth(from.Year, from.Month);
        return budgets
            .Where(b => b.Category is { Archived: false })
            .Select(b =>
            {
                var s = spent.GetValueOrDefault(b.CategoryId);
                long? projected = current && elapsed >= 3 ? s * days / elapsed : null;
                return new BudgetStatus(b, b.Category!, s, projected);
            })
            .OrderByDescending(s => s.Ratio)
            .ToList();
    }

    /// <summary>Presupuestos del mes en curso al 80 % o más.</summary>
    public async Task<List<BudgetStatus>> GetAlertsAsync(DateTime today) =>
        (await GetStatusAsync(today, today)).Where(s => s.Level != BudgetLevel.Ok).ToList();

    /// <summary>Sugerencia: lo que gastaste en promedio en esa categoría los últimos 3 meses completos (redondeado a $ 1.000).</summary>
    public async Task<long> SuggestAsync(int categoryId, DateTime today)
    {
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var from = thisMonth.AddMonths(-3);
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Type == TxType.Expense && t.CategoryId == categoryId && t.Date >= from && t.Date < thisMonth &&
                        t.Account!.Currency == Currency.ARS)
            .Select(t => new { t.Date, t.Amount })
            .ToListAsync();
        var months = rows.Select(r => (r.Date.Year, r.Date.Month)).Distinct().Count();
        if (months == 0) return 0;
        var avg = rows.Sum(r => r.Amount) / months;
        const long step = 1_000_00;
        return (avg + step - 1) / step * step;
    }
}
