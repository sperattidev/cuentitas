using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Data;

/// <summary>
/// Datos de ejemplo para desarrollo y capturas. Sólo se cargan con <c>--demo</c> y
/// usando CUENTITAS_DATA_DIR (nunca sobre los datos reales). Ver docs/DESARROLLO.md.
/// Incluye: 3 meses de movimientos, pagos recurrentes (uno vencido, uno por vencer), sueldo,
/// compra de dólares MEP y una compra en 3 cuotas.
/// </summary>
public static class DemoData
{
    public static async Task LoadAsync(CuentitasDbContext db, DateTime today)
    {
        if (await db.Transactions.AnyAsync()) return;

        var cash = await db.Accounts.FirstAsync();
        cash.InitialBalance = 95_000_00;
        var bank = new Account { Name = "Caja de ahorro", Type = AccountType.Bank, Icon = "bank", Color = 0xFF2F6FB5, InitialBalance = 420_000_00, SortOrder = 1 };
        var mp = new Account { Name = "Mercado Pago", Type = AccountType.Wallet, Icon = "wallet", Color = 0xFF2B8FA8, InitialBalance = 58_000_00, SortOrder = 2 };
        var card = new Account { Name = "Tarjeta Visa", Type = AccountType.CreditCard, Icon = "card", Color = 0xFF6B5BB5, SortOrder = 3 };
        var usd = new Account { Name = "Dólares ahorro", Type = AccountType.Cash, Currency = Currency.USD, Icon = "dollar", Color = 0xFF3F8F5A, InitialBalance = 300_00, SortOrder = 4 };
        db.Accounts.AddRange(bank, mp, card, usd);
        await db.SaveChangesAsync();

        var cat = await db.Categories.ToDictionaryAsync(c => c.Name);
        var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-2);

        RecurringItem Rec(string name, TxType type, string category, Account acc, long amount, Frequency f, int day,
            bool variable = false, int remind = 3) => new()
        {
            Name = name, Type = type, CategoryId = cat[category].Id, AccountId = acc.Id, EstimatedAmount = amount,
            VariableAmount = variable, Frequency = f, RemindDaysBefore = remind,
            StartDate = firstMonth.AddDays(Math.Min(day, DateTime.DaysInMonth(firstMonth.Year, firstMonth.Month)) - 1),
        };

        var sueldo = Rec("Sueldo", TxType.Income, "Sueldo", bank, 1_250_000_00, Frequency.Monthly, 1);
        var alquiler = Rec("Alquiler", TxType.Expense, "Alquiler", bank, 380_000_00, Frequency.Monthly, 2);
        var expensas = Rec("Expensas", TxType.Expense, "Expensas", bank, 95_000_00, Frequency.Monthly, 2, variable: true);
        var luz = Rec("Luz (Edenor)", TxType.Expense, "Luz", mp, 42_000_00, Frequency.Bimonthly, 5, variable: true);
        var gas = Rec("Gas (Metrogas)", TxType.Expense, "Gas", mp, 19_000_00, Frequency.Bimonthly, 6, variable: true);
        var internet = Rec("Internet", TxType.Expense, "Internet y cable", card, 29_999_00, Frequency.Monthly, 7);
        var celular = Rec("Celular", TxType.Expense, "Celular", mp, 18_500_00, Frequency.Monthly, 8);
        var prepaga = Rec("Prepaga", TxType.Expense, "Prepaga / obra social", bank, 145_000_00, Frequency.Monthly, 10);
        var streaming = Rec("Netflix + Spotify", TxType.Expense, "Streaming y suscripciones", card, 11_990_00, Frequency.Monthly, 10);
        var monotributo = Rec("Monotributo", TxType.Expense, "Impuestos (monotributo, ganancias)", bank, 38_000_00, Frequency.Monthly, 16);
        var abl = Rec("ABL", TxType.Expense, "ABL e inmobiliario", bank, 14_200_00, Frequency.Monthly, 25);
        var aguinaldo = new RecurringItem
        {
            Name = "Aguinaldo", Type = TxType.Income, CategoryId = cat["Aguinaldo"].Id, AccountId = bank.Id,
            EstimatedAmount = 625_000_00, Frequency = Frequency.Semiannual, StartDate = new DateTime(today.Year, 12, 30),
        };
        db.RecurringItems.AddRange(sueldo, alquiler, expensas, luz, gas, internet, celular, prepaga, streaming, monotributo, abl, aguinaldo);
        await db.SaveChangesAsync();

