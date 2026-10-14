using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;

namespace Cuentitas.Core.Tests;

public class ServiceTests
{
    private static readonly DateTime Today = new(2026, 10, 14);

    private static async Task<(int ars, int usd, Category food, Category salary)> SetupAsync(TestDb db)
    {
        var ars = await db.Accounts.CreateAsync(new Account
        {
            Name = "Banco", Type = AccountType.Bank, Currency = Currency.ARS, InitialBalance = 100_000_00,
        });
        var usd = await db.Accounts.CreateAsync(new Account
        {
            Name = "Dólares", Type = AccountType.Cash, Currency = Currency.USD,
        });
        var cats = await db.Categories.GetAsync();
        return (ars, usd, cats.First(c => c.Name == "Delivery"), cats.First(c => c.Name == "Sueldo"));
    }

    [Fact]
    public async Task Seed_creates_default_account_and_categories_once()
    {
        await using var db = await TestDb.CreateAsync();
        await Cuentitas.Core.Data.Database.InitializeAsync(db.Factory); // segunda vez: no duplica

        var accounts = await db.Accounts.GetActiveAsync();
        Assert.Single(accounts);
        Assert.Equal("Efectivo", accounts[0].Name);
        Assert.Equal(37, (await db.Categories.GetAsync()).Count);
    }

    [Fact]
    public async Task Seed_uses_only_known_icon_keys()
    {
        await using var db = await TestDb.CreateAsync();
        var used = (await db.Categories.GetAsync()).Select(c => c.Icon)
            .Concat((await db.Accounts.GetActiveAsync()).Select(a => a.Icon));
        Assert.All(used, key => Assert.Contains(key, IconKeys.All));
    }

