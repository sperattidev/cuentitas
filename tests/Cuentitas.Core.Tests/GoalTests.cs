using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;

namespace Cuentitas.Core.Tests;

public class GoalMathTests
{
    [Fact]
    public void Without_inflation_is_a_simple_division_rounded_up()
    {
        Assert.Equal(4, GoalMath.MonthsToReach(saved: 0, target: 1_000, monthly: 300, monthlyInflation: 0));
        Assert.Equal(0, GoalMath.MonthsToReach(1_000, 1_000, 0, 0));
        Assert.Null(GoalMath.MonthsToReach(0, 1_000, 0, 0));
    }

    [Fact]
    public void Inflation_makes_it_take_longer_or_never()
    {
        var plain = GoalMath.MonthsToReach(0, 1_000_000, 100_000, 0);
        var withInflation = GoalMath.MonthsToReach(0, 1_000_000, 100_000, 0.02);
        Assert.Equal(10, plain);
        Assert.True(withInflation > plain);
        // 1 % del objetivo por mes con 3 % de inflación mensual: nunca alcanza.
        Assert.Null(GoalMath.MonthsToReach(0, 1_000_000, 10_000, 0.03));
    }

    [Fact]
    public void Required_monthly_reaches_the_inflated_target_exactly()
    {
        var required = GoalMath.RequiredMonthly(saved: 200_000, target: 1_000_000, months: 12, monthlyInflation: 0.02);
        var inflated = GoalMath.TargetAt(1_000_000, 12, 0.02);
        Assert.True(200_000 + required * 12 >= inflated);
        Assert.True(200_000 + (required - 1) * 12 < inflated);
        Assert.Equal(0, GoalMath.RequiredMonthly(2_000_000, 1_000_000, 12, 0));
    }

    [Theory]
    [InlineData("2026-10-14", "2027-10-14", 12)]
    [InlineData("2026-10-14", "2027-10-13", 11)]
    [InlineData("2026-10-14", "2026-10-30", 0)]
    [InlineData("2026-10-14", "2026-01-01", 0)]
    public void Months_until(string today, string date, int expected) =>
        Assert.Equal(expected, GoalMath.MonthsUntil(DateTime.Parse(today), DateTime.Parse(date)));

    [Fact]
    public void Projection_reports_eta_and_on_track()
    {
        var today = new DateTime(2026, 10, 14);
        var p = GoalMath.Project(current: 100, target: 1_000, monthlyPace: 100, PaceSource.Planned, 0, today, new DateTime(2027, 6, 14));
        Assert.Equal(9, p.MonthsToReach);
        Assert.Equal(new DateTime(2027, 7, 14), p.Eta);
        Assert.Equal(8, p.MonthsToTargetDate);
        Assert.False(p.OnTrack);
        Assert.Equal(113, p.RequiredMonthly); // (1000 − 100) / 8 redondeado hacia arriba
        Assert.Equal(0.1, p.Progress, 3);
    }
}

public class GoalServiceTests
{
    private static readonly DateTime Today = new(2026, 10, 14);

    [Fact]
    public async Task Contributions_track_progress_and_cannot_overdraw()
    {
        await using var db = await TestDb.CreateAsync();
        var id = await db.Goals.CreateAsync(new Goal { Name = "Vacaciones", TargetAmount = 1_000_000_00 });

        await db.Goals.AddContributionAsync(id, 300_000_00, Today.AddDays(-10));
        await db.Goals.AddContributionAsync(id, -50_000_00, Today);
        await Assert.ThrowsAsync<ArgumentException>(() => db.Goals.AddContributionAsync(id, -999_000_00, Today));

        var goal = (await db.Goals.GetAllAsync()).Single();
        var current = await db.Goals.GetCurrentAmountsAsync([goal], Today);
        Assert.Equal(250_000_00, current[id]);
        Assert.Equal(2, (await db.Goals.GetContributionsAsync(id)).Count);
    }

    [Fact]
    public async Task Linked_account_uses_its_balance_and_requires_same_currency()
    {
        await using var db = await TestDb.CreateAsync();
        var usd = await db.Accounts.CreateAsync(new Account
            { Name = "Dólares", Type = AccountType.Cash, Currency = Currency.USD, InitialBalance = 1_500_00 });
        var ars = (await db.Accounts.GetActiveAsync()).First(a => a.Currency == Currency.ARS).Id;

        await Assert.ThrowsAsync<ArgumentException>(() => db.Goals.CreateAsync(new Goal
            { Name = "Auto", Currency = Currency.USD, TargetAmount = 10_000_00, Tracking = GoalTracking.LinkedAccount, AccountId = ars }));

        var id = await db.Goals.CreateAsync(new Goal
            { Name = "Auto", Currency = Currency.USD, TargetAmount = 10_000_00, Tracking = GoalTracking.LinkedAccount, AccountId = usd });
        var goal = (await db.Goals.GetAllAsync()).Single();
        Assert.False(goal.AdjustForInflation); // en dólares no se ajusta
        Assert.Equal(1_500_00, (await db.Goals.GetCurrentAmountsAsync([goal], Today))[id]);
        await Assert.ThrowsAsync<ArgumentException>(() => db.Goals.AddContributionAsync(id, 100, Today));
    }

