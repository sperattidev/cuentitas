using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

/// <summary>
/// Una cuenta con su saldo a hoy (centavos, en la moneda de la cuenta) y lo ya cargado con fecha
/// futura (ej. cuotas que faltan): <see cref="Future"/> negativo = compromisos por pagar.
/// </summary>
public sealed record AccountBalance(Account Account, long Balance, long Future = 0);

public sealed class AccountService(IDbContextFactory<CuentitasDbContext> factory, DataChanges changes)
{
    /// <summary>
    /// Saldo = saldo inicial + ingresos − gastos − transferencias salientes + transferencias entrantes
    /// (estas últimas en la moneda destino). Ver docs/MODELO_DE_DATOS.md § Saldos.
    /// </summary>
    /// <param name="asOf">Fecha del saldo (por defecto hoy). Los movimientos posteriores van a <see cref="AccountBalance.Future"/>.</param>
    public async Task<List<AccountBalance>> GetAllWithBalancesAsync(bool includeArchived = false, DateTime? asOf = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var limit = (asOf ?? DateTime.Today).Date.AddDays(1);

        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => includeArchived || !a.Archived)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
            .ToListAsync();

        var outgoing = await db.Transactions
            .GroupBy(t => new { t.AccountId, Future = t.Date >= limit })
            .Select(g => new { g.Key.AccountId, g.Key.Future, Delta = g.Sum(t => t.Type == TxType.Income ? t.Amount : -t.Amount) })
            .ToListAsync();

        var incoming = await db.Transactions
            .Where(t => t.Type == TxType.Transfer && t.ToAccountId != null)
            .GroupBy(t => new { AccountId = t.ToAccountId!.Value, Future = t.Date >= limit })
            .Select(g => new { g.Key.AccountId, g.Key.Future, Delta = g.Sum(t => t.ToAmount ?? 0) })
            .ToListAsync();

        var deltas = outgoing.Concat(incoming).ToList();
        long Sum(int id, bool future) => deltas.Where(d => d.AccountId == id && d.Future == future).Sum(d => d.Delta);

        return accounts
            .Select(a => new AccountBalance(a, a.InitialBalance + Sum(a.Id, false), Sum(a.Id, true)))
            .ToList();
    }

    public async Task<List<Account>> GetActiveAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Accounts.AsNoTracking().Where(a => !a.Archived)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Id).ToListAsync();
    }

    public async Task<int> CreateAsync(Account account)
    {
        Validate(account);
        await using var db = await factory.CreateDbContextAsync();
        account.Id = 0;
        account.Name = account.Name.Trim();
        account.SortOrder = (await db.Accounts.MaxAsync(a => (int?)a.SortOrder) ?? 0) + 1;
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        changes.Notify();
        return account.Id;
    }

    public async Task UpdateAsync(Account account)
    {
        Validate(account);
        await using var db = await factory.CreateDbContextAsync();
        var existing = await db.Accounts.FindAsync(account.Id)
                       ?? throw new InvalidOperationException("La cuenta no existe.");

        if (existing.Currency != account.Currency && await HasTransactionsAsync(db, account.Id))
            throw new InvalidOperationException("No se puede cambiar la moneda de una cuenta con movimientos.");

        existing.Name = account.Name.Trim();
        existing.Type = account.Type;
        existing.Currency = account.Currency;
        existing.InitialBalance = account.InitialBalance;
        existing.Color = account.Color;
        existing.Icon = account.Icon;
        existing.IncludeInTotal = account.IncludeInTotal;
        existing.Archived = account.Archived;
        await db.SaveChangesAsync();
        changes.Notify();
    }

    public async Task SetArchivedAsync(int id, bool archived)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Archived, archived));
        changes.Notify();
    }

    public async Task<bool> HasTransactionsAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await HasTransactionsAsync(db, id);
    }

    /// <summary>Borra la cuenta. Sólo si no tiene movimientos; si no, hay que archivarla.</summary>
    public async Task DeleteAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (await HasTransactionsAsync(db, id))
            throw new InvalidOperationException("La cuenta tiene movimientos: archivala en lugar de borrarla.");
        await db.Accounts.Where(a => a.Id == id).ExecuteDeleteAsync();
        changes.Notify();
    }

    private static Task<bool> HasTransactionsAsync(CuentitasDbContext db, int id) =>
        db.Transactions.AnyAsync(t => t.AccountId == id || t.ToAccountId == id);

    private static void Validate(Account a)
    {
        if (string.IsNullOrWhiteSpace(a.Name)) throw new ArgumentException("La cuenta necesita un nombre.");
        if (a.Name.Trim().Length > 60) throw new ArgumentException("El nombre es demasiado largo (máx. 60).");
    }
}
