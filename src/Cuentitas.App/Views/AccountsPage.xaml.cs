using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Views;

public sealed partial class AccountsPage : Page
{
    private AccountOption? _menuTarget;

    public AccountsPage()
    {
        ViewModel = App.Get<AccountsViewModel>();
        InitializeComponent();
        foreach (var grid in new[] { ActiveGrid, ArchivedGrid })
            grid.ContainerContentChanging += (_, args) =>
                args.ItemContainer.ContextFlyout = (MenuFlyout)Resources["AccountMenu"];
    }

    public AccountsViewModel ViewModel { get; }

    private DialogService Dialogs => App.Get<DialogService>();

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();

    private async void OnNewClick(object sender, RoutedEventArgs e) => await Dialogs.EditAccountAsync(null);

    private async void OnBuyDollarsClick(object sender, RoutedEventArgs e) => await BuyDollars.StartAsync();

    private async void OnAccountClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AccountOption a) await Dialogs.EditAccountAsync(a.Account);
    }

    private void OnMenuOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        _menuTarget = menu.Target is GridViewItem { Content: AccountOption a } ? a : null;
        if (menu.Items[1] is MenuFlyoutItem archive)
            archive.Text = _menuTarget?.Account.Archived == true ? "Desarchivar" : "Archivar";
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is { } a) await Dialogs.EditAccountAsync(a.Account);
    }

    private async void OnArchiveClick(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is { } a) await ViewModel.SetArchivedAsync(a, !a.Account.Archived);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is not { } a) return;
        if (await ViewModel.HasTransactionsAsync(a))
        {
            await Dialogs.ShowMessageAsync("No se puede eliminar",
                $"«{a.Name}» tiene movimientos registrados. Podés archivarla: deja de aparecer pero se conserva el historial.");
            return;
        }
        if (await Dialogs.ConfirmAsync("¿Eliminar cuenta?", $"Se eliminará «{a.Name}». Esta acción no se puede deshacer."))
            await ViewModel.DeleteAsync(a);
    }
}
