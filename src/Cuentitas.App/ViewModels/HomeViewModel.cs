using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cuentitas.App.Helpers;
using Cuentitas.Core;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Cuentitas.App.ViewModels;

/// <summary>Barra de "en qué se fue la plata".</summary>
public sealed class CategoryBar(CategoryTotal total, long max, long monthExpense)
{
    public string IconKey => total.Category.Icon;
    public uint Color => total.Category.Color;
    public string Name => total.Category.Name;
    public string AmountText => Money.Format(total.Total, compact: true);
    public double Percent => max > 0 ? 100.0 * total.Total / max : 0;
    public string ShareText => monthExpense > 0 ? $"{100.0 * total.Total / monthExpense:0}%" : "";
    public Brush BarBrush => Ui.Brush(total.Category.Color);
}

public sealed partial class HomeViewModel(
    DataChanges changes,
    SummaryService summary,
    AccountService accounts,
    TransactionService transactions,
    RecurringService recurring,
    GoalService goals,
    QuoteService quotes,
    BudgetService budgets) : PageViewModel(changes)
{
    /// <summary>Presupuestos del mes al 80 % o más (máximo 3).</summary>
    public ObservableCollection<BudgetRow> BudgetAlerts { get; } = [];

    [ObservableProperty] public partial Visibility BudgetsVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial string TotalEquivalentText { get; set; } = "";
    [ObservableProperty] public partial string QuoteHint { get; set; } = "";
    [ObservableProperty] public partial Visibility EquivalentVisibility { get; set; } = Visibility.Collapsed;

    /// <summary>Hasta 3 metas activas con su progreso.</summary>
    public ObservableCollection<GoalCard> Goals { get; } = [];

    [ObservableProperty] public partial Visibility GoalsVisibility { get; set; } = Visibility.Collapsed;

    public ObservableCollection<TransactionItem> Recent { get; } = [];

    /// <summary>Vencidos y próximos 7 días (máximo 5).</summary>
    public ObservableCollection<DueItem> UpcomingBills { get; } = [];

    [ObservableProperty] public partial Visibility BillsVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial string BillsTitle { get; set; } = "";
    public ObservableCollection<AccountOption> Accounts { get; } = [];
    public ObservableCollection<CategoryBar> TopCategories { get; } = [];

    public string Greeting
    {
        get
        {
            var h = DateTime.Now.Hour;
            return h < 6 ? "Buenas noches" : h < 13 ? "Buen día" : h < 20 ? "Buenas tardes" : "Buenas noches";
        }
    }

    public string MonthTitle => Ui.MonthTitle(DateTime.Today);

    [ObservableProperty] public partial string NetWorthArs { get; set; } = "";
    [ObservableProperty] public partial string NetWorthUsd { get; set; } = "";
    [ObservableProperty] public partial Visibility UsdVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial string IncomeText { get; set; } = "";
    [ObservableProperty] public partial string ExpenseText { get; set; } = "";
    [ObservableProperty] public partial string NetText { get; set; } = "";
    [ObservableProperty] public partial Brush? NetBrush { get; set; }
    [ObservableProperty] public partial string SavingsRateText { get; set; } = "";
    [ObservableProperty] public partial string OptionalText { get; set; } = "";
    [ObservableProperty] public partial string OptionalHint { get; set; } = "";
    [ObservableProperty] public partial double ExpenseRatio { get; set; }
    [ObservableProperty] public partial Visibility EmptyVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility CategoriesEmptyVisibility { get; set; } = Visibility.Collapsed;

    protected override async Task LoadAsync()
    {
        var today = DateTime.Today;
        var (from, to) = SummaryService.MonthRange(today.Year, today.Month);
        // Hasta hoy: las cuotas futuras del mes todavía no se gastaron.
        to = to < today ? to : today;

        var worth = await summary.GetNetWorthAsync();
        var balances = await accounts.GetAllWithBalancesAsync();
        var totals = (await summary.GetTotalsAsync(from, to))[Currency.ARS];
        var byCategory = await summary.GetExpensesByCategoryAsync(from, to);
        var recent = await transactions.QueryAsync(new TxFilter { Limit = 8, To = today });

        NetWorthArs = Money.Format(worth[Currency.ARS]);
        NetWorthUsd = Money.Format(worth[Currency.USD], Currency.USD);
        UsdVisibility = balances.Any(b => b.Account.Currency == Currency.USD) ? Visibility.Visible : Visibility.Collapsed;

        IncomeText = Money.Format(totals.Income, showSign: true);
        ExpenseText = Money.Format(-totals.Expense);
        NetText = Money.Format(totals.Net, showSign: true);
        NetBrush = Ui.ToneBrush(totals.Net >= 0 ? Tone.Income : Tone.Expense);
        SavingsRateText = totals.SavingsRate is { } r
            ? (r >= 0 ? $"Ahorraste el {r:P0} de lo que entró" : $"Gastaste {(-r):P0} más de lo que entró")
            : "Todavía no registraste ingresos este mes";
        ExpenseRatio = totals.Income > 0 ? Math.Min(100, 100.0 * totals.Expense / totals.Income) : (totals.Expense > 0 ? 100 : 0);

        OptionalText = Money.Format(totals.OptionalExpense);
        OptionalHint = totals.OptionalExpense > 0
            ? $"Es el {(double)totals.OptionalExpense / Math.Max(1, totals.Expense):P0} de tus gastos. Recortando la mitad ahorrarías {Money.Format(totals.OptionalExpense / 2, compact: true)} por mes."
            : "Sin gastos prescindibles este mes. ¡Bien!";

        Accounts.Clear();
        foreach (var b in balances) Accounts.Add(AccountOption.From(b));

        TopCategories.Clear();
        var max = byCategory.FirstOrDefault()?.Total ?? 0;
        foreach (var c in byCategory.Take(6)) TopCategories.Add(new CategoryBar(c, max, totals.Expense));
        CategoriesEmptyVisibility = TopCategories.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var due = (await recurring.GetAgendaAsync(today, today, today.AddDays(7)))
            .Where(o => o.IsPending).ToList();
        UpcomingBills.Clear();
        foreach (var o in due.Take(5)) UpcomingBills.Add(new DueItem(o, today));
        BillsVisibility = due.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var overdue = due.Count(o => o.Status == DueStatus.Overdue);
        BillsTitle = overdue switch
        {
            0 => "Próximos vencimientos",
            1 => "Vencimientos (1 vencido)",
            _ => $"Vencimientos ({overdue} vencidos)",
        };

        // Patrimonio total en pesos con la cotización guardada (sin esperar a internet).
        var snapshot = await quotes.GetCachedAsync();
        var valuation = await quotes.GetValuationTypeAsync();
        var quote = snapshot?.Get(valuation);
        EquivalentVisibility = worth[Currency.USD] != 0 ? Visibility.Visible : Visibility.Collapsed;
        if (quote is not null)
        {
            var total = worth[Currency.ARS] + (long)Math.Round(worth[Currency.USD] * quote.Buy);
            TotalEquivalentText = $"Todo junto: {Money.Format(total, compact: true)}";
            QuoteHint = $"Dólares al {DollarTypes.Label(valuation)} comprador {Money.FormatRate(quote.Buy)} · " +
                        $"{(quote.UpdatedAt.Date == today ? "hoy" : Ui.ShortDate(quote.UpdatedAt))} {quote.UpdatedAt:HH:mm}";
        }
        else
        {
            TotalEquivalentText = "";
            QuoteHint = "Sin cotización del dólar todavía (se actualiza sola con internet).";
        }

        BudgetAlerts.Clear();
        foreach (var b in (await budgets.GetAlertsAsync(today)).Take(3)) BudgetAlerts.Add(new BudgetRow(b, true));
        BudgetsVisibility = BudgetAlerts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var inflation = await goals.GetMonthlyInflationAsync();
        Goals.Clear();
        foreach (var g in (await goals.GetOverviewAsync(today)).Take(3)) Goals.Add(new GoalCard(g, null, 0, inflation));
        GoalsVisibility = Goals.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Recent.Clear();
        foreach (var t in recent) Recent.Add(new TransactionItem(t));
        EmptyVisibility = Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
