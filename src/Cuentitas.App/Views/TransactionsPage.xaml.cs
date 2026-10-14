using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Cuentitas.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Views;

public sealed partial class TransactionsPage : Page
{
    private TransactionItem? _menuTarget;

    public TransactionsPage()
    {
        ViewModel = App.Get<TransactionsViewModel>();
        InitializeComponent();
        List.ItemsSource = new Microsoft.UI.Xaml.Data.CollectionViewSource
        {
            Source = ViewModel.Groups,
            IsSourceGrouped = true,
        }.View;
    }

    public TransactionsViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();

    private async void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TransactionItem item)
            await App.Get<DialogService>().EditTransactionAsync(item.Transaction);
    }

    private void OnRowMenuOpening(object? sender, object e)
    {
        // El flyout se abre sobre el ListViewItem: tomamos su contenido.
        _menuTarget = (sender as MenuFlyout)?.Target is ListViewItem { Content: TransactionItem item } ? item : null;
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is { } item) await App.Get<DialogService>().EditTransactionAsync(item.Transaction);
    }

    private async void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is { } item) await ViewModel.DuplicateAsync(item);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is not { } item) return;
        var dialogs = App.Get<DialogService>();
        if (item.Transaction.InstallmentGroup is { } group)
        {
            var choice = await dialogs.ChooseAsync("¿Eliminar cuotas?",
                $"{item.Title} · cuota {item.Transaction.InstallmentNumber} de {item.Transaction.InstallmentCount}.\n\n" +
                "Podés borrar sólo esta cuota o la compra completa (todas las cuotas). No se puede deshacer.",
                "Todas las cuotas", "Sólo esta cuota");
            if (choice == ContentDialogResult.Primary) await ViewModel.DeleteGroupAsync(group);
            else if (choice == ContentDialogResult.Secondary) await ViewModel.DeleteAsync(item);
            return;
        }
        var ok = await dialogs.ConfirmAsync("¿Eliminar este movimiento?",
            $"{item.Title} · {item.AmountText} · {item.DateText}\n\nEsta acción no se puede deshacer." +
            (item.Transaction.RecurringItemId is not null ? "\nEl vencimiento vuelve a quedar pendiente." : ""));
        if (ok) await ViewModel.DeleteAsync(item);
    }

    private async void OnExportClick(object sender, RoutedEventArgs e) => await App.Get<ExportFlow>().RunAsync();

    private void OnTypeFilterChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectedType = TypeFilter.SelectedItem as FilterOption<TxType?>;

    private void OnAccountFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountFilter.SelectedItem is FilterOption<int?> f) ViewModel.SelectedAccount = f;
    }

    private void OnNecessityFilterChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectedNecessity = NecessityFilter.SelectedItem as FilterOption<Necessity?>;
}
