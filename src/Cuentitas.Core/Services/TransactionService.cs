using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

/// <summary>Filtro para listar movimientos. Fechas inclusivas por día.</summary>
public sealed record TxFilter
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int? AccountId { get; init; }
    public int? CategoryId { get; init; }
    public TxType? Type { get; init; }
    public Necessity? Necessity { get; init; }
    public string? Search { get; init; }
    public int? Limit { get; init; }
}

public sealed class TransactionService(IDbContextFactory<CuentitasDbContext> factory, DataChanges changes)
{
    public async Task<List<Transaction>> QueryAsync(TxFilter filter)
    {
        await using var db = await factory.CreateDbContextAsync();
        IQueryable<Transaction> q = db.Transactions.AsNoTracking()
            .Include(t => t.Account).Include(t => t.ToAccount).Include(t => t.Category).Include(t => t.RecurringItem);

        if (filter.From is { } from) q = q.Where(t => t.Date >= from.Date);
        if (filter.To is { } to) q = q.Where(t => t.Date < to.Date.AddDays(1));
        if (filter.AccountId is { } acc) q = q.Where(t => t.AccountId == acc || t.ToAccountId == acc);
        if (filter.CategoryId is { } cat) q = q.Where(t => t.CategoryId == cat);
        if (filter.Type is { } type) q = q.Where(t => t.Type == type);
        if (filter.Necessity is { } nec) q = q.Where(t => t.Necessity == nec);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            q = q.Where(t => EF.Functions.Like(t.Note, pattern)
                             || (t.Category != null && EF.Functions.Like(t.Category.Name, pattern))
                             || EF.Functions.Like(t.Account!.Name, pattern));
        }

