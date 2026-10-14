using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace Cuentitas.App.Views;

public sealed partial class BillsPage : Page
{
    private DueItem? _dueTarget;
    private RecurringRow? _recurringTarget;

    public BillsPage()
    {
        ViewModel = App.Get<BillsViewModel>();
        InitializeComponent();
        Agenda.ItemsSource = new CollectionViewSource { Source = ViewModel.Groups, IsSourceGrouped = true }.View;
        Agenda.ContainerContentChanging += (_, a) => a.ItemContainer.ContextFlyout = (MenuFlyout)Resources["DueMenu"];
        foreach (var list in new[] { PaymentsList, IncomesList })
            list.ContainerContentChanging += (_, a) => a.ItemContainer.ContextFlyout = (MenuFlyout)Resources["RecurringMenu"];
    }

    public BillsViewModel ViewModel { get; }

    private DialogService Dialogs => App.Get<DialogService>();

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();

    private async void OnNewClick(object sender, RoutedEventArgs e) => await Dialogs.EditRecurringAsync(null);

    private async void OnNewIncomeClick(object sender, RoutedEventArgs e) => await Dialogs.EditRecurringAsync(null, TxType.Income);

    private async void OnRecurringClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecurringRow row) await Dialogs.EditRecurringAsync(row.Item);
    }

    // ── Menú de un vencimiento ──────────────────────────────────────────────

    private void OnDueMenuOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        _dueTarget = menu.Target is ListViewItem { Content: DueItem d } ? d : null;
        if (_dueTarget is null) return;
        var status = _dueTarget.Occurrence.Status;
        var pay = (MenuFlyoutItem)menu.Items[0];
        pay.Text = _dueTarget.PayText;
        pay.Visibility = _dueTarget.IsPending ? Visibility.Visible : Visibility.Collapsed;
        menu.Items[1].Visibility = status == DueStatus.Paid ? Visibility.Visible : Visibility.Collapsed;
        var skip = (MenuFlyoutItem)menu.Items[2];
        skip.Text = status == DueStatus.Skipped ? "Volver a esperar este vencimiento" : "Omitir este vencimiento";
        skip.Visibility = status == DueStatus.Paid ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnPayMenuClick(object sender, RoutedEventArgs e)
    {
        if (_dueTarget is { } d) await Dialogs.PayAsync(d.Occurrence);
    }

    private async void OnViewPaymentClick(object sender, RoutedEventArgs e)
    {
        if (_dueTarget?.Occurrence.Payment is { } payment)
        {
            // El pago viene sin cuentas/categoría cargadas: lo buscamos completo.
            var full = (await App.Get<TransactionService>().QueryAsync(new TxFilter
            {
                From = payment.Date, To = payment.Date, AccountId = payment.AccountId,
            })).FirstOrDefault(t => t.Id == payment.Id);
            if (full is not null) await Dialogs.EditTransactionAsync(full);
        }
    }

    private async void OnSkipClick(object sender, RoutedEventArgs e)
    {
        if (_dueTarget is not { } d) return;
        if (d.Occurrence.Status == DueStatus.Skipped) await ViewModel.UnskipAsync(d);
        else await ViewModel.SkipAsync(d);
    }

    private async void OnEditFromDueClick(object sender, RoutedEventArgs e)
    {
        if (_dueTarget is { } d) await Dialogs.EditRecurringAsync(d.Occurrence.Item);
    }

    // ── Menú de un pago recurrente ──────────────────────────────────────────

    private void OnRecurringMenuOpening(object? sender, object e) =>
        _recurringTarget = (sender as MenuFlyout)?.Target is ListViewItem { Content: RecurringRow r } ? r : null;

    private async void OnEditRecurringClick(object sender, RoutedEventArgs e)
    {
        if (_recurringTarget is { } r) await Dialogs.EditRecurringAsync(r.Item);
    }

    private async void OnArchiveRecurringClick(object sender, RoutedEventArgs e)
    {
        if (_recurringTarget is not { } r) return;
        if (await Dialogs.ConfirmAsync($"¿Archivar «{r.Name}»?",
                "Deja de aparecer en vencimientos y no te avisamos más. Los pagos ya registrados se conservan.", "Archivar"))
            await ViewModel.ArchiveAsync(r.Item);
    }

    private async void OnDeleteRecurringClick(object sender, RoutedEventArgs e)
    {
        if (_recurringTarget is not { } r) return;
        if (await Dialogs.ConfirmAsync($"¿Eliminar «{r.Name}»?",
                "Se borra el pago recurrente. Los movimientos que ya registraste se conservan en Movimientos."))
            await ViewModel.DeleteAsync(r.Item);
    }
}
