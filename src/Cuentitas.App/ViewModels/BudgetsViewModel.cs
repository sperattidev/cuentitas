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

/// <summary>Fila de presupuesto: barra, gastado/tope y aviso.</summary>
public sealed class BudgetRow(BudgetStatus s, bool currentMonth)
{
    public BudgetStatus Status => s;
    public Category Category => s.Category;
    public string Name => s.Category.Name;
    public string IconKey => s.Category.Icon;
    public uint Color => s.Category.Color;
    public string SpentText => $"{Money.Format(s.Spent, compact: true)} de {Money.Format(s.Limit, compact: true)}";
    public double Percent => Math.Min(100, s.Ratio * 100);
    public string PercentText => $"{s.Ratio * 100:0} %";

    public Brush BarBrush => Ui.ToneBrush(s.Level switch
    {
        BudgetLevel.Over => Tone.Expense,
        BudgetLevel.Warning => Tone.Optional,
        _ => Tone.Income,
    });

    public string StatusText => s.Level == BudgetLevel.Over
        ? $"Te pasaste {Money.Format(-s.Remaining, compact: true)}"
        : currentMonth ? $"Te quedan {Money.Format(s.Remaining, compact: true)}" : $"Sobraron {Money.Format(s.Remaining, compact: true)}";

    public string ProjectionText => s.ProjectedEndOfMonth is { } p && s.Level != BudgetLevel.Over
        ? s.WillExceed
            ? $"A este ritmo cerrás el mes en {Money.FormatWhole(p)}: te vas a pasar."
            : $"A este ritmo cerrás el mes en {Money.FormatWhole(p)}."
        : "";

    public Brush ProjectionBrush => Ui.ToneBrush(s.WillExceed ? Tone.Optional : Tone.Neutral);
    public Visibility ProjectionVisibility => ProjectionText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Categoría con gasto este mes y sin presupuesto (para sugerir ponerle tope).</summary>
public sealed class UnbudgetedRow(Category c, long spent)
{
    public Category Category => c;
    public string Name => c.Name;
    public string IconKey => c.Icon;
    public uint Color => c.Color;
    public string SpentText => $"{Money.Format(spent, compact: true)} este mes";
}

public sealed partial class BudgetsViewModel(DataChanges changes, BudgetService budgets, SummaryService summary)
    : PageViewModel(changes)
{
    public ObservableCollection<BudgetRow> Rows { get; } = [];
    public ObservableCollection<UnbudgetedRow> Unbudgeted { get; } = [];

    [ObservableProperty] public partial DateTime Month { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] public partial string TotalText { get; set; } = "";
    [ObservableProperty] public partial string TotalHint { get; set; } = "";
    [ObservableProperty] public partial double TotalPercent { get; set; }
    [ObservableProperty] public partial Brush? TotalBrush { get; set; }
    [ObservableProperty] public partial string AlertText { get; set; } = "";
    [ObservableProperty] public partial Visibility EmptyVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility ContentVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility UnbudgetedVisibility { get; set; } = Visibility.Collapsed;

    public string MonthTitle => Ui.MonthTitle(Month);
    private bool IsCurrent => Month == new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public bool CanGoNext => !IsCurrent;

    protected override async Task LoadAsync()
    {
        var today = DateTime.Today;
        var status = await budgets.GetStatusAsync(today, Month);
        Rows.Clear();
        foreach (var s in status) Rows.Add(new BudgetRow(s, IsCurrent));
        EmptyVisibility = status.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ContentVisibility = status.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var limit = status.Sum(s => s.Limit);
        var spent = status.Sum(s => s.Spent);
        TotalText = $"{Money.Format(spent, compact: true)} de {Money.Format(limit, compact: true)}";
        TotalPercent = limit > 0 ? Math.Min(100, 100.0 * spent / limit) : 0;
        TotalBrush = Ui.ToneBrush(spent > limit ? Tone.Expense : spent >= limit * BudgetService.WarningRatio ? Tone.Optional : Tone.Income);
        TotalHint = limit > 0 ? $"Usaste el {100.0 * spent / limit:0} % de lo presupuestado" : "";
        var over = status.Count(s => s.Level == BudgetLevel.Over);
        var warn = status.Count(s => s.Level == BudgetLevel.Warning || s.WillExceed);
        AlertText = (over, warn) switch
        {
            (0, 0) => "Vas bien en todos tus presupuestos.",
            (0, _) => $"{warn} {(warn == 1 ? "presupuesto viene" : "presupuestos vienen")} justo.",
            _ => $"Te pasaste en {over} {(over == 1 ? "presupuesto" : "presupuestos")}" + (warn > 0 ? $" y {warn} vienen justo." : "."),
        };

        // Categorías con gasto y sin tope.
        var (from, to) = SummaryService.MonthRange(Month.Year, Month.Month);
        if (to > today) to = today;
        var withBudget = status.Select(s => s.Category.Id).ToHashSet();
        Unbudgeted.Clear();
        foreach (var c in (await summary.GetExpensesByCategoryAsync(from, to)).Where(c => !withBudget.Contains(c.Category.Id)).Take(6))
            Unbudgeted.Add(new UnbudgetedRow(c.Category, c.Total));
        UnbudgetedVisibility = Unbudgeted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        if (IsCurrent) return Task.CompletedTask;
        Month = Month.AddMonths(1);
        return ReloadAsync();
    }

    partial void OnMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(CanGoNext));
    }

    public Task DeleteAsync(Category c) => budgets.DeleteAsync(c.Id);
}