        q = q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id);
        if (filter.Limit is { } limit) q = q.Take(limit);
        return await q.ToListAsync();
    }

    public async Task<int> AddAsync(Transaction tx)
    {
        await using var db = await factory.CreateDbContextAsync();
        await NormalizeAndValidateAsync(db, tx);
        tx.Id = 0;
        tx.CreatedAt = tx.UpdatedAt = DateTime.Now;
        tx.Account = tx.ToAccount = null;
        tx.Category = null;
        tx.RecurringItem = null;
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();
        changes.Notify();
        return tx.Id;
    }

    public async Task UpdateAsync(Transaction tx)
    {
        await using var db = await factory.CreateDbContextAsync();
        var existing = await db.Transactions.FindAsync(tx.Id)
                       ?? throw new InvalidOperationException("El movimiento no existe.");
        await NormalizeAndValidateAsync(db, tx);

        existing.Type = tx.Type;
        existing.Amount = tx.Amount;
        existing.AccountId = tx.AccountId;
        existing.ToAccountId = tx.ToAccountId;
        existing.ToAmount = tx.ToAmount;
        existing.DollarType = tx.DollarType;
        existing.CategoryId = tx.CategoryId;
        existing.Date = tx.Date;
        existing.Note = tx.Note;
        existing.Necessity = tx.Necessity;
        existing.RecurringItemId = tx.RecurringItemId;
        existing.DueDate = tx.DueDate;
        // Las cuotas (InstallmentGroup/Number/Count) no se tocan al editar una cuota.
        existing.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        changes.Notify();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Transactions.Where(t => t.Id == id).ExecuteDeleteAsync();
        changes.Notify();
    }

    /// <summary>
    /// Reglas de consistencia de un movimiento (docs/MODELO_DE_DATOS.md § Reglas).
    /// Limpia los campos que no corresponden al tipo y lanza <see cref="ArgumentException"/> si algo no cierra.
    /// </summary>
    internal static async Task NormalizeAndValidateAsync(CuentitasDbContext db, Transaction tx)
    {
        if (tx.Amount <= 0) throw new ArgumentException("El monto tiene que ser mayor a cero.");
        tx.Date = tx.Date.Date;
        tx.Note = (tx.Note ?? "").Trim();
        if (tx.Note.Length > 500) throw new ArgumentException("La nota es demasiado larga (máx. 500).");

        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == tx.AccountId)
                      ?? throw new ArgumentException("Elegí una cuenta.");

        if (tx.Type == TxType.Transfer)
        {
            tx.CategoryId = null;
            tx.Necessity = null;
            tx.RecurringItemId = null;
            tx.DueDate = null;
            if (tx.ToAccountId is null) throw new ArgumentException("Elegí la cuenta destino.");
            if (tx.ToAccountId == tx.AccountId) throw new ArgumentException("La cuenta origen y destino son la misma.");
            var target = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == tx.ToAccountId)
                         ?? throw new ArgumentException("La cuenta destino no existe.");

            if (target.Currency == account.Currency)
            {
                tx.ToAmount = tx.Amount;
                tx.DollarType = null;
            }
            else if (tx.ToAmount is null or <= 0)
            {
                throw new ArgumentException($"Indicá cuánto recibís en {target.Currency}.");
            }
            return;
        }

        tx.ToAccountId = null;
        tx.ToAmount = null;
        tx.DollarType = null;
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == tx.CategoryId)
                       ?? throw new ArgumentException("Elegí una categoría.");
        var expectedKind = tx.Type == TxType.Expense ? CategoryKind.Expense : CategoryKind.Income;
        if (category.Kind != expectedKind)
            throw new ArgumentException("La categoría no corresponde al tipo de movimiento.");

        if (tx.Type == TxType.Income) tx.Necessity = null;
        else tx.Necessity ??= category.DefaultNecessity;

        await ValidateRecurringLinkAsync(db, tx);
    }

    /// <summary>Si el movimiento paga un vencimiento: el pago recurrente existe, coincide el tipo y no está pagado ya.</summary>
    private static async Task ValidateRecurringLinkAsync(CuentitasDbContext db, Transaction tx)
    {
        if (tx.RecurringItemId is null)
        {
            tx.DueDate = null;
            return;
        }
        if (tx.DueDate is null) throw new ArgumentException("Falta la fecha de vencimiento que se paga.");
        tx.DueDate = tx.DueDate.Value.Date;

        var item = await db.RecurringItems.AsNoTracking().FirstOrDefaultAsync(r => r.Id == tx.RecurringItemId)
                   ?? throw new ArgumentException("El pago recurrente no existe.");
        if (item.Type != tx.Type)
            throw new ArgumentException(item.Type == TxType.Income
                ? "Un cobro recurrente se registra como ingreso."
                : "Un pago recurrente se registra como gasto.");

        var dueDate = tx.DueDate.Value;
        if (await db.Transactions.AnyAsync(t => t.Id != tx.Id && t.RecurringItemId == tx.RecurringItemId && t.DueDate == dueDate))
            throw new ArgumentException($"«{item.Name}» con vencimiento {dueDate:dd/MM/yyyy} ya está registrado.");
    }

    /// <summary>
    /// Compra en cuotas: crea <paramref name="count"/> gastos mensuales a partir de la fecha de compra.
    /// El total se reparte en partes iguales; los centavos que sobran van a la primera cuota.
    /// Devuelve el identificador del grupo.
    /// </summary>
    public async Task<string> AddInstallmentsAsync(Transaction purchase, int count)
    {
        if (count is < 2 or > 60) throw new ArgumentException("Las cuotas tienen que ser entre 2 y 60.");
        if (purchase.Type != TxType.Expense) throw new ArgumentException("Sólo los gastos se pueden pagar en cuotas.");
        purchase.RecurringItemId = null;

        await using var db = await factory.CreateDbContextAsync();
        await NormalizeAndValidateAsync(db, purchase);
        if (purchase.Amount < count) throw new ArgumentException("El monto es demasiado chico para esas cuotas.");

        var group = Guid.NewGuid().ToString();
        var each = purchase.Amount / count;
        var first = each + purchase.Amount % count;
        for (var i = 0; i < count; i++)
        {
            db.Transactions.Add(new Transaction
            {
                Type = TxType.Expense,
                Amount = i == 0 ? first : each,
                AccountId = purchase.AccountId,
                CategoryId = purchase.CategoryId,
                Necessity = purchase.Necessity,
                Note = purchase.Note,
                Date = purchase.Date.AddMonths(i),
                InstallmentGroup = group,
                InstallmentNumber = i + 1,
                InstallmentCount = count,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
            });
        }
        await db.SaveChangesAsync();
        changes.Notify();
        return group;
    }

    /// <summary>Borra todas las cuotas de una compra.</summary>
    public async Task DeleteInstallmentGroupAsync(string group)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Transactions.Where(t => t.InstallmentGroup == group).ExecuteDeleteAsync();
        changes.Notify();
    }
}
