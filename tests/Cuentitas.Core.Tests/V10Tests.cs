using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Tests;

public class PinTests
{
    [Theory]
    [InlineData("12", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("1111", false)]
    [InlineData("1234", false)]
    [InlineData("4321", false)]
    [InlineData("2580", true)]
    [InlineData("739164", true)]
    public void Validate(string pin, bool ok) => Assert.Equal(ok, PinService.Validate(pin) is null);

    [Fact]
    public async Task Set_verify_and_disable()
    {
        await using var db = await TestDb.CreateAsync();
        var pins = new PinService(db.Settings);
        Assert.False(await pins.IsEnabledAsync());
        Assert.True(await pins.VerifyAsync("cualquiera")); // sin PIN, no bloquea

        await pins.SetPinAsync("2580");
        Assert.True(await pins.IsEnabledAsync());
        Assert.True(await pins.VerifyAsync("2580"));
        Assert.False(await pins.VerifyAsync("2581"));

        // Se guarda el hash, nunca el PIN.
        var stored = await db.Settings.GetAsync(PinService.Keys.Hash);
        Assert.StartsWith("v1:", stored);
        Assert.DoesNotContain("2580", stored);

        await pins.SetHelloEnabledAsync(true);
        await pins.DisableAsync();
        Assert.False(await pins.IsEnabledAsync());
        Assert.False(await pins.IsHelloEnabledAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => pins.SetPinAsync("1111"));
    }
}

public class SetupTests
{
    private static readonly DateTime Today = new(2026, 10, 14);

    [Fact]
    public async Task Apply_creates_accounts_salary_aguinaldo_and_bills()
    {
        await using var db = await TestDb.CreateAsync();
        var setup = new SetupService(db.Factory, db.Settings, db.Changes);
        Assert.True(await setup.ShouldShowAsync());

        await setup.ApplyAsync(new SetupData
        {
            CashBalance = 20_000_00,
            Accounts =
            [
                new SetupAccount("Caja de ahorro", AccountType.Bank, Currency.ARS, 350_000_00, "bank", 0xFF2F6FB5),
                new SetupAccount("Dólares", AccountType.Cash, Currency.USD, 500_00, "dollar", 0xFF3F8F5A),
                new SetupAccount("  ", AccountType.Wallet, Currency.ARS, 0, "wallet", 0),
            ],
            Salary = 1_200_000_00, SalaryDay = 5, SalaryAccountIndex = 0, Aguinaldo = true,
            Bills =
            [
                new SetupBill("Luz", "Luz", Frequency.Bimonthly, 15, 0, Variable: true),
                new SetupBill("Expensas", "Expensas", Frequency.Monthly, 10, 95_000_00, Variable: false),
                new SetupBill("Nada", "Gas", Frequency.Monthly, 10, 0, Variable: false), // sin monto ni variable: se omite
            ],
            BillsAccountIndex = 0,
        }, Today);

        var accounts = (await db.Accounts.GetAllWithBalancesAsync(asOf: Today)).ToDictionary(a => a.Account.Name);
        Assert.Equal(3, accounts.Count); // la fila vacía no se crea
        Assert.Equal(20_000_00, accounts["Efectivo"].Balance);
        Assert.Equal(500_00, accounts["Dólares"].Balance);

        var items = (await db.Recurring.GetAllAsync()).ToDictionary(r => r.Name);
        Assert.Equal(["Aguinaldo", "Expensas", "Luz", "Sueldo"], items.Keys.Order());
        Assert.Equal(new DateTime(2026, 11, 5), items["Sueldo"].StartDate); // el 5 de octubre ya pasó
        Assert.Equal(600_000_00, items["Aguinaldo"].EstimatedAmount);
        Assert.Equal(new DateTime(2026, 12, 30), items["Aguinaldo"].StartDate);
        Assert.True(items["Luz"].VariableAmount);
        Assert.Equal(accounts["Caja de ahorro"].Account.Id, items["Expensas"].AccountId);

        Assert.False(await setup.ShouldShowAsync());
    }

    [Fact]
    public async Task Not_shown_if_there_are_movements()
    {
        await using var db = await TestDb.CreateAsync();
        var account = (await db.Accounts.GetActiveAsync()).First().Id;
        var cat = (await db.Categories.GetAsync(CategoryKind.Expense)).First().Id;
        await db.Transactions.AddAsync(new Transaction
            { Type = TxType.Expense, Amount = 100, AccountId = account, CategoryId = cat, Date = Today });
        Assert.False(await new SetupService(db.Factory, db.Settings, db.Changes).ShouldShowAsync());
    }
}

public class RestoreTests
{
    [Fact]
    public async Task Backup_and_restore_round_trip_with_safety_copy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cuentitas-restore-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "cuentitas.db");
        try
        {
            var options = new DbContextOptionsBuilder<CuentitasDbContext>();
            Database.Configure(options, dbPath);
            var factory = new FileFactory(options.Options);
            await Database.InitializeAsync(factory);
            var changes = new DataChanges();
            var accounts = new AccountService(factory, changes);
            var backups = new BackupService(factory, Path.Combine(dir, "copias"));

            await accounts.CreateAsync(new Account { Name = "Banco", Type = AccountType.Bank });
            var copy = await backups.CreateAsync();
            var info = await BackupService.InspectAsync(copy);
            Assert.Equal(2, info.Accounts);

            // Después de la copia se agrega otra cuenta; al restaurar tiene que desaparecer.
            await accounts.CreateAsync(new Account { Name = "Mercado Pago", Type = AccountType.Wallet });
            Assert.Equal(3, (await accounts.GetActiveAsync()).Count);

            var safety = await backups.RestoreAsync(copy);
            Assert.Equal(["Efectivo", "Banco"], (await accounts.GetActiveAsync()).Select(a => a.Name));
            Assert.Equal(3, (await BackupService.InspectAsync(safety)).Accounts); // la copia de seguridad previa conserva todo

            var junk = Path.Combine(dir, "otro.txt");
            await File.WriteAllTextAsync(junk, "no soy una base");
            await Assert.ThrowsAsync<InvalidDataException>(() => BackupService.InspectAsync(junk));
            await Assert.ThrowsAsync<InvalidDataException>(() => backups.RestoreAsync(junk));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }

    private sealed class FileFactory(DbContextOptions<CuentitasDbContext> options) : IDbContextFactory<CuentitasDbContext>
    {
        public CuentitasDbContext CreateDbContext() => new(options);
    }
}
