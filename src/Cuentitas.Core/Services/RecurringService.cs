using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

public enum DueStatus
{
    /// <summary>Venció y no se pagó.</summary>
    Overdue,

    /// <summary>Vence dentro de los días de aviso (o hoy).</summary>
    DueSoon,

    /// <summary>Vence más adelante.</summary>
    Upcoming,

    Paid,
    Skipped,
}

/// <summary>Un vencimiento concreto de un pago/cobro recurrente.</summary>
public sealed record DueOccurrence(RecurringItem Item, DateTime DueDate, DueStatus Status, Transaction? Payment)
{
    public bool IsPending => Status is DueStatus.Overdue or DueStatus.DueSoon or DueStatus.Upcoming;

    /// <summary>Días hasta el vencimiento (negativo si ya venció).</summary>
    public int DaysFrom(DateTime today) => (DueDate.Date - today.Date).Days;
}

/// <summary>Cálculo de fechas de vencimiento. Puro, sin base de datos (fácil de testear).</summary>
public static class Schedule
{
    public static int Months(Frequency f) => (int)f;

    /// <summary>Vencimiento número <paramref name="index"/> (0 = el primero).</summary>
    public static DateTime DueDate(RecurringItem item, int index)
    {
        var start = item.StartDate.Date;
        var month = new DateTime(start.Year, start.Month, 1).AddMonths(index * Months(item.Frequency));
        var day = Math.Min(start.Day, DateTime.DaysInMonth(month.Year, month.Month));
        return month.AddDays(day - 1);
    }

    /// <summary>Vencimientos entre <paramref name="from"/> y <paramref name="to"/> (inclusive).</summary>
    public static IEnumerable<DateTime> DueDates(RecurringItem item, DateTime from, DateTime to)
    {
        from = from.Date;
        to = to.Date;
        if (item.EndDate is { } end && end.Date < to) to = end.Date;
        if (to < item.StartDate.Date) yield break;

        // Salta directo cerca de "from" en lugar de recorrer desde el inicio.
        var monthsDiff = (from.Year - item.StartDate.Year) * 12 + from.Month - item.StartDate.Month;
        var index = Math.Max(0, monthsDiff / Months(item.Frequency) - 1);
        while (true)
        {
            var d = DueDate(item, index++);
            if (d > to) yield break;
            if (d >= from) yield return d;
        }
    }

    /// <summary>Próximo vencimiento a partir de <paramref name="today"/> (inclusive), o null si terminó.</summary>
    public static DateTime? Next(RecurringItem item, DateTime today) =>
        DueDates(item, today, today.AddMonths(Months(item.Frequency) + 1)).Cast<DateTime?>().FirstOrDefault();

