using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;

namespace Cuentitas.Core.Tests;

public class ScheduleTests
{
    private static RecurringItem Item(DateTime start, Frequency f, DateTime? end = null) =>
        new() { Name = "x", StartDate = start, Frequency = f, EndDate = end };

    [Fact]
    public void Monthly_on_day_31_uses_last_day_of_short_months()
    {
        var item = Item(new DateTime(2026, 1, 31), Frequency.Monthly);
        var dates = Schedule.DueDates(item, new DateTime(2026, 1, 1), new DateTime(2026, 4, 30)).ToList();
        Assert.Equal([new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 31), new(2026, 4, 30)], dates);
    }

    [Fact]
    public void Bimonthly_skips_a_month_and_respects_end_date()
    {
        var item = Item(new DateTime(2026, 2, 15), Frequency.Bimonthly, end: new DateTime(2026, 8, 1));
        var dates = Schedule.DueDates(item, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)).ToList();
        Assert.Equal([new(2026, 2, 15), new(2026, 4, 15), new(2026, 6, 15)], dates);
    }

    [Fact]
    public void Semiannual_like_aguinaldo()
    {
        var item = Item(new DateTime(2026, 6, 30), Frequency.Semiannual);
        var dates = Schedule.DueDates(item, new DateTime(2027, 1, 1), new DateTime(2027, 12, 31)).ToList();
        Assert.Equal([new(2027, 6, 30), new(2027, 12, 30)], dates);
    }

    [Fact]
    public void Range_far_from_start_is_found_without_missing_dates()
    {
        var item = Item(new DateTime(2020, 3, 10), Frequency.Quarterly);
        var dates = Schedule.DueDates(item, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)).ToList();
        Assert.Equal([new(2026, 3, 10), new(2026, 6, 10), new(2026, 9, 10), new(2026, 12, 10)], dates);
    }

    [Theory]
    [InlineData(10, "2026-10-14", "2026-11-10")]
    [InlineData(14, "2026-10-14", "2026-10-14")]
    [InlineData(31, "2026-10-14", "2026-10-31")]
    [InlineData(31, "2026-11-05", "2026-11-30")]
    public void First_due_for_day_of_month(int day, string today, string expected) =>
        Assert.Equal(DateTime.Parse(expected), Schedule.FirstDueFor(day, DateTime.Parse(today)));
}

public class RecurringServiceTests
{
    private static readonly DateTime Today = new(2026, 10, 14);

    private static async Task<(int account, Category luz, Category sueldo)> SetupAsync(TestDb db)
    {
        var account = (await db.Accounts.GetActiveAsync()).First().Id;
        var cats = await db.Categories.GetAsync();
        return (account, cats.First(c => c.Name == "Luz"), cats.First(c => c.Name == "Sueldo"));
    }

    [Fact]
    public async Task Agenda_classifies_overdue_due_soon_upcoming_paid_and_skipped()
    {
        await using var db = await TestDb.CreateAsync();
        var (account, luz, _) = await SetupAsync(db);
        var id = await db.Recurring.CreateAsync(new RecurringItem
        {
            Name = "Luz", Type = TxType.Expense, CategoryId = luz.Id, AccountId = account, EstimatedAmount = 40_000_00,
            Frequency = Frequency.Monthly, StartDate = new DateTime(2026, 7, 16), RemindDaysBefore = 3,
        });

        // Julio pagado, agosto omitido, septiembre impago → vencido.
        await db.Transactions.AddAsync(new Transaction
        {
            Type = TxType.Expense, Amount = 38_000_00, AccountId = account, CategoryId = luz.Id,
            Date = new DateTime(2026, 7, 15), RecurringItemId = id, DueDate = new DateTime(2026, 7, 16),
        });
        await db.Recurring.SkipAsync(id, new DateTime(2026, 8, 16));

        var agenda = await db.Recurring.GetAgendaAsync(Today, new DateTime(2026, 10, 1), new DateTime(2026, 11, 30));
        var byDate = agenda.ToDictionary(o => o.DueDate, o => o.Status);

        Assert.False(byDate.ContainsKey(new DateTime(2026, 7, 16))); // pagado y fuera del rango: no aparece
        Assert.False(byDate.ContainsKey(new DateTime(2026, 8, 16))); // omitido fuera del rango
        Assert.Equal(DueStatus.Overdue, byDate[new DateTime(2026, 9, 16)]);
        Assert.Equal(DueStatus.DueSoon, byDate[new DateTime(2026, 10, 16)]); // 2 días ≤ 3 de aviso
        Assert.Equal(DueStatus.Upcoming, byDate[new DateTime(2026, 11, 16)]);

        var reminders = await db.Recurring.GetRemindersAsync(Today);
        Assert.Equal([new DateTime(2026, 9, 16), new DateTime(2026, 10, 16)], reminders.Select(r => r.DueDate));
    }

