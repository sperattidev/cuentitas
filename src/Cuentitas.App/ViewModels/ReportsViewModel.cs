using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cuentitas.App.Controls;
using Cuentitas.App.Helpers;
using Cuentitas.Core;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Cuentitas.App.ViewModels;

/// <summary>Fila de la leyenda de la dona.</summary>
public sealed class LegendRow(string name, string iconKey, uint color, long amount, long total)
{
    public string Name => name;
    public string IconKey => iconKey;
    public uint Color => color;
    public string AmountText => Money.Format(amount, compact: true);
    public string ShareText => total > 0 ? $"{100.0 * amount / total:0} %" : "";
}

/// <summary>Fila de la comparación con el mes anterior.</summary>
public sealed class ChangeRow(CategoryChange c, double inflation)
{
    public string Name => c.Category.Name;
    public string IconKey => c.Category.Icon;
    public uint Color => c.Category.Color;
    public string CurrentText => Money.Format(c.Current, compact: true);
    public string PreviousText => $"antes {Money.Format(c.Previous, compact: true)}";

    public string ChangeText => c.Change switch
    {
        null => "nuevo",
        var x => $"{(x >= 0 ? "+" : "")}{x * 100:0} %",
    };

    public string RealText => c.RealChange(inflation) is { } r ? $"{(r >= 0 ? "+" : "")}{r * 100:0} % real" : "";

    /// <summary>Más gasto = color de gasto; menos gasto = verde.</summary>
    public Brush ChangeBrush => (c.RealChange(inflation) ?? c.Change ?? 1) switch
    {
        > 0.05 => Ui.ToneBrush(Tone.Expense),
        < -0.05 => Ui.ToneBrush(Tone.Income),
        _ => Ui.ToneBrush(Tone.Neutral),
    };
}