        var txs = new List<Transaction>();

        void Add(TxType type, long amount, Account acc, string? category, DateTime date, string note = "",
            Account? to = null, long? toAmount = null, Necessity? necessity = null, DollarType? dollar = null,
            RecurringItem? pays = null, DateTime? due = null)
        {
            var c = category is null ? null : cat[category];
            txs.Add(new Transaction
            {
                Type = type, Amount = amount, AccountId = acc.Id, CategoryId = c?.Id, Date = date.Date, Note = note,
                ToAccountId = to?.Id, ToAmount = type == TxType.Transfer ? toAmount ?? amount : null,
                Necessity = type == TxType.Expense ? necessity ?? c?.DefaultNecessity : null,
                DollarType = dollar, RecurringItemId = pays?.Id, DueDate = pays is null ? null : (due ?? date).Date,
            });
        }

        for (var m = 2; m >= 0; m--)
        {
            var month = new DateTime(today.Year, today.Month, 1).AddMonths(-m);
            DateTime D(int day) => month.AddDays(Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
            bool Upto(int day) => m > 0 || day <= today.Day;
            var billMonth = m % 2 == 0; // luz y gas bimestrales

            if (Upto(1)) Add(TxType.Income, 1_250_000_00, bank, "Sueldo", D(1), "", pays: sueldo);
            if (Upto(2)) Add(TxType.Expense, 380_000_00, bank, "Alquiler", D(2), "", pays: alquiler);
            if (Upto(2)) Add(TxType.Expense, 93_400_00 + m * 1_500_00, bank, "Expensas", D(2), "", pays: expensas);
            if (Upto(3)) Add(TxType.Transfer, 120_000_00, bank, null, D(3), "Ahorro del mes", usd, 100_00, dollar: DollarType.Mep);
            if (Upto(4)) Add(TxType.Transfer, 60_000_00, bank, null, D(4), "", mp);
            if (billMonth && Upto(5)) Add(TxType.Expense, 42_300_00, mp, "Luz", D(4), "", pays: luz, due: D(5));
            if (billMonth && Upto(6)) Add(TxType.Expense, 18_900_00, mp, "Gas", D(6), "", pays: gas);
            if (Upto(7)) Add(TxType.Expense, 29_999_00, card, "Internet y cable", D(7), "", pays: internet);
            if (m > 0) Add(TxType.Expense, 18_500_00, mp, "Celular", D(8), "", pays: celular); // este mes: vencido
            if (Upto(8)) Add(TxType.Expense, 96_450_00, card, "Supermercado", D(8), "Compra grande");
            if (Upto(9)) Add(TxType.Expense, 14_500_00, mp, "Delivery", D(9), "Pizza viernes");
            if (Upto(10)) Add(TxType.Expense, 11_990_00, card, "Streaming y suscripciones", D(10), "", pays: streaming);
            if (Upto(10)) Add(TxType.Expense, 145_000_00, bank, "Prepaga / obra social", D(10), "", pays: prepaga);
            if (Upto(11)) Add(TxType.Expense, 8_500_00, cash, "Café y kiosco", D(11), "");
            if (Upto(12)) Add(TxType.Expense, 25_000_00, mp, "Transporte (SUBE)", D(12), "Carga");
            if (Upto(13)) Add(TxType.Expense, 32_800_00, card, "Salidas", D(13), "Cumple de Juli");
            if (Upto(13)) Add(TxType.Expense, 21_700_00, mp, "Delivery", D(13), "Sushi");
            if (Upto(14)) Add(TxType.Expense, 54_200_00, card, "Supermercado", D(14), "");
            if (Upto(14)) Add(TxType.Income, 85_000_00, mp, "Trabajos extra", D(14), "Diseño logo");
            if (m > 0) Add(TxType.Expense, 38_000_00, bank, "Impuestos (monotributo, ganancias)", D(16), "", pays: monotributo);
            if (Upto(18)) Add(TxType.Expense, 15_600_00, cash, "Farmacia", D(18), "");
            if (Upto(20)) Add(TxType.Transfer, 150_000_00, bank, null, D(20), "Pago resumen tarjeta", card);
            if (Upto(22)) Add(TxType.Expense, 12_300_00, mp, "Comida afuera", D(22), "Almuerzo");
            if (m > 0) Add(TxType.Expense, 14_200_00, bank, "ABL e inmobiliario", D(25), "", pays: abl);
            if (Upto(25)) Add(TxType.Expense, 61_000_00, card, "Supermercado", D(25), "");
        }

        // Zapatillas en 3 cuotas con la tarjeta, compradas el mes pasado.
        var group = Guid.NewGuid().ToString();
        var purchase = new DateTime(today.Year, today.Month, 1).AddMonths(-1).AddDays(15);
        for (var i = 0; i < 3; i++)
        {
            txs.Add(new Transaction
            {
                Type = TxType.Expense, Amount = 47_000_00, AccountId = card.Id, CategoryId = cat["Ropa y calzado"].Id,
                Necessity = Necessity.Optional, Note = "Zapatillas", Date = purchase.AddMonths(i),
                InstallmentGroup = group, InstallmentNumber = i + 1, InstallmentCount = 3,
            });
        }

        db.Transactions.AddRange(txs);
        await db.SaveChangesAsync();

        // Metas: una con aportes y fecha, otra sin fecha, y una en dólares con la cuenta de ahorro.
        var emergencia = new Goal
        {
            Name = "Fondo de emergencia", Icon = "heart", Color = 0xFF1F8A7A, TargetAmount = 2_400_000_00,
            TargetDate = today.AddMonths(8), CreatedAt = firstMonth,
        };
        var vacaciones = new Goal
        {
            Name = "Vacaciones en Bariloche", Icon = "plane", Color = 0xFF2F6FB5, TargetAmount = 1_800_000_00,
            CreatedAt = firstMonth,
        };
        var auto = new Goal
        {
            Name = "Auto", Icon = "car", Color = 0xFF56657A, Currency = Currency.USD, TargetAmount = 12_000_00,
            Tracking = GoalTracking.LinkedAccount, AccountId = usd.Id, AdjustForInflation = false,
            TargetDate = today.AddYears(3), CreatedAt = firstMonth,
        };
        db.Goals.AddRange(emergencia, vacaciones, auto);
        await db.SaveChangesAsync();
        db.GoalContributions.AddRange(
            new GoalContribution { GoalId = emergencia.Id, Amount = 300_000_00, Date = firstMonth.AddDays(2), Note = "Arranque" },
            new GoalContribution { GoalId = emergencia.Id, Amount = 250_000_00, Date = firstMonth.AddMonths(1).AddDays(2) },
            new GoalContribution { GoalId = emergencia.Id, Amount = 200_000_00, Date = today.AddDays(-10) },
            new GoalContribution { GoalId = vacaciones.Id, Amount = 150_000_00, Date = firstMonth.AddMonths(1).AddDays(5) });

        // Presupuestos: uno pasado, uno justo y uno holgado.
        db.Budgets.AddRange(
            new Budget { CategoryId = cat["Delivery"].Id, MonthlyLimit = 40_000_00 },
            new Budget { CategoryId = cat["Salidas"].Id, MonthlyLimit = 30_000_00 },
            new Budget { CategoryId = cat["Supermercado"].Id, MonthlyLimit = 320_000_00 });
        await db.SaveChangesAsync();
    }
}
