using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cuentitas.App.Helpers;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Cuentitas.App.ViewModels;

/// <summary>Un vencimiento listo para mostrar.</summary>
public sealed class DueItem(DueOccurrence occurrence, DateTime today)
{
    public DueOccurrence Occurrence { get; } = occurrence;
    private RecurringItem Item => Occurrence.Item;

    public string IconKey => Item.Category?.Icon ?? "other";
    public uint Color => Item.Category?.Color ?? 0xFF6E7781;
    public string Name => Item.Name;
    public bool IsIncome => Item.Type == TxType.Income;

    public string Subtitle =>
        $"Vto. {Ui.ShortDate(Occurrence.DueDate)} · {Item.Account?.Name}" +
        (Occurrence.Payment is { } p && p.Date.Date != Occurrence.DueDate ? $" · pagado el {Ui.ShortDate(p.Date)}" : "");

    public string StatusText => DueText.Relative(Occurrence, today);
    public string AmountText => DueText.Amount(Occurrence);

    public Brush StatusBrush => Occurrence.Status switch
    {
        DueStatus.Overdue => Ui.ToneBrush(Tone.Expense),
        DueStatus.DueSoon => Ui.ToneBrush(Tone.Optional),
        DueStatus.Paid => Ui.ToneBrush(Tone.Income),
        _ => Ui.ThemeResource("SecondaryTextBrush"),
    };

    public Brush AmountBrush => Ui.ToneBrush(IsIncome ? Tone.Income : Tone.Neutral);

    public bool IsPending => Occurrence.IsPending;
    public Visibility PayVisibility => IsPending ? Visibility.Visible : Visibility.Collapsed;
    public string PayText => IsIncome ? "Cobrar" : "Pagar";
    public double RowOpacity => Occurrence.Status is DueStatus.Paid or DueStatus.Skipped ? 0.6 : 1;
}

/// <summary>Grupo de la agenda (Vencidos, Próximos 7 días…).</summary>
public sealed class DueGroup(string title, IEnumerable<DueItem> items) : ObservableCollection<DueItem>(items)
{
    public string Title { get; } = title;

    public string TotalText
    {
        get
        {
            var pending = this.Where(i => i.IsPending && !i.IsIncome).ToList();
            if (pending.Count == 0) return "";
            var byCurrency = pending.GroupBy(i => i.Occurrence.Item.Account?.Currency ?? Currency.ARS)
                .Select(g => Money.Format(g.Sum(i => i.Occurrence.Item.EstimatedAmount), g.Key, compact: true));
            return "A pagar: " + string.Join(" + ", byCurrency);
        }
    }
}

/// <summary>Un pago/cobro recurrente en la lista de configurados.</summary>
public sealed class RecurringRow(RecurringItem item, DateTime today)
{
    public RecurringItem Item { get; } = item;
    public string IconKey => Item.Category?.Icon ?? "other";
    public uint Color => Item.Category?.Color ?? 0xFF6E7781;
    public string Name => Item.Name;
    public string Subtitle => $"{DueText.Frequency(Item)} · {Item.Account?.Name}";
    public string AmountText => Item.EstimatedAmount > 0
        ? (Item.VariableAmount ? "≈ " : "") + Money.Format(Item.EstimatedAmount, Item.Account?.Currency ?? Currency.ARS, compact: true)
        : "Variable";
    public string NextText => Schedule.Next(Item, today) is { } next ? $"Próximo: {Ui.ShortDate(next)}" : "Terminado";
}

public sealed partial class BillsViewModel(DataChanges changes, RecurringService recurring) : PageViewModel(changes)
{
    public ObservableCollection<DueGroup> Groups { get; } = [];
    public ObservableCollection<RecurringRow> Payments { get; } = [];
    public ObservableCollection<RecurringRow> Incomes { get; } = [];

    [ObservableProperty] public partial DateTime Month { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] public partial string OverdueText { get; set; } = "";
    [ObservableProperty] public partial string OverdueCount { get; set; } = "";
    [ObservableProperty] public partial string WeekText { get; set; } = "";
    [ObservableProperty] public partial string WeekCount { get; set; } = "";
    [ObservableProperty] public partial string MonthPendingText { get; set; } = "";
    [ObservableProperty] public partial string MonthPaidText { get; set; } = "";
    [ObservableProperty] public partial string IncomeText { get; set; } = "";
    [ObservableProperty] public partial Visibility EmptyVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility ContentVisibility { get; set; } = Visibility.Visible;
    [ObservableProperty] public partial Visibility AgendaEmptyVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility IncomesVisibility { get; set; } = Visibility.Collapsed;

