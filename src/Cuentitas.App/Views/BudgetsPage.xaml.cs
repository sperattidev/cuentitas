using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Views;

public sealed partial class BudgetsPage : Page
{
    public BudgetsPage()
    {
        ViewModel = App.Get<BudgetsViewModel>();
        InitializeComponent();
    }

    public BudgetsViewModel ViewModel { get; }

    private DialogService Dialogs => App.Get<DialogService>();

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();

    private async void OnNewClick(object sender, RoutedEventArgs e) => await Dialogs.EditBudgetAsync(null, null);

    private async void OnRowClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BudgetRow row) await Dialogs.EditBudgetAsync(row.Category, row.Status.Limit);
    }

    private async void OnUnbudgetedClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is UnbudgetedRow row) await Dialogs.EditBudgetAsync(row.Category, null);
    }
}