    [Fact]
    public async Task Savings_stats_average_last_complete_months_and_rank_optional_spending()
    {
        await using var db = await TestDb.CreateAsync();
        var account = (await db.Accounts.GetActiveAsync()).First().Id;
        var cats = (await db.Categories.GetAsync()).ToDictionary(c => c.Name);

        async Task Add(TxType type, long amount, string cat, DateTime date) =>
            await db.Transactions.AddAsync(new Transaction
                { Type = type, Amount = amount, AccountId = account, CategoryId = cats[cat].Id, Date = date });

        foreach (var month in new[] { new DateTime(2026, 8, 1), new DateTime(2026, 9, 1) })
        {
            await Add(TxType.Income, 1_000_000_00, "Sueldo", month);
            await Add(TxType.Expense, 600_000_00, "Supermercado", month.AddDays(5));
            await Add(TxType.Expense, 60_000_00, "Delivery", month.AddDays(10));
            await Add(TxType.Expense, 40_000_00, "Salidas", month.AddDays(12));
        }
        // El mes en curso no cuenta para el promedio (está incompleto).
        await Add(TxType.Expense, 900_000_00, "Supermercado", Today);

        var stats = (await db.Goals.GetSavingsStatsAsync(Today))[Currency.ARS];
        Assert.Equal(2, stats.Months);
        Assert.Equal(1_000_000_00, stats.AvgIncome);
        Assert.Equal(700_000_00, stats.AvgExpense);
        Assert.Equal(300_000_00, stats.AvgNet);
        Assert.Equal(100_000_00, stats.AvgOptional);
        Assert.Equal(["Delivery", "Salidas"], stats.OptionalByCategory.Select(o => o.Category.Name));
        Assert.Equal(60_000_00, stats.OptionalByCategory[0].MonthlyAverage);
    }

    [Fact]
    public async Task Overview_projects_with_average_savings_and_cut_shortens_it()
    {
        await using var db = await TestDb.CreateAsync();
        var account = (await db.Accounts.GetActiveAsync()).First().Id;
        var cats = (await db.Categories.GetAsync()).ToDictionary(c => c.Name);
        foreach (var month in new[] { new DateTime(2026, 7, 1), new DateTime(2026, 8, 1), new DateTime(2026, 9, 1) })
        {
            await db.Transactions.AddAsync(new Transaction
                { Type = TxType.Income, Amount = 500_000_00, AccountId = account, CategoryId = cats["Sueldo"].Id, Date = month });
            await db.Transactions.AddAsync(new Transaction
                { Type = TxType.Expense, Amount = 400_000_00, AccountId = account, CategoryId = cats["Delivery"].Id, Date = month });
        }
        await db.Settings.SetAsync(GoalService.Keys.MonthlyInflation, "0");
        await db.Goals.CreateAsync(new Goal { Name = "Notebook", TargetAmount = 1_200_000_00 });

        var normal = (await db.Goals.GetOverviewAsync(Today)).Single().Projection;
        Assert.Equal(PaceSource.AverageSavings, normal.PaceSource);
        Assert.Equal(100_000_00, normal.MonthlyPace);
        Assert.Equal(12, normal.MonthsToReach);

        // Recortando la mitad del delivery (200.000/mes) ahorrás 300.000/mes → 4 meses.
        var cut = (await db.Goals.GetOverviewAsync(Today, cutOptional: 0.5)).Single().Projection;
        Assert.Equal(300_000_00, cut.MonthlyPace);
        Assert.Equal(4, cut.MonthsToReach);
    }

    [Fact]
    public async Task Usd_goal_converts_peso_savings_with_purchase_rate_or_override()
    {
        await using var db = await TestDb.CreateAsync();
        var ars = (await db.Accounts.GetActiveAsync()).First().Id;
        var usd = await db.Accounts.CreateAsync(new Account { Name = "USD", Type = AccountType.Cash, Currency = Currency.USD });
        // Compra: $ 120.000 → US$ 100 = $ 1.200 por dólar.
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Transfer, Amount = 120_000_00, AccountId = ars, ToAccountId = usd, ToAmount = 100_00, Date = Today.AddDays(-5) });
        Assert.Equal(1200m, await db.Goals.GetUsdRateAsync(Today));

        var goal = new Goal { Name = "Viaje", Currency = Currency.USD, TargetAmount = 2_000_00 };
        var stats = new Dictionary<Currency, SavingsStats>
        {
            [Currency.ARS] = new() { AvgIncome = 1_000_000_00, AvgExpense = 760_000_00 },
            [Currency.USD] = new(),
        };
        var (pace, source) = GoalService.MonthlyPace(goal, stats, 1200m);
        Assert.Equal(200_00, pace); // $ 240.000 / 1.200 = US$ 200
        Assert.Equal(PaceSource.AverageSavings, source);

        await db.Goals.SetUsdRateOverrideAsync(1500m);
        Assert.Equal(1500m, await db.Goals.GetUsdRateAsync(Today));

        goal.PlannedMonthly = 50_00;
        Assert.Equal((50_00L, PaceSource.Planned), GoalService.MonthlyPace(goal, stats, 1200m));
    }

    [Fact]
    public async Task Validation_and_delete()
    {
        await using var db = await TestDb.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => db.Goals.CreateAsync(new Goal { Name = "", TargetAmount = 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Goals.CreateAsync(new Goal { Name = "X", TargetAmount = 0 }));
        var id = await db.Goals.CreateAsync(new Goal { Name = "X", TargetAmount = 100 });
        await db.Goals.AddContributionAsync(id, 50, Today);
        await db.Goals.DeleteAsync(id);
        Assert.Empty(await db.Goals.GetAllAsync(includeArchived: true));
        Assert.Empty(await db.Goals.GetContributionsAsync(id));
    }
}