    /// <summary>Primer vencimiento sugerido para un día del mes: este mes si todavía no pasó, si no el próximo.</summary>
    public static DateTime FirstDueFor(int dayOfMonth, DateTime today)
    {
        DateTime For(DateTime month) =>
            month.AddDays(Math.Min(dayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var candidate = For(thisMonth);
        return candidate >= today.Date ? candidate : For(thisMonth.AddMonths(1));
    }

    public static string Label(Frequency f) => f switch
    {
        Frequency.Monthly => "Mensual",
        Frequency.Bimonthly => "Bimestral",
        Frequency.Quarterly => "Trimestral",
        Frequency.Semiannual => "Semestral",
        Frequency.Yearly => "Anual",
        _ => f.ToString(),
    };
}

public sealed class RecurringService(IDbContextFactory<CuentitasDbContext> factory, DataChanges changes)
{
    /// <summary>Cuántos meses hacia atrás se buscan vencimientos impagos.</summary>
    public const int OverdueLookbackMonths = 12;

    public async Task<List<RecurringItem>> GetAllAsync(bool includeArchived = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.RecurringItems.AsNoTracking()
            .Include(r => r.Category).Include(r => r.Account)
            .Where(r => includeArchived || !r.Archived)
            .OrderBy(r => r.Type).ThenBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<RecurringItem?> GetAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.RecurringItems.AsNoTracking()
            .Include(r => r.Category).Include(r => r.Account)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    /// <summary>
    /// Vencimientos de los pagos/cobros activos entre <paramref name="from"/> y <paramref name="to"/>.
    /// Con <paramref name="includeOverdue"/> suma los impagos anteriores a <paramref name="from"/>
    /// (hasta <see cref="OverdueLookbackMonths"/> meses).
    /// </summary>
    public async Task<List<DueOccurrence>> GetAgendaAsync(DateTime today, DateTime from, DateTime to,
        bool includeOverdue = true)
    {
        today = today.Date;
        await using var db = await factory.CreateDbContextAsync();
        var items = await db.RecurringItems.AsNoTracking()
            .Include(r => r.Category).Include(r => r.Account)
            .Where(r => !r.Archived)
            .ToListAsync();
        if (items.Count == 0) return [];

        var scanFrom = includeOverdue ? Min(from.Date, today.AddMonths(-OverdueLookbackMonths)) : from.Date;
        var ids = items.Select(i => i.Id).ToList();

        var payments = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.RecurringItemId != null && ids.Contains(t.RecurringItemId.Value) && t.DueDate != null)
            .ToListAsync();
        var paid = payments.GroupBy(t => (t.RecurringItemId!.Value, t.DueDate!.Value.Date))
            .ToDictionary(g => g.Key, g => g.First());
        var skipped = (await db.RecurringSkips.AsNoTracking().Where(s => ids.Contains(s.RecurringItemId)).ToListAsync())
            .Select(s => (s.RecurringItemId, s.DueDate.Date)).ToHashSet();

        var result = new List<DueOccurrence>();
        foreach (var item in items)
        {
            foreach (var due in Schedule.DueDates(item, scanFrom, to))
            {
                var key = (item.Id, due);
                DueStatus status;
                paid.TryGetValue(key, out var payment);
                if (payment is not null) status = DueStatus.Paid;
                else if (skipped.Contains(key)) status = DueStatus.Skipped;
                else if (due < today) status = DueStatus.Overdue;
                else if (due <= today.AddDays(Math.Max(0, item.RemindDaysBefore))) status = DueStatus.DueSoon;
                else status = DueStatus.Upcoming;

                // Antes de "from" sólo interesan los impagos.
                if (due < from.Date && status != DueStatus.Overdue) continue;
                result.Add(new DueOccurrence(item, due, status, payment));
            }
        }
        return result.OrderBy(o => o.DueDate).ThenBy(o => o.Item.Name).ToList();
    }

    /// <summary>Lo que hay que avisar hoy: vencidos + los que vencen dentro de sus días de aviso.</summary>
    public async Task<List<DueOccurrence>> GetRemindersAsync(DateTime today) =>
        (await GetAgendaAsync(today, today, today.AddDays(31)))
        .Where(o => o.Status is DueStatus.Overdue or DueStatus.DueSoon)
        .ToList();

    public async Task<int> CreateAsync(RecurringItem item)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ValidateAsync(db, item);
        item.Id = 0;
        item.Category = null;
        item.Account = null;
        item.CreatedAt = DateTime.Now;
        db.RecurringItems.Add(item);
        await db.SaveChangesAsync();
        changes.Notify();
        return item.Id;
    }

    public async Task UpdateAsync(RecurringItem item)
    {
        await using var db = await factory.CreateDbContextAsync();
        var existing = await db.RecurringItems.FindAsync(item.Id)
                       ?? throw new InvalidOperationException("El pago recurrente no existe.");
        await ValidateAsync(db, item);
        existing.Name = item.Name;
        existing.Type = item.Type;
        existing.CategoryId = item.CategoryId;
        existing.AccountId = item.AccountId;
        existing.EstimatedAmount = item.EstimatedAmount;
        existing.VariableAmount = item.VariableAmount;
        existing.Frequency = item.Frequency;
        existing.StartDate = item.StartDate;
        existing.EndDate = item.EndDate;
        existing.RemindDaysBefore = item.RemindDaysBefore;
        existing.Note = item.Note;
        existing.Archived = item.Archived;
        await db.SaveChangesAsync();
        changes.Notify();
    }

    public async Task SetArchivedAsync(int id, bool archived)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.RecurringItems.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Archived, archived));
        changes.Notify();
    }

    /// <summary>Borra el pago recurrente. Los movimientos ya registrados se conservan (sin vínculo).</summary>
    public async Task DeleteAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Transactions.Where(t => t.RecurringItemId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RecurringItemId, (int?)null).SetProperty(t => t.DueDate, (DateTime?)null));
        await db.RecurringItems.Where(r => r.Id == id).ExecuteDeleteAsync();
        changes.Notify();
    }

    /// <summary>Marca un vencimiento como omitido ("este mes no corresponde").</summary>
    public async Task SkipAsync(int itemId, DateTime dueDate)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.RecurringSkips.AnyAsync(s => s.RecurringItemId == itemId && s.DueDate == dueDate.Date))
        {
            db.RecurringSkips.Add(new RecurringSkip { RecurringItemId = itemId, DueDate = dueDate.Date });
            await db.SaveChangesAsync();
        }
        changes.Notify();
    }

    public async Task UnskipAsync(int itemId, DateTime dueDate)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.RecurringSkips.Where(s => s.RecurringItemId == itemId && s.DueDate == dueDate.Date).ExecuteDeleteAsync();
        changes.Notify();
    }

    private static async Task ValidateAsync(CuentitasDbContext db, RecurringItem item)
    {
        item.Name = (item.Name ?? "").Trim();
        item.Note = (item.Note ?? "").Trim();
        item.StartDate = item.StartDate.Date;
        item.EndDate = item.EndDate?.Date;
        if (item.Name.Length == 0) throw new ArgumentException("Poné un nombre (ej.: Luz, Expensas, Sueldo).");
        if (item.Name.Length > 60) throw new ArgumentException("El nombre es demasiado largo (máx. 60).");
        if (item.Type == TxType.Transfer) throw new ArgumentException("Un pago recurrente es un gasto o un ingreso.");
        if (item.EstimatedAmount < 0) throw new ArgumentException("El monto no puede ser negativo.");
        if (item.EstimatedAmount == 0 && !item.VariableAmount)
            throw new ArgumentException("Indicá el monto, o marcá que el monto varía.");
        if (item.EndDate is { } end && end < item.StartDate)
            throw new ArgumentException("La fecha de fin es anterior al primer vencimiento.");
        if (item.RemindDaysBefore is < 0 or > 30) throw new ArgumentException("El aviso tiene que ser entre 0 y 30 días antes.");
        if (!Enum.IsDefined(item.Frequency)) throw new ArgumentException("Frecuencia inválida.");

        if (!await db.Accounts.AnyAsync(a => a.Id == item.AccountId)) throw new ArgumentException("Elegí una cuenta.");
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == item.CategoryId)
                       ?? throw new ArgumentException("Elegí una categoría.");
        var expected = item.Type == TxType.Expense ? CategoryKind.Expense : CategoryKind.Income;
        if (category.Kind != expected) throw new ArgumentException("La categoría no corresponde (gasto/ingreso).");
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
