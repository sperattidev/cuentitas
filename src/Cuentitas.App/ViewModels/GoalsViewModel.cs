using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Cuentitas.App.Helpers;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Cuentitas.App.ViewModels;

/// <summary>Tarjeta de una meta: progreso, cuándo llegás y qué pasa si recortás.</summary>
public sealed class GoalCard(GoalOverview overview, GoalProjection? withCut, double cut, double inflation)
{
    public Goal Goal => overview.Goal;
    private GoalProjection P => overview.Projection;
    private Currency Currency => Goal.Currency;
    /// <summary>Montos de proyección redondeados a la unidad: son estimaciones, los centavos no aportan.</summary>
    private string F(long cents) => Money.FormatWhole(cents, Currency);

    public string Name => Goal.Name;
    public string IconKey => Goal.Icon;
    public uint Color => Goal.Color;
    public Brush ColorBrush => Ui.Brush(Goal.Color);
    public string CurrencyCode => Currency.ToString();

    public string TrackingText => Goal.Tracking == GoalTracking.LinkedAccount
        ? $"Saldo de «{Goal.Account?.Name}»"
        : "Con aportes";

    public string CurrentText => F(P.Current);
    public string TargetText => $"de {F(P.Target)}";
    public double ProgressPercent => P.Progress * 100;
    public string PercentText => $"{P.Progress * 100:0} %";

    public string EtaText => P switch
    {
        { Reached: true } => "¡Meta cumplida!",
        { MonthsToReach: null } => "A este ritmo no llegás",
        { MonthsToReach: 1 } => "Llegás el mes que viene",
        { MonthsToReach: var m, Eta: var eta } => $"Llegás en {m} meses ({Ui.MonthTitle(eta!.Value).ToLowerInvariant()})",
    };

    public Brush EtaBrush => P switch
    {
        { Reached: true } => Ui.ToneBrush(Tone.Income),
        { MonthsToReach: null } => Ui.ToneBrush(Tone.Expense),
        { OnTrack: false } => Ui.ToneBrush(Tone.Optional),
        _ => Ui.ToneBrush(Tone.Neutral),
    };

    public string PaceText => P.PaceSource switch
    {
        PaceSource.Planned => $"Aportando {F(P.MonthlyPace)} por mes (tu plan)",
        PaceSource.AverageSavings when P.MonthlyPace > 0 => $"Si destinás tu ahorro promedio: {F(P.MonthlyPace)} por mes",
        PaceSource.AverageSavings => "En los últimos meses gastaste todo lo que entró",
        _ => "Todavía no hay datos de tu ahorro: cargá ingresos y gastos o planificá un aporte",
    };

    public string DateText
    {
        get
        {
            if (Goal.TargetDate is not { } date || P.Reached) return "";
            var when = date.ToString("d 'de' MMMM 'de' yyyy", Ui.Culture);
            if (P.OnTrack == true) return $"Vas bien para el {when}.";
            return P.RequiredMonthly is { } req && P.MonthsToTargetDate > 0
                ? $"Para llegar al {when} necesitás {F(req)} por mes."
                : $"La fecha deseada ({when}) ya llegó: faltan {F(P.Remaining)}.";
        }
    }

    public string InflationText =>
        Goal.Currency == Currency.ARS && Goal.AdjustForInflation && !P.Reached && P.TargetAtEta is { } t && t > P.Target
            ? $"Con {inflation * 100:0.#} % de inflación mensual, al llegar vas a necesitar {F(t)}."
            : "";

    public string CutText
    {
        get
        {
            if (cut <= 0 || withCut is null || P.Reached) return "";
            var pct = $"{cut * 100:0} %";
            return (P.MonthsToReach, withCut.MonthsToReach) switch
            {
                (null, { } m) => $"Recortando el {pct} de lo prescindible llegás en {m} meses.",
                ({ } a, { } b) when b < a => $"Recortando el {pct} de lo prescindible: {b} meses ({a - b} antes).",
                _ => "",
            };
        }
    }

    public Visibility DateVisibility => DateText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InflationVisibility => InflationText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CutVisibility => CutText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContributeVisibility =>
        Goal.Tracking == GoalTracking.Contributions ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Un gasto prescindible del ranking ("¿qué recortar?").</summary>
public sealed class OptionalRow(OptionalSpending s, long total)
{
    public string Name => s.Category.Name;
    public string IconKey => s.Category.Icon;
    public uint Color => s.Category.Color;
    public string AmountText => $"{Money.Format(s.MonthlyAverage, compact: true)} / mes";
    public double Percent => total > 0 ? 100.0 * s.MonthlyAverage / total : 0;
    public string HalfText => $"Recortando la mitad: {Money.Format(s.MonthlyAverage / 2, compact: true)} más por mes";
}

public sealed partial class GoalsViewModel(DataChanges changes, GoalService goals) : PageViewModel(changes)
{
    public ObservableCollection<GoalCard> Goals { get; } = [];
    public ObservableCollection<OptionalRow> OptionalRanking { get; } = [];

    public SavingsStats? Stats { get; private set; }