    [Fact]
    public async Task Balances_follow_income_expense_and_transfers()
    {
        await using var db = await TestDb.CreateAsync();
        var (ars, usd, food, salary) = await SetupAsync(db);

        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Income, Amount = 900_000_00, AccountId = ars, CategoryId = salary.Id, Date = Today });
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 15_000_00, AccountId = ars, CategoryId = food.Id, Date = Today });
        // Compra de dólares: salen $ 120.000, entran US$ 100.
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Transfer, Amount = 120_000_00, AccountId = ars, ToAccountId = usd, ToAmount = 100_00, Date = Today });

        var balances = (await db.Accounts.GetAllWithBalancesAsync()).ToDictionary(b => b.Account.Id, b => b.Balance);
        Assert.Equal(100_000_00 + 900_000_00 - 15_000_00 - 120_000_00, balances[ars]);
        Assert.Equal(100_00, balances[usd]);

        var worth = await db.Summary.GetNetWorthAsync();
        Assert.Equal(865_000_00, worth[Currency.ARS]);
        Assert.Equal(100_00, worth[Currency.USD]);
    }

    [Fact]
    public async Task Month_totals_exclude_transfers_and_track_optional_spending()
    {
        await using var db = await TestDb.CreateAsync();
        var (ars, usd, food, salary) = await SetupAsync(db);
        var supermarket = (await db.Categories.GetAsync(CategoryKind.Expense)).First(c => c.Name == "Supermercado");

        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Income, Amount = 500_000_00, AccountId = ars, CategoryId = salary.Id, Date = Today });
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 20_000_00, AccountId = ars, CategoryId = food.Id, Date = Today });
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 80_000_00, AccountId = ars, CategoryId = supermarket.Id, Date = Today });
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Transfer, Amount = 50_000_00, AccountId = ars, ToAccountId = usd, ToAmount = 40_00, Date = Today });
        // Fuera del mes: no cuenta.
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 1_000_00, AccountId = ars, CategoryId = food.Id, Date = new DateTime(2026, 9, 30) });

        var (from, to) = SummaryService.MonthRange(2026, 10);
        var totals = (await db.Summary.GetTotalsAsync(from, to))[Currency.ARS];
        Assert.Equal(500_000_00, totals.Income);
        Assert.Equal(100_000_00, totals.Expense);
        Assert.Equal(20_000_00, totals.OptionalExpense); // Delivery es prescindible por defecto
        Assert.Equal(0.8, totals.SavingsRate!.Value, 3);

        var byCat = await db.Summary.GetExpensesByCategoryAsync(from, to);
        Assert.Equal(["Supermercado", "Delivery"], byCat.Select(c => c.Category.Name));
    }

    [Fact]
    public async Task Transfer_same_currency_copies_amount_and_cross_currency_requires_target_amount()
    {
        await using var db = await TestDb.CreateAsync();
        var (ars, usd, _, _) = await SetupAsync(db);
        var cash = (await db.Accounts.GetActiveAsync()).First(a => a.Name == "Efectivo").Id;

        var id = await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Transfer, Amount = 10_000_00, AccountId = ars, ToAccountId = cash, ToAmount = 1, Date = Today, DollarType = DollarType.Blue });
        var tx = (await db.Transactions.QueryAsync(new TxFilter())).Single(t => t.Id == id);
        Assert.Equal(10_000_00, tx.ToAmount);
        Assert.Null(tx.DollarType); // misma moneda: no es compra de dólares

        var buyId = await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Transfer, Amount = 130_000_00, AccountId = ars, ToAccountId = usd, ToAmount = 100_00, Date = Today, DollarType = DollarType.Mep });
        var buy = (await db.Transactions.QueryAsync(new TxFilter())).Single(t => t.Id == buyId);
        Assert.Equal(DollarType.Mep, buy.DollarType);
        Assert.True(buy.IsCurrencyExchange);

        await Assert.ThrowsAsync<ArgumentException>(() => db.Transactions.AddAsync(new Transaction
            { Type = TxType.Transfer, Amount = 10_000_00, AccountId = ars, ToAccountId = usd, Date = Today }));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Transactions.AddAsync(new Transaction
            { Type = TxType.Transfer, Amount = 10_000_00, AccountId = ars, ToAccountId = ars, Date = Today }));
    }

    [Fact]
    public async Task Validation_rejects_bad_movements_and_defaults_necessity()
    {
        await using var db = await TestDb.CreateAsync();
        var (ars, _, food, salary) = await SetupAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(() => db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 0, AccountId = ars, CategoryId = food.Id, Date = Today }));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 100, AccountId = ars, CategoryId = salary.Id, Date = Today }));

        var id = await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 100, AccountId = ars, CategoryId = food.Id, Date = Today });
        var tx = (await db.Transactions.QueryAsync(new TxFilter())).Single(t => t.Id == id);
        Assert.Equal(Necessity.Optional, tx.Necessity);
    }

    [Fact]
    public async Task Account_with_movements_cannot_be_deleted_or_change_currency()
    {
        await using var db = await TestDb.CreateAsync();
        var (ars, _, food, _) = await SetupAsync(db);
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 100, AccountId = ars, CategoryId = food.Id, Date = Today });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Accounts.DeleteAsync(ars));
        var account = (await db.Accounts.GetActiveAsync()).Single(a => a.Id == ars);
        account.Currency = Currency.USD;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Accounts.UpdateAsync(account));
    }

    [Fact]
    public async Task Search_and_filters()
    {
        await using var db = await TestDb.CreateAsync();
        var (ars, _, food, salary) = await SetupAsync(db);
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 100, AccountId = ars, CategoryId = food.Id, Date = Today, Note = "Pizza viernes" });
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Income, Amount = 100, AccountId = ars, CategoryId = salary.Id, Date = Today });

        Assert.Single(await db.Transactions.QueryAsync(new TxFilter { Search = "pizza" }));
        Assert.Single(await db.Transactions.QueryAsync(new TxFilter { Search = "deliv" }));
        Assert.Single(await db.Transactions.QueryAsync(new TxFilter { Type = TxType.Income }));
        Assert.Equal(2, (await db.Transactions.QueryAsync(new TxFilter { AccountId = ars })).Count);
    }

    [Fact]
    public async Task Backup_creates_a_readable_copy()
    {
        await using var db = await TestDb.CreateAsync();
        var folder = Path.Combine(Path.GetTempPath(), "cuentitas-test-" + Guid.NewGuid());
        try
        {
            var path = await new BackupService(db.Factory).CreateAsync(folder);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Services_notify_changes()
    {
        await using var db = await TestDb.CreateAsync();
        var count = 0;
        db.Changes.Changed += (_, _) => count++;
        await SetupAsync(db);
        Assert.Equal(2, count);
    }
}
