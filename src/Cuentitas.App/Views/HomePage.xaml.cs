using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        ViewModel = App.Get<HomeViewModel>();
        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();

    private async void OnTransactionClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TransactionItem item)
            await App.Get<DialogService>().EditTransactionAsync(item.Transaction);
    }

    private void OnSeeAllClick(object sender, RoutedEventArgs e) => App.MainWindow.NavigateTo("transactions");

    private void OnAccountsClick(object sender, RoutedEventArgs e) => App.MainWindow.NavigateTo("accounts");

    private void OnBillsClick(object sender, RoutedEventArgs e) => App.MainWindow.NavigateTo("bills");

    private void OnGoalsClick(object sender, RoutedEventArgs e) => App.MainWindow.NavigateTo("goals");

    private void OnBudgetsClick(object sender, RoutedEventArgs e) => App.MainWindow.NavigateTo("budgets");

    private async void OnBuyDollarsClick(object sender, RoutedEventArgs e) =>
        await BuyDollars.StartAsync();
}
