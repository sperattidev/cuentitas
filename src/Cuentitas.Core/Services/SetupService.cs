using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

/// <summary>Una cuenta cargada en la bienvenida.</summary>
public sealed record SetupAccount(string Name, AccountType Type, Currency Currency, long Balance, string Icon, uint Color);

/// <summary>Un servicio cargado en la bienvenida (se crea como pago recurrente).</summary>
public sealed record SetupBill(string Name, string Category, Frequency Frequency, int Day, long Amount, bool Variable);

/// <summary>Todo lo que se carga en la pantalla de bienvenida.</summary>
public sealed record SetupData
{
    /// <summary>Plata en efectivo hoy (la cuenta "Efectivo" ya existe).</summary>
    public long CashBalance { get; init; }

    public IReadOnlyList<SetupAccount> Accounts { get; init; } = [];

    /// <summary>Sueldo neto mensual (0 = no cargar). Se cobra en <see cref="SalaryAccountIndex"/>.</summary>
    public long Salary { get; init; }
    public int SalaryDay { get; init; } = 1;

    /// <summary>Índice en <see cref="Accounts"/> donde cobrás; -1 = efectivo.</summary>
    public int SalaryAccountIndex { get; init; } = -1;

    /// <summary>También crear el aguinaldo (medio sueldo, junio y diciembre).</summary>
    public bool Aguinaldo { get; init; }

    public IReadOnlyList<SetupBill> Bills { get; init; } = [];

    /// <summary>Índice en <see cref="Accounts"/> con el que pagás los servicios; -1 = efectivo.</summary>
    public int BillsAccountIndex { get; init; } = -1;
}

/// <summary>Primera configuración (pantalla de bienvenida, v1.0).</summary>
public sealed class SetupService(IDbContextFactory<CuentitasDbContext> factory, SettingsService settings, DataChanges changes)
{
    public const string DoneKey = "onboarding.done";

    /// <summary>¿Hay que mostrar la bienvenida? Sólo si nunca se completó y no hay movimientos cargados.</summary>
    public async Task<bool> ShouldShowAsync()
    {
        if (await settings.GetAsync(DoneKey) is { Length: > 0 }) return false;
        await using var db = await factory.CreateDbContextAsync();
        return !await db.Transactions.AnyAsync();
    }

    public Task MarkDoneAsync() => settings.SetAsync(DoneKey, DateTime.Now.ToString("O"));

    public async Task ApplyAsync(SetupData data, DateTime today)
    {
        if (data.CashBalance < 0) throw new ArgumentException("El efectivo no puede ser negativo.");
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var cash = await db.Accounts.OrderBy(a => a.Id).FirstAsync(a => a.Type == AccountType.Cash);
        cash.InitialBalance = data.CashBalance;

        var order = await db.Accounts.MaxAsync(a => (int?)a.SortOrder) ?? 0;
        var created = new List<Account>();
        foreach (var a in data.Accounts.Where(a => !string.IsNullOrWhiteSpace(a.Name)))
        {
            var account = new Account
            {
                Name = a.Name.Trim(), Type = a.Type, Currency = a.Currency, InitialBalance = a.Balance,
                Icon = a.Icon, Color = a.Color, SortOrder = ++order,
            };
            db.Accounts.Add(account);
            created.Add(account);
        }
        await db.SaveChangesAsync();

        Account Pick(int index) => index >= 0 && index < created.Count ? created[index] : cash;
        var categories = await db.Categories.ToDictionaryAsync(c => c.Name);

        if (data.Salary > 0)
        {
            var account = Pick(data.SalaryAccountIndex);
            var start = Schedule.FirstDueFor(Math.Clamp(data.SalaryDay, 1, 31), today);
            db.RecurringItems.Add(new RecurringItem
            {
                Name = "Sueldo", Type = TxType.Income, CategoryId = categories["Sueldo"].Id, AccountId = account.Id,
                EstimatedAmount = data.Salary, Frequency = Frequency.Monthly, StartDate = start, RemindDaysBefore = 0,
            });
            if (data.Aguinaldo)
            {
                var june = new DateTime(today.Year, 6, 30);
                var december = new DateTime(today.Year, 12, 30);
                db.RecurringItems.Add(new RecurringItem
                {
                    Name = "Aguinaldo", Type = TxType.Income, CategoryId = categories["Aguinaldo"].Id, AccountId = account.Id,
                    EstimatedAmount = data.Salary / 2, Frequency = Frequency.Semiannual, RemindDaysBefore = 0,
                    StartDate = today <= june ? june : today <= december ? december : june.AddYears(1),
                });
            }
        }

        var billsAccount = Pick(data.BillsAccountIndex);
        foreach (var b in data.Bills)
        {
            if (!categories.TryGetValue(b.Category, out var category)) category = categories["Otros gastos"];
            if (b.Amount <= 0 && !b.Variable) continue;
            db.RecurringItems.Add(new RecurringItem
            {
                Name = b.Name, Type = TxType.Expense, CategoryId = category.Id, AccountId = billsAccount.Id,
                EstimatedAmount = Math.Max(0, b.Amount), VariableAmount = b.Variable || b.Amount <= 0,
                Frequency = b.Frequency, StartDate = Schedule.FirstDueFor(Math.Clamp(b.Day, 1, 31), today), RemindDaysBefore = 3,
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await MarkDoneAsync();
        changes.Notify();
    }
}
