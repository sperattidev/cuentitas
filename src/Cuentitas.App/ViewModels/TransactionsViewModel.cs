using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cuentitas.App.Helpers;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;

namespace Cuentitas.App.ViewModels;

/// <summary>Lista de movimientos por mes, con búsqueda y filtros.</summary>
public sealed partial class TransactionsViewModel(
    DataChanges changes,
    TransactionService transactions,
    AccountService accounts) : PageViewModel(changes)
{
    private CancellationTokenSource? _searchDebounce;

    public ObservableCollection<TransactionGroup> Groups { get; } = [];
    public ObservableCollection<FilterOption<int?>> AccountFilters { get; } = [];

    public List<FilterOption<TxType?>> TypeFilters { get; } =
    [
        new("Todos los tipos", null),
        new("Gastos", TxType.Expense),
        new("Ingresos", TxType.Income),
        new("Transferencias", TxType.Transfer),
    ];

    public List<FilterOption<Necessity?>> NecessityFilters { get; } =
    [
        new("Necesarios y prescindibles", null),
        new("Sólo necesarios", Necessity.Necessary),
        new("Sólo prescindibles", Necessity.Optional),
    ];

    [ObservableProperty] public partial DateTime Month { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial FilterOption<TxType?>? SelectedType { get; set; }
    [ObservableProperty] public partial FilterOption<int?>? SelectedAccount { get; set; }
    [ObservableProperty] public partial FilterOption<Necessity?>? SelectedNecessity { get; set; }

    [ObservableProperty] public partial string IncomeText { get; set; } = "";
    [ObservableProperty] public partial string ExpenseText { get; set; } = "";
    [ObservableProperty] public partial string NetText { get; set; } = "";
    [ObservableProperty] public partial string UsdSummaryText { get; set; } = "";
    [ObservableProperty] public partial string ResultText { get; set; } = "";
    [ObservableProperty] public partial Visibility EmptyVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility MonthVisibility { get; set; } = Visibility.Visible;

    public string MonthTitle => Ui.MonthTitle(Month);
    public bool CanGoNext => Month < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(12);

    private bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    protected override async Task LoadAsync()
    {
        if (AccountFilters.Count == 0) await LoadAccountFiltersAsync();
        SelectedType ??= TypeFilters[0];
        SelectedNecessity ??= NecessityFilters[0];

        var (from, to) = SummaryService.MonthRange(Month.Year, Month.Month);
        var filter = new TxFilter
        {
            From = IsSearching ? null : from,
            To = IsSearching ? null : to,
            Search = IsSearching ? SearchText : null,
            Type = SelectedType?.Value,
            AccountId = SelectedAccount?.Value,
            Necessity = SelectedNecessity?.Value,
        };
        var list = await transactions.QueryAsync(filter);

        Groups.Clear();
        foreach (var g in list.GroupBy(t => t.Date.Date))
            Groups.Add(new TransactionGroup(g.Key, g.Select(t => new TransactionItem(t))));

        // Resumen de lo que se ve en pantalla (sin transferencias).
        long Sum(Currency c, TxType type) =>
            list.Where(t => t.Type == type && t.Account?.Currency == c).Sum(t => t.Amount);
        var income = Sum(Currency.ARS, TxType.Income);
        var expense = Sum(Currency.ARS, TxType.Expense);
        IncomeText = Money.Format(income, showSign: true);
        ExpenseText = Money.Format(-expense);
        NetText = Money.Format(income - expense, showSign: true);

        var usdIn = Sum(Currency.USD, TxType.Income);
        var usdOut = Sum(Currency.USD, TxType.Expense);
        UsdSummaryText = usdIn != 0 || usdOut != 0
            ? $"En dólares: {Money.Format(usdIn, Currency.USD, showSign: true)} / {Money.Format(-usdOut, Currency.USD)}"
            : "";

        ResultText = list.Count switch
        {
            0 => "",
            1 => "1 movimiento",
            var n => $"{n} movimientos",
        } + (IsSearching && list.Count > 0 ? " en todos los meses" : "");
        EmptyVisibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MonthVisibility = IsSearching ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadAccountFiltersAsync()
    {
        AccountFilters.Add(new FilterOption<int?>("Todas las cuentas", null));
        foreach (var a in await accounts.GetAllWithBalancesAsync(includeArchived: true))
            AccountFilters.Add(new FilterOption<int?>(a.Account.Name, a.Account.Id));
        SelectedAccount = AccountFilters[0];
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

    public Task DeleteAsync(TransactionItem item) => transactions.DeleteAsync(item.Transaction.Id);

    public Task DeleteGroupAsync(string installmentGroup) => transactions.DeleteInstallmentGroupAsync(installmentGroup);

    /// <summary>Crea una copia con fecha de hoy (útil para gastos que se repiten).</summary>
    public Task DuplicateAsync(TransactionItem item)
    {
        var t = item.Transaction;
        return transactions.AddAsync(new Transaction
        {
            Type = t.Type, Amount = t.Amount, AccountId = t.AccountId, ToAccountId = t.ToAccountId,
            ToAmount = t.ToAmount, CategoryId = t.CategoryId, Necessity = t.Necessity, Note = t.Note,
            Date = DateTime.Today,
        });
    }

    partial void OnMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnSearchTextChanged(string value) => _ = DebouncedReloadAsync();
    partial void OnSelectedTypeChanged(FilterOption<TxType?>? value) => ReloadIfReady();
    partial void OnSelectedAccountChanged(FilterOption<int?>? value) => ReloadIfReady();
    partial void OnSelectedNecessityChanged(FilterOption<Necessity?>? value) => ReloadIfReady();

    private void ReloadIfReady()
    {
        if (AccountFilters.Count > 0 && !IsLoading) _ = ReloadAsync();
    }

    private async Task DebouncedReloadAsync()
    {
        _searchDebounce?.Cancel();
        var cts = _searchDebounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            await ReloadAsync();
        }
        catch (TaskCanceledException)
        {
            // Siguió escribiendo.
        }
    }
}
