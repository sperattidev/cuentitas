using Cuentitas.Core.Data;
using Cuentitas.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Tests;

/// <summary>Base SQLite en memoria con migraciones reales y datos iniciales, más los servicios.</summary>
public sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public IDbContextFactory<CuentitasDbContext> Factory { get; }
    public DataChanges Changes { get; } = new();
    public AccountService Accounts { get; }
    public CategoryService Categories { get; }
    public TransactionService Transactions { get; }
    public SummaryService Summary { get; }
    public RecurringService Recurring { get; }
    public SettingsService Settings { get; }
    public GoalService Goals { get; }

    private TestDb(SqliteConnection connection)
    {
        _connection = connection;
        var options = new DbContextOptionsBuilder<CuentitasDbContext>().UseSqlite(connection).Options;
        Factory = new Factory_(options);
        Accounts = new AccountService(Factory, Changes);
        Categories = new CategoryService(Factory, Changes);
        Transactions = new TransactionService(Factory, Changes);
        Summary = new SummaryService(Factory, Accounts);
        Recurring = new RecurringService(Factory, Changes);
        Settings = new SettingsService(Factory);
        Goals = new GoalService(Factory, Accounts, Settings, Changes);
    }

    public static async Task<TestDb> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var db = new TestDb(connection);
        await Database.InitializeAsync(db.Factory);
        return db;
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private sealed class Factory_(DbContextOptions<CuentitasDbContext> options) : IDbContextFactory<CuentitasDbContext>
    {
        public CuentitasDbContext CreateDbContext() => new(options);
    }
}