public sealed partial class ReportsViewModel(
    DataChanges changes, ReportService reports, SummaryService summary, GoalService goals) : PageViewModel(changes)
{
    public ObservableCollection<LegendRow> Legend { get; } = [];
    public ObservableCollection<ChangeRow> Changes { get; } = [];

    /// <summary>0 = 6 meses, 1 = 12 meses.</summary>
    [ObservableProperty] public partial int RangeIndex { get; set; } = 1;
    [ObservableProperty] public partial DateTime Month { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty] public partial BarChartData? IncomeExpense { get; set; }
    [ObservableProperty] public partial BarChartData? Necessity { get; set; }
    [ObservableProperty] public partial IReadOnlyList<(string Label, long Value)>? NetWorth { get; set; }
    [ObservableProperty] public partial IReadOnlyList<DonutSlice>? Slices { get; set; }
    [ObservableProperty] public partial string DonutCenter { get; set; } = "";

    [ObservableProperty] public partial string AvgSavingsText { get; set; } = "";
    [ObservableProperty] public partial string SavingsRateText { get; set; } = "";
    [ObservableProperty] public partial string OptionalShareText { get; set; } = "";
    [ObservableProperty] public partial string BestMonthText { get; set; } = "";
    [ObservableProperty] public partial string NetWorthChangeText { get; set; } = "";
    [ObservableProperty] public partial string ComparisonTitle { get; set; } = "";
    [ObservableProperty] public partial string ComparisonSummary { get; set; } = "";
    [ObservableProperty] public partial Brush? ComparisonBrush { get; set; }
    [ObservableProperty] public partial Visibility DonutEmptyVisibility { get; set; } = Visibility.Collapsed;

    public string MonthTitle => Ui.MonthTitle(Month);
    public bool CanGoNext => Month < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    protected override async Task LoadAsync()
    {
        var today = DateTime.Today;
        var months = RangeIndex == 0 ? 6 : 12;
        var series = await reports.GetMonthlySeriesAsync(today, months);
        // Sin los meses anteriores a tus primeros movimientos (si no, el patrimonio se ve "plano" y engaña).
        var first = series.FindIndex(p => p.Income > 0 || p.Expense > 0);
        if (first > 0) series = series.Skip(Math.Min(first, series.Count - 3)).ToList();
        var labels = series.Select(p => Ui.MonthShort(p.Month)).ToList();

        IncomeExpense = new BarChartData(labels,
        [
            new BarSeries("Ingresos", Ui.ToneBrush(Tone.Income), series.Select(p => p.Income).ToList()),
            new BarSeries("Gastos", Ui.ToneBrush(Tone.Expense), series.Select(p => p.Expense).ToList()),
        ]);
        Necessity = new BarChartData(labels,
        [
            new BarSeries("Necesarios", Ui.ThemeResource("BrandBrush"), series.Select(p => p.Necessary).ToList()),
            new BarSeries("Prescindibles", Ui.ToneBrush(Tone.Optional), series.Select(p => p.Optional).ToList()),
        ], Stacked: true);
        NetWorth = series.Select(p => (Ui.MonthShort(p.Month), p.NetWorth)).ToList();

        // Indicadores del período (sólo meses con movimientos).
        var active = series.Where(p => p.Income > 0 || p.Expense > 0).ToList();
        var income = active.Sum(p => p.Income);
        var expense = active.Sum(p => p.Expense);
        AvgSavingsText = active.Count > 0 ? Money.FormatWhole((income - expense) / active.Count, showSign: true) : "—";
        SavingsRateText = income > 0 ? $"Ahorrás el {(double)(income - expense) / income * 100:0} % de lo que entra" : "Sin ingresos en el período";
        OptionalShareText = expense > 0 ? $"{(double)active.Sum(p => p.Optional) / expense * 100:0} %" : "—";
        BestMonthText = active.Count > 0 && active.MaxBy(p => p.Net) is { } best
            ? $"{Ui.MonthTitle(best.Month)} ({Money.Format(best.Net, showSign: true, compact: true)})"
            : "—";
        NetWorthChangeText = series.Count > 1
            ? Money.Format(series[^1].NetWorth - series[0].NetWorth, showSign: true, compact: true)
            : "—";

        await LoadMonthAsync(today);
    }

    private async Task LoadMonthAsync(DateTime today)
    {
        var (from, to) = SummaryService.MonthRange(Month.Year, Month.Month);
        if (to > today) to = today;
        var byCategory = await summary.GetExpensesByCategoryAsync(from, to);
        var total = byCategory.Sum(c => c.Total);
        var top = byCategory.Take(7).ToList();
        var rest = byCategory.Skip(7).Sum(c => c.Total);

        var slices = top.Select(c => new DonutSlice(c.Category.Name, c.Total, Ui.Brush(c.Category.Color))).ToList();
        if (rest > 0) slices.Add(new DonutSlice("Otras", rest, Ui.Brush(0xFF6E7781)));
        Slices = slices;
        DonutCenter = total > 0 ? Ui.ShortMoney(total) : "";
        DonutEmptyVisibility = total > 0 ? Visibility.Collapsed : Visibility.Visible;

        Legend.Clear();
        foreach (var c in top) Legend.Add(new LegendRow(c.Category.Name, c.Category.Icon, c.Category.Color, c.Total, total));
        if (rest > 0) Legend.Add(new LegendRow("Otras", "other", 0xFF6E7781, rest, total));

        var inflation = await goals.GetMonthlyInflationAsync();
        var cmp = await reports.CompareMonthAsync(today, Month);
        ComparisonTitle = cmp.IsPartial
            ? $"Del 1 al {today.Day} vs el mismo tramo de {Ui.MonthTitle(cmp.PreviousFrom).ToLowerInvariant()}"
            : $"{Ui.MonthTitle(Month)} vs {Ui.MonthTitle(cmp.PreviousFrom).ToLowerInvariant()}";
        ComparisonSummary = cmp.Change is { } ch
            ? $"Gastaste {Money.Format(cmp.Expense, compact: true)}: {(ch >= 0 ? "+" : "")}{ch * 100:0} % " +
              $"({(cmp.Expense / (cmp.PreviousExpense * (1 + inflation)) - 1) * 100:+0;-0;0} % descontando {inflation * 100:0.#} % de inflación)"
            : $"Gastaste {Money.Format(cmp.Expense, compact: true)}. No hay datos del mes anterior para comparar.";
        ComparisonBrush = cmp.Change is { } c2 && cmp.Expense / (cmp.PreviousExpense * (1 + inflation)) - 1 > 0.05
            ? Ui.ToneBrush(Tone.Expense)
            : Ui.ToneBrush(Tone.Neutral);

        Changes.Clear();
        foreach (var c in cmp.Categories.Take(10)) Changes.Add(new ChangeRow(c, inflation));
    }

    [RelayCommand]
    private Task PreviousMonthAsync()
    {
        Month = Month.AddMonths(-1);
        return LoadMonthAsync(DateTime.Today);
    }

    [RelayCommand]
    private Task NextMonthAsync()
    {
        if (!CanGoNext) return Task.CompletedTask;
        Month = Month.AddMonths(1);
        return LoadMonthAsync(DateTime.Today);
    }

    partial void OnMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnRangeIndexChanged(int value) => _ = ReloadAsync();
}
