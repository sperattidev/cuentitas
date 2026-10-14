using Cuentitas.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Cuentitas.App.Dialogs;

public sealed partial class TransactionDialog : ContentDialog
{
    public TransactionDialog(TransactionEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DatePicker.Date = ViewModel.Date;

        // Sincronización explícita de los ComboBox (la lista destino se regenera al cambiar el origen).
        // x:Bind carga las listas recién en Loaded: recién ahí se puede marcar la selección inicial.
        Loaded += (_, _) =>
        {
            AccountBox.SelectedItem = ViewModel.SelectedAccount;
            ToAccountBox.SelectedItem = ViewModel.SelectedToAccount;
            CategoryGrid.SelectedItem = ViewModel.SelectedCategory;
            if (ViewModel.SelectedCategory is { } cat) CategoryGrid.ScrollIntoView(cat);
        };

        // Pagar un vencimiento es de a uno: no tiene sentido "cargar otro".
        if (ViewModel.IsRecurringPayment || ViewModel.IsEditing) SecondaryButtonText = "";
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.SelectedToAccount) &&
                !ReferenceEquals(ToAccountBox.SelectedItem, ViewModel.SelectedToAccount))
                ToAccountBox.SelectedItem = ViewModel.SelectedToAccount;
        };
        Opened += (_, _) => AmountBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    public TransactionEditorViewModel ViewModel { get; }

    /// <summary>Cantidad de movimientos guardados en esta apertura del diálogo.</summary>
    public int SavedCount { get; private set; }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        if (await ViewModel.SaveAsync()) SavedCount++;
        else args.Cancel = true;
        deferral.Complete();
    }

    /// <summary>Guarda y deja el diálogo abierto, listo para el siguiente.</summary>
    private async void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        args.Cancel = true;
        if (await ViewModel.SaveAsync())
        {
            SavedCount++;
            ViewModel.ResetForNext();
            AmountBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }
        deferral.Complete();
    }

    private void OnAmountKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Sólo dígitos, coma, punto y teclas de edición.
        var k = e.Key;
        var allowed = k is >= VirtualKey.Number0 and <= VirtualKey.Number9
            or >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9
            or VirtualKey.Decimal or (VirtualKey)188 /* coma */ or (VirtualKey)190 /* punto */
            or VirtualKey.Back or VirtualKey.Delete or VirtualKey.Left or VirtualKey.Right
            or VirtualKey.Home or VirtualKey.End or VirtualKey.Tab or VirtualKey.Enter or VirtualKey.Escape;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!allowed && !ctrl) e.Handled = true;
    }

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate is { } d) ViewModel.Date = d;
    }

    private void OnAccountChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectedAccount = AccountBox.SelectedItem as AccountOption;

    private void OnToAccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToAccountBox.SelectedItem is AccountOption a) ViewModel.SelectedToAccount = a;
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryGrid.SelectedItem is CategoryOption c) ViewModel.SelectedCategory = c;
    }
}