    public string MonthTitle => Ui.MonthTitle(Month);
    private bool IsCurrentMonth => Month == new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    protected override async Task LoadAsync()
    {
        var today = DateTime.Today;
        var all = await recurring.GetAllAsync();

        Payments.Clear();
        Incomes.Clear();
        foreach (var item in all)
            (item.Type == TxType.Income ? Incomes : Payments).Add(new RecurringRow(item, today));
        IncomesVisibility = Incomes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyVisibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ContentVisibility = all.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var (from, to) = SummaryService.MonthRange(Month.Year, Month.Month);
        var agenda = await recurring.GetAgendaAsync(today, from, to, includeOverdue: IsCurrentMonth);
        var items = agenda.Select(o => new DueItem(o, today)).ToList();

        // Tarjetas de resumen (pagos, en pesos).
        var overdue = items.Where(i => i.Occurrence.Status == DueStatus.Overdue && !i.IsIncome).ToList();
        var week = items.Where(i => i.IsPending && !i.IsIncome && i.Occurrence.Status != DueStatus.Overdue &&
                                    i.Occurrence.DueDate <= today.AddDays(7)).ToList();
        var monthPending = items.Where(i => i.IsPending && !i.IsIncome && i.Occurrence.DueDate >= from).ToList();
        var monthPaid = items.Where(i => i.Occurrence.Status == DueStatus.Paid && !i.IsIncome).ToList();

        OverdueCount = overdue.Count.ToString();
        OverdueText = overdue.Count == 0 ? "Nada vencido" : Sum(overdue);
        WeekCount = week.Count.ToString();
        WeekText = week.Count == 0 ? "Nada esta semana" : Sum(week);
        MonthPendingText = monthPending.Count switch
        {
            0 => "Todo pagado",
            var n when monthPending.All(i => i.Occurrence.Item.EstimatedAmount == 0) =>
                $"{n} {(n == 1 ? "pago" : "pagos")} a confirmar",
            var n => $"{Sum(monthPending)} en {n} {(n == 1 ? "pago" : "pagos")}",
        };
        MonthPaidText = monthPaid.Count == 0 ? "" : $"Ya pagaste {Sum(monthPaid, paid: true)}";
        var incomes = items.Where(i => i.IsIncome && i.IsPending && i.Occurrence.DueDate >= from).ToList();
        IncomeText = incomes.Count == 0 ? "" : $"Por cobrar este mes: {Sum(incomes)}";

        Groups.Clear();
        void Add(string title, IEnumerable<DueItem> list)
        {
            var l = list.ToList();
            if (l.Count > 0) Groups.Add(new DueGroup(title, l));
        }
        if (IsCurrentMonth)
        {
            Add("Vencidos", items.Where(i => i.Occurrence.Status == DueStatus.Overdue));
            Add("Próximos 7 días", items.Where(i => i.IsPending && i.Occurrence.Status != DueStatus.Overdue && i.Occurrence.DueDate <= today.AddDays(7)));
            Add("Más adelante este mes", items.Where(i => i.IsPending && i.Occurrence.DueDate > today.AddDays(7)));
        }
        else
        {
            Add(Month < today ? "Sin pagar" : "Pendientes", items.Where(i => i.IsPending));
        }
        Add("Pagados y cobrados", items.Where(i => i.Occurrence.Status == DueStatus.Paid));
        Add("Omitidos", items.Where(i => i.Occurrence.Status == DueStatus.Skipped));
        AgendaEmptyVisibility = Groups.Count == 0 && all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Sum(IEnumerable<DueItem> items, bool paid = false)
    {
        var list = items.ToList();
        if (!paid && list.All(i => i.Occurrence.Item.EstimatedAmount == 0)) return "Monto a confirmar";
        items = list;
        var parts = items.GroupBy(i => i.Occurrence.Item.Account?.Currency ?? Currency.ARS)
            .Select(g => Money.Format(g.Sum(i => paid && i.Occurrence.Payment is { } p ? p.Amount : i.Occurrence.Item.EstimatedAmount),
                g.Key, compact: true));
        return string.Join(" + ", parts);
    }

    [RelayCommand]
    private Task PreviousMonthAsync()
    {
        Month = Month.AddMonths(-1);
        return ReloadAsync();
    }

    [RelayCommand]
    private Task NextMonthAsync()
    {
        Month = Month.AddMonths(1);
        return ReloadAsync();
    }

    [RelayCommand]
    private Task CurrentMonthAsync()
    {
        Month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return ReloadAsync();
    }

    partial void OnMonthChanged(DateTime value) => OnPropertyChanged(nameof(MonthTitle));

    public Task SkipAsync(DueItem item) => recurring.SkipAsync(item.Occurrence.Item.Id, item.Occurrence.DueDate);
    public Task UnskipAsync(DueItem item) => recurring.UnskipAsync(item.Occurrence.Item.Id, item.Occurrence.DueDate);
    public Task ArchiveAsync(RecurringItem item) => recurring.SetArchivedAsync(item.Id, true);
    public Task DeleteAsync(RecurringItem item) => recurring.DeleteAsync(item.Id);
}
