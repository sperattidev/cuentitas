using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cuentitas.Core;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;

namespace Cuentitas.App.ViewModels;

public sealed partial class AccountsViewModel(DataChanges changes, AccountService accounts, SummaryService summary)
    : PageViewModel(changes)
{
    public ObservableCollection<AccountOption> Active { get; } = [];
    public ObservableCollection<AccountOption> Archived { get; } = [];

    [ObservableProperty] public partial string TotalArs { get; set; } = "";
    [ObservableProperty] public partial string TotalUsd { get; set; } = "";
    [ObservableProperty] public partial Visibility UsdVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility ArchivedVisibility { get; set; } = Visibility.Collapsed;

    protected override async Task LoadAsync()
    {
        var all = await accounts.GetAllWithBalancesAsync(includeArchived: true);
        Active.Clear();
        Archived.Clear();
        foreach (var a in all)
            (a.Account.Archived ? Archived : Active).Add(AccountOption.From(a));

        var worth = await summary.GetNetWorthAsync();
        TotalArs = Money.Format(worth[Currency.ARS]);
        TotalUsd = Money.Format(worth[Currency.USD], Currency.USD);
        UsdVisibility = Active.Any(a => a.Currency == Currency.USD) ? Visibility.Visible : Visibility.Collapsed;
        ArchivedVisibility = Archived.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public Task SetArchivedAsync(AccountOption a, bool archived) => accounts.SetArchivedAsync(a.Id, archived);

    public Task<bool> HasTransactionsAsync(AccountOption a) => accounts.HasTransactionsAsync(a.Id);

    public Task DeleteAsync(AccountOption a) => accounts.DeleteAsync(a.Id);
}
