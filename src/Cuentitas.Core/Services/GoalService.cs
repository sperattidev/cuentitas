using System.Globalization;
using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

/// <summary>Promedio mensual de un gasto prescindible por categoría.</summary>
public sealed record OptionalSpending(Category Category, long MonthlyAverage);

/// <summary>Promedios mensuales de los últimos meses completos, por moneda (centavos).</summary>
public sealed record SavingsStats
{
    public long AvgIncome { get; init; }
    public long AvgExpense { get; init; }
    public long AvgOptional { get; init; }
    public long AvgNet => AvgIncome - AvgExpense;

    /// <summary>Meses completos con movimientos usados para el promedio (0 = se usó el mes en curso).</summary>
    public int Months { get; init; }

    /// <summary>Sólo pesos: gastos prescindibles por categoría, de mayor a menor.</summary>
    public IReadOnlyList<OptionalSpending> OptionalByCategory { get; init; } = [];
}

/// <summary>Una meta con su progreso y proyección calculados.</summary>
public sealed record GoalOverview(Goal Goal, GoalProjection Projection);

public sealed class GoalService(IDbContextFactory<CuentitasDbContext> factory, AccountService accounts,
    SettingsService settings, DataChanges changes, QuoteService? quotes = null)
{
    public const int StatsMonths = 3;
    public const double DefaultMonthlyInflation = 0.02;

    public static class Keys
    {
        /// <summary>Inflación mensual estimada, fracción ("0.02" = 2 %).</summary>
        public const string MonthlyInflation = "goals.monthlyInflation";

        /// <summary>Pesos por dólar elegidos por el usuario (texto invariante). Vacío = automático.</summary>
        public const string UsdRate = "goals.usdRate";
    }

    // ── Metas ────────────────────────────────────────────────────────────────

    public async Task<List<Goal>> GetAllAsync(bool includeArchived = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Goals.AsNoTracking().Include(g => g.Account)
            .Where(g => includeArchived || !g.Archived)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .ToListAsync();
    }

    public async Task<int> CreateAsync(Goal goal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ValidateAsync(db, goal);
        goal.Id = 0;
        goal.Account = null;
        goal.CreatedAt = DateTime.Now;
        goal.SortOrder = (await db.Goals.MaxAsync(g => (int?)g.SortOrder) ?? 0) + 1;
        db.Goals.Add(goal);
        await db.SaveChangesAsync();
        changes.Notify();
        return goal.Id;
    }

    public async Task UpdateAsync(Goal goal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Goals.FindAsync(goal.Id) ?? throw new InvalidOperationException("La meta no existe.");
        await ValidateAsync(db, goal);
        e.Name = goal.Name;
        e.Icon = goal.Icon;
        e.Color = goal.Color;
        e.Currency = goal.Currency;
        e.TargetAmount = goal.TargetAmount;
        e.TargetDate = goal.TargetDate;
        e.Tracking = goal.Tracking;
        e.AccountId = goal.AccountId;
        e.AdjustForInflation = goal.AdjustForInflation;
        e.PlannedMonthly = goal.PlannedMonthly;
        e.Note = goal.Note;
        e.CompletedAt = goal.CompletedAt;
        e.Archived = goal.Archived;
        await db.SaveChangesAsync();
        changes.Notify();
    }

    public async Task SetArchivedAsync(int id, bool archived)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Goals.Where(g => g.Id == id).ExecuteUpdateAsync(s => s.SetProperty(g => g.Archived, archived));
        changes.Notify();
    }

    /// <summary>Marca la meta como cumplida (o la reabre) y la archiva.</summary>
    public async Task SetCompletedAsync(int id, bool completed)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Goals.Where(g => g.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(g => g.CompletedAt, completed ? DateTime.Now : null)
            .SetProperty(g => g.Archived, completed));
        changes.Notify();
    }

    /// <summary>Borra la meta y sus aportes. No toca cuentas ni movimientos.</summary>
    public async Task DeleteAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.GoalContributions.Where(c => c.GoalId == id).ExecuteDeleteAsync();
        await db.Goals.Where(g => g.Id == id).ExecuteDeleteAsync();
        changes.Notify();
    }

    // ── Aportes ──────────────────────────────────────────────────────────────

    public async Task<List<GoalContribution>> GetContributionsAsync(int goalId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.GoalContributions.AsNoTracking().Where(c => c.GoalId == goalId)
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id).ToListAsync();
    }

    /// <summary>Registra un aporte (monto positivo) o un retiro (negativo).</summary>
    public async Task<int> AddContributionAsync(int goalId, long amount, DateTime date, string note = "")
    {
        if (amount == 0) throw new ArgumentException("El monto no puede ser cero.");
        await using var db = await factory.CreateDbContextAsync();
        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == goalId)
                   ?? throw new ArgumentException("La meta no existe.");
        if (goal.Tracking != GoalTracking.Contributions)
            throw new ArgumentException("Esta meta se mide con el saldo de una cuenta: movés plata a esa cuenta en lugar de aportar.");
        if (amount < 0)
        {
            var current = await db.GoalContributions.Where(c => c.GoalId == goalId).SumAsync(c => (long?)c.Amount) ?? 0;
            if (current + amount < 0) throw new ArgumentException("No podés retirar más de lo que tiene la meta.");
        }
        var c = new GoalContribution { GoalId = goalId, Amount = amount, Date = date.Date, Note = (note ?? "").Trim() };
        db.GoalContributions.Add(c);
        await db.SaveChangesAsync();
        changes.Notify();
        return c.Id;
    }

    public async Task DeleteContributionAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.GoalContributions.Where(c => c.Id == id).ExecuteDeleteAsync();
        changes.Notify();
    }

    /// <summary>Cuánto lleva cada meta: suma de aportes, o saldo a hoy de la cuenta vinculada.</summary>
    public async Task<Dictionary<int, long>> GetCurrentAmountsAsync(IEnumerable<Goal> goals, DateTime today)
    {
        var list = goals.ToList();
        await using var db = await factory.CreateDbContextAsync();
        var ids = list.Select(g => g.Id).ToList();
        var sums = await db.GoalContributions.Where(c => ids.Contains(c.GoalId) && c.Date <= today.Date)
            .GroupBy(c => c.GoalId).Select(g => new { g.Key, Sum = g.Sum(c => c.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum);
        var balances = list.Any(g => g.Tracking == GoalTracking.LinkedAccount)
            ? (await accounts.GetAllWithBalancesAsync(includeArchived: true, asOf: today)).ToDictionary(b => b.Account.Id, b => b.Balance)
            : [];

        return list.ToDictionary(g => g.Id, g => g.Tracking == GoalTracking.LinkedAccount
            ? Math.Max(0, g.AccountId is { } a ? balances.GetValueOrDefault(a) : 0)
            : sums.GetValueOrDefault(g.Id));
    }

    // ── Estadísticas de ahorro ───────────────────────────────────────────────

    /// <summary>
    /// Promedios de los últimos <see cref="StatsMonths"/> meses completos con movimientos.
    /// Si todavía no hay ninguno, usa lo que va del mes actual (Months = 0).
    /// </summary>
    public async Task<Dictionary<Currency, SavingsStats>> GetSavingsStatsAsync(DateTime today)
    {
        await using var db = await factory.CreateDbContextAsync();
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var from = thisMonth.AddMonths(-StatsMonths);

        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Date >= from && t.Date <= today.Date && t.Type != TxType.Transfer)
            .Select(t => new { t.Date, t.Type, t.Amount, t.Necessity, t.CategoryId, t.Account!.Currency })
            .ToListAsync();

        var complete = rows.Where(r => r.Date < thisMonth).ToList();
        var months = complete.Select(r => (r.Date.Year, r.Date.Month)).Distinct().Count();
        var source = months > 0 ? complete : rows.Where(r => r.Date >= thisMonth).ToList();
        var divisor = Math.Max(1, months);

        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id);
        return Enum.GetValues<Currency>().ToDictionary(cur => cur, cur =>
        {
            var r = source.Where(x => x.Currency == cur).ToList();
            var optional = r.Where(x => x.Type == TxType.Expense && x.Necessity == Necessity.Optional).ToList();
            return new SavingsStats
            {
                AvgIncome = r.Where(x => x.Type == TxType.Income).Sum(x => x.Amount) / divisor,
                AvgExpense = r.Where(x => x.Type == TxType.Expense).Sum(x => x.Amount) / divisor,
                AvgOptional = optional.Sum(x => x.Amount) / divisor,
                Months = months,
                OptionalByCategory = optional.Where(x => x.CategoryId is not null)
                    .GroupBy(x => x.CategoryId!.Value)
                    .Select(g => new OptionalSpending(categories[g.Key], g.Sum(x => x.Amount) / divisor))
                    .OrderByDescending(o => o.MonthlyAverage)
                    .ToList(),
            };
        });
    }

    // ── Parámetros: inflación y dólar ────────────────────────────────────────

    public async Task<double> GetMonthlyInflationAsync() =>
        double.TryParse(await settings.GetAsync(Keys.MonthlyInflation), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : DefaultMonthlyInflation;

    public async Task SetMonthlyInflationAsync(double value)
    {
        if (value is < 0 or > 0.5) throw new ArgumentException("La inflación mensual tiene que estar entre 0 % y 50 %.");
        await settings.SetAsync(Keys.MonthlyInflation, value.ToString(CultureInfo.InvariantCulture));
        changes.Notify();
    }

    /// <summary>Pesos por dólar elegido por el usuario, o null si es automático.</summary>
    public async Task<decimal?> GetUsdRateOverrideAsync() =>
        decimal.TryParse(await settings.GetAsync(Keys.UsdRate), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : null;

    public async Task SetUsdRateOverrideAsync(decimal? pesosPerDollar)
    {
        if (pesosPerDollar is <= 0) throw new ArgumentException("El tipo de cambio tiene que ser mayor a cero.");
        await settings.SetAsync(Keys.UsdRate, pesosPerDollar?.ToString(CultureInfo.InvariantCulture) ?? "");
        changes.Notify();
    }

    /// <summary>Tipo de cambio promedio de tus compras de dólares de los últimos 3 meses (pesos por dólar), o null.</summary>
    public async Task<decimal?> GetAverageDollarPurchaseRateAsync(DateTime today)
    {
        await using var db = await factory.CreateDbContextAsync();
        var from = today.Date.AddMonths(-StatsMonths);
        var buys = await db.Transactions.AsNoTracking()
            .Where(t => t.Type == TxType.Transfer && t.Date >= from && t.Date <= today.Date &&
                        t.Account!.Currency == Currency.ARS && t.ToAccount!.Currency == Currency.USD && t.ToAmount > 0)
            .Select(t => new { t.Amount, ToAmount = t.ToAmount!.Value })
            .ToListAsync();
        var received = buys.Sum(b => b.ToAmount);
        return received > 0 ? (decimal)buys.Sum(b => b.Amount) / received : null;
    }

    /// <summary>
    /// Tipo de cambio a usar: el elegido por el usuario; si no, la última cotización guardada del dólar
    /// de valuación (venta); si no, el promedio de tus compras.
    /// </summary>
    public async Task<decimal?> GetUsdRateAsync(DateTime today) =>
        await GetUsdRateOverrideAsync() ?? await GetQuotedRateAsync() ?? await GetAverageDollarPurchaseRateAsync(today);

    /// <summary>Cotización guardada (sin ir a internet) del dólar elegido para valuar.</summary>
    public async Task<decimal?> GetQuotedRateAsync()
    {
        if (quotes is null || await quotes.GetCachedAsync() is not { } snapshot) return null;
        return snapshot.Get(await quotes.GetValuationTypeAsync())?.Sell;
    }

    // ── Proyección ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ritmo mensual de ahorro para una meta, en su moneda. Usa el aporte planificado si hay;
    /// si no, el ahorro promedio (en dólares: el ahorro en pesos convertido + lo ahorrado en dólares).
    /// <paramref name="extraArs"/> suma pesos por mes (ej. lo que recortarías de prescindibles).
    /// </summary>
    public static (long Pace, PaceSource Source) MonthlyPace(Goal goal, IReadOnlyDictionary<Currency, SavingsStats> stats,
        decimal? usdRate, long extraArs = 0)
    {
        long ToGoalCurrency(long ars) => goal.Currency == Currency.ARS
            ? ars
            : usdRate is > 0 ? (long)Math.Round(ars / usdRate.Value) : 0;

        if (goal.PlannedMonthly is > 0) return (goal.PlannedMonthly.Value + ToGoalCurrency(extraArs), PaceSource.Planned);

        var arsNet = stats.TryGetValue(Currency.ARS, out var ars) ? ars.AvgNet : 0;
        var usdNet = stats.TryGetValue(Currency.USD, out var usd) ? usd.AvgNet : 0;
        var pace = goal.Currency == Currency.ARS
            ? arsNet + extraArs
            : usdNet + ToGoalCurrency(arsNet + extraArs);
        var hasData = (ars?.AvgIncome ?? 0) > 0 || (usd?.AvgIncome ?? 0) > 0;
        return (Math.Max(0, pace), hasData ? PaceSource.AverageSavings : PaceSource.None);
    }

    /// <summary>Todas las metas activas con su proyección. <paramref name="cutOptional"/>: fracción (0..1) de prescindibles que recortarías.</summary>
    public async Task<List<GoalOverview>> GetOverviewAsync(DateTime today, double cutOptional = 0)
    {
        var goals = await GetAllAsync();
        if (goals.Count == 0) return [];
        var current = await GetCurrentAmountsAsync(goals, today);
        var stats = await GetSavingsStatsAsync(today);
        var rate = await GetUsdRateAsync(today);
        var inflation = await GetMonthlyInflationAsync();
        var extra = (long)Math.Round(stats[Currency.ARS].AvgOptional * Math.Clamp(cutOptional, 0, 1));

        return goals.Select(g =>
        {
            var (pace, source) = MonthlyPace(g, stats, rate, extra);
            var i = g.Currency == Currency.ARS && g.AdjustForInflation ? inflation : 0;
            return new GoalOverview(g, GoalMath.Project(current[g.Id], g.TargetAmount, pace, source, i, today, g.TargetDate));
        }).ToList();
    }

    private static async Task ValidateAsync(CuentitasDbContext db, Goal goal)
    {
        goal.Name = (goal.Name ?? "").Trim();
        goal.Note = (goal.Note ?? "").Trim();
        goal.TargetDate = goal.TargetDate?.Date;
        if (goal.Name.Length == 0) throw new ArgumentException("Poné un nombre para la meta (ej.: Vacaciones, Auto).");
        if (goal.Name.Length > 60) throw new ArgumentException("El nombre es demasiado largo (máx. 60).");
        if (goal.TargetAmount <= 0) throw new ArgumentException("Indicá cuánto querés juntar.");
        if (goal.PlannedMonthly is < 0) throw new ArgumentException("El aporte mensual no puede ser negativo.");
        if (goal.PlannedMonthly == 0) goal.PlannedMonthly = null;
        if (goal.Currency == Currency.USD) goal.AdjustForInflation = false;

        if (goal.Tracking == GoalTracking.LinkedAccount)
        {
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == goal.AccountId)
                          ?? throw new ArgumentException("Elegí la cuenta donde juntás la plata.");
            if (account.Currency != goal.Currency)
                throw new ArgumentException($"La cuenta «{account.Name}» es en {account.Currency} y la meta en {goal.Currency}.");
        }
        else
        {
            goal.AccountId = null;
        }
    }
}