    [ObservableProperty] public partial string AvgIncomeText { get; set; } = "";
    [ObservableProperty] public partial string AvgExpenseText { get; set; } = "";
    [ObservableProperty] public partial string AvgNetText { get; set; } = "";
    [ObservableProperty] public partial Brush? AvgNetBrush { get; set; }
    [ObservableProperty] public partial string AvgOptionalText { get; set; } = "";
    [ObservableProperty] public partial string StatsNote { get; set; } = "";
    [ObservableProperty] public partial string InflationInput { get; set; } = "";
    [ObservableProperty] public partial string UsdRateInput { get; set; } = "";
    [ObservableProperty] public partial string UsdRateHint { get; set; } = "";
    [ObservableProperty] public partial string ParamsError { get; set; } = "";
    public Visibility ParamsErrorVisibility => ParamsError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    partial void OnParamsErrorChanged(string value) => OnPropertyChanged(nameof(ParamsErrorVisibility));

    /// <summary>Porcentaje (0..100) de gastos prescindibles que el usuario simula recortar.</summary>
    [ObservableProperty] public partial double CutPercent { get; set; }
    [ObservableProperty] public partial string CutSummary { get; set; } = "";

    [ObservableProperty] public partial Visibility EmptyVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility GoalsVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility RankingVisibility { get; set; } = Visibility.Collapsed;

    private bool _loading;

    protected override async Task LoadAsync()
    {
        _loading = true;
        var today = DateTime.Today;
        var all = await goals.GetSavingsStatsAsync(today);
        var stats = Stats = all[Currency.ARS];
        var inflation = await goals.GetMonthlyInflationAsync();
        var rateOverride = await goals.GetUsdRateOverrideAsync();
        var autoRate = await goals.GetAverageDollarPurchaseRateAsync(today);

        AvgIncomeText = Money.Format(stats.AvgIncome, compact: true);
        AvgExpenseText = Money.Format(stats.AvgExpense, compact: true);
        AvgNetText = Money.Format(stats.AvgNet, showSign: true, compact: true);
        AvgNetBrush = Ui.ToneBrush(stats.AvgNet >= 0 ? Tone.Income : Tone.Expense);
        AvgOptionalText = Money.Format(stats.AvgOptional, compact: true);
        StatsNote = stats.Months switch
        {
            0 => "Con lo que va del mes: todavía no hay un mes completo cargado.",
            1 => "Promedio del último mes completo.",
            var n => $"Promedio por mes de los últimos {n} meses completos.",
        };

        InflationInput = (inflation * 100).ToString("0.##", Ui.Culture);
        UsdRateInput = rateOverride is { } r ? r.ToString("0.##", Ui.Culture) : "";
        UsdRateHint = autoRate is { } a
            ? $"Vacío = automático: {Money.FormatRate(a)} (promedio de tus compras de los últimos 3 meses)."
            : "Todavía no compraste dólares: cargá el tipo de cambio para proyectar metas en dólares.";

        OptionalRanking.Clear();
        foreach (var o in stats.OptionalByCategory.Take(6)) OptionalRanking.Add(new OptionalRow(o, stats.AvgOptional));
        RankingVisibility = OptionalRanking.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        await LoadGoalsAsync(today, inflation);
        _loading = false;
    }

    private async Task LoadGoalsAsync(DateTime today, double inflation)
    {
        var cut = CutPercent / 100;
        var normal = await goals.GetOverviewAsync(today);
        var withCut = cut > 0 ? (await goals.GetOverviewAsync(today, cut)).ToDictionary(o => o.Goal.Id, o => o.Projection) : null;

        Goals.Clear();
        foreach (var o in normal) Goals.Add(new GoalCard(o, withCut?.GetValueOrDefault(o.Goal.Id), cut, inflation));
        EmptyVisibility = Goals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GoalsVisibility = Goals.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var extra = (long)Math.Round((Stats?.AvgOptional ?? 0) * cut);
        CutSummary = cut <= 0
            ? "Mové el control para ver cuánto antes llegarías a tus metas."
            : extra > 0
                ? $"Recortando el {CutPercent:0} % de tus gastos prescindibles ahorrás {Money.Format(extra, compact: true)} más por mes."
                : "No tenés gastos prescindibles registrados en los últimos meses.";
    }

    partial void OnCutPercentChanged(double value)
    {
        if (_loading) return;
        _ = RefreshGoalsAsync();
    }

    private async Task RefreshGoalsAsync() =>
        await LoadGoalsAsync(DateTime.Today, await goals.GetMonthlyInflationAsync());

    /// <summary>Guarda inflación y tipo de cambio de referencia (se llama al salir de los campos).</summary>
    public async Task ApplyParametersAsync()
    {
        ParamsError = "";
        try
        {
            if (!double.TryParse(InflationInput.Replace('.', ','), NumberStyles.Float, Ui.Culture, out var pct))
                throw new ArgumentException("La inflación tiene que ser un número (ej. 2,5).");
            decimal? rate = null;
            if (!string.IsNullOrWhiteSpace(UsdRateInput))
            {
                if (Money.Parse(UsdRateInput) is not { } cents || cents <= 0)
                    throw new ArgumentException("El tipo de cambio tiene que ser un número (ej. 1.250).");
                rate = cents / 100m;
            }

            var current = await goals.GetMonthlyInflationAsync();
            if (Math.Abs(current - pct / 100) > 1e-9) await goals.SetMonthlyInflationAsync(pct / 100);
            if (await goals.GetUsdRateOverrideAsync() != rate) await goals.SetUsdRateOverrideAsync(rate);
        }
        catch (ArgumentException ex)
        {
            ParamsError = ex.Message;
        }
    }

    public Task ArchiveAsync(Goal g) => goals.SetArchivedAsync(g.Id, true);
    public Task CompleteAsync(Goal g) => goals.SetCompletedAsync(g.Id, true);
    public Task DeleteAsync(Goal g) => goals.DeleteAsync(g.Id);
}