    [Fact]
    public async Task Paying_links_the_movement_and_cannot_pay_twice()
    {
        await using var db = await TestDb.CreateAsync();
        var (account, _, sueldo) = await SetupAsync(db);
        var id = await db.Recurring.CreateAsync(new RecurringItem
        {
            Name = "Sueldo", Type = TxType.Income, CategoryId = sueldo.Id, AccountId = account,
            EstimatedAmount = 1_000_000_00, Frequency = Frequency.Monthly, StartDate = new DateTime(2026, 10, 1),
        });

        Transaction Pay() => new()
        {
            Type = TxType.Income, Amount = 1_050_000_00, AccountId = account, CategoryId = sueldo.Id,
            Date = new DateTime(2026, 10, 3), RecurringItemId = id, DueDate = new DateTime(2026, 10, 1),
        };
        await db.Transactions.AddAsync(Pay());
        await Assert.ThrowsAsync<ArgumentException>(() => db.Transactions.AddAsync(Pay()));

        var agenda = await db.Recurring.GetAgendaAsync(Today, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));
        var occ = Assert.Single(agenda);
        Assert.Equal(DueStatus.Paid, occ.Status);
        Assert.Equal(1_050_000_00, occ.Payment!.Amount);

        // Un cobro no se puede registrar como gasto.
        var luz = (await db.Categories.GetAsync(CategoryKind.Expense)).First();
        await Assert.ThrowsAsync<ArgumentException>(() => db.Transactions.AddAsync(new Transaction
        {
            Type = TxType.Expense, Amount = 100, AccountId = account, CategoryId = luz.Id, Date = Today,
            RecurringItemId = id, DueDate = new DateTime(2026, 11, 1),
        }));
    }

    [Fact]
    public async Task Validation_and_delete_keeps_movements()
    {
        await using var db = await TestDb.CreateAsync();
        var (account, luz, sueldo) = await SetupAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(() => db.Recurring.CreateAsync(new RecurringItem
            { Name = "Luz", Type = TxType.Expense, CategoryId = sueldo.Id, AccountId = account, EstimatedAmount = 1, StartDate = Today }));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Recurring.CreateAsync(new RecurringItem
            { Name = "Luz", Type = TxType.Expense, CategoryId = luz.Id, AccountId = account, EstimatedAmount = 0, StartDate = Today }));

        var id = await db.Recurring.CreateAsync(new RecurringItem
        {
            Name = "Luz", Type = TxType.Expense, CategoryId = luz.Id, AccountId = account, VariableAmount = true,
            Frequency = Frequency.Bimonthly, StartDate = Today,
        });
        var txId = await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 500, AccountId = account, CategoryId = luz.Id, Date = Today, RecurringItemId = id, DueDate = Today });

        await db.Recurring.DeleteAsync(id);
        var tx = (await db.Transactions.QueryAsync(new TxFilter())).Single(t => t.Id == txId);
        Assert.Null(tx.RecurringItemId);
        Assert.Empty(await db.Recurring.GetAllAsync(includeArchived: true));
    }

    [Fact]
    public async Task Installments_split_amount_and_future_ones_do_not_affect_todays_balance()
    {
        await using var db = await TestDb.CreateAsync();
        var card = await db.Accounts.CreateAsync(new Account { Name = "Visa", Type = AccountType.CreditCard });
        var ropa = (await db.Categories.GetAsync(CategoryKind.Expense)).First(c => c.Name.StartsWith("Ropa"));

        var group = await db.Transactions.AddInstallmentsAsync(new Transaction
        {
            Type = TxType.Expense, Amount = 100_000_01, AccountId = card, CategoryId = ropa.Id,
            Date = Today, Note = "Zapatillas",
        }, 3);

        var cuotas = (await db.Transactions.QueryAsync(new TxFilter())).Where(t => t.InstallmentGroup == group)
            .OrderBy(t => t.InstallmentNumber).ToList();
        Assert.Equal(3, cuotas.Count);
        Assert.Equal([33_333_35, 33_333_33, 33_333_33], cuotas.Select(c => c.Amount));
        Assert.Equal([Today, Today.AddMonths(1), Today.AddMonths(2)], cuotas.Select(c => c.Date));
        Assert.All(cuotas, c => Assert.Equal(3, c.InstallmentCount));

        var balance = (await db.Accounts.GetAllWithBalancesAsync(asOf: Today)).Single(b => b.Account.Id == card);
        Assert.Equal(-33_333_35, balance.Balance);
        Assert.Equal(-66_666_66, balance.Future);

        await db.Transactions.DeleteInstallmentGroupAsync(group);
        Assert.DoesNotContain(await db.Transactions.QueryAsync(new TxFilter()), t => t.InstallmentGroup == group);
    }
}
