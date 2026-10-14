using Cuentitas.App.ViewModels;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Dialogs;

/// <summary>Alta / edición de un pago o cobro recurrente.</summary>
public sealed partial class RecurringDialog : ContentDialog
{
    private static readonly Frequency[] Frequencies =
        [Frequency.Monthly, Frequency.Bimonthly, Frequency.Quarterly, Frequency.Semiannual, Frequency.Yearly];
    private static readonly int[] RemindDays = [0, 1, 2, 3, 5, 7];

    /// <summary>Plantilla rápida: nombre, categoría (por nombre), frecuencia y día habitual en Argentina.</summary>
    private sealed record QuickTemplate(string Label, string Name, string Category, Frequency Frequency, int Day, bool Variable);

    private static readonly QuickTemplate[] ExpenseTemplates =
    [
        new("Luz", "Luz", "Luz", Frequency.Bimonthly, 15, true),
        new("Gas", "Gas", "Gas", Frequency.Bimonthly, 15, true),
        new("Agua", "Agua", "Agua", Frequency.Bimonthly, 20, true),
        new("Internet", "Internet", "Internet y cable", Frequency.Monthly, 10, false),
        new("Celular", "Celular", "Celular", Frequency.Monthly, 10, false),
        new("Alquiler", "Alquiler", "Alquiler", Frequency.Monthly, 10, false),
        new("Expensas", "Expensas", "Expensas", Frequency.Monthly, 10, true),
        new("Prepaga", "Prepaga", "Prepaga / obra social", Frequency.Monthly, 10, false),
        new("ABL", "ABL", "ABL e inmobiliario", Frequency.Monthly, 15, false),
        new("Monotributo", "Monotributo", "Impuestos (monotributo, ganancias)", Frequency.Monthly, 20, false),
        new("Resumen tarjeta", "Resumen de tarjeta", "Cuotas y préstamos", Frequency.Monthly, 10, true),
        new("Streaming", "Streaming", "Streaming y suscripciones", Frequency.Monthly, 5, false),
        new("Gimnasio", "Gimnasio", "Gimnasio y deporte", Frequency.Monthly, 10, false),
    ];

    private static readonly QuickTemplate[] IncomeTemplates =
    [
        new("Sueldo", "Sueldo", "Sueldo", Frequency.Monthly, 1, false),
        new("Aguinaldo", "Aguinaldo", "Aguinaldo", Frequency.Semiannual, 30, false),
    ];

    private readonly RecurringService _service;
    private readonly RecurringItem? _editing;
    private readonly List<Category> _categories;
    private readonly List<AccountOption> _accounts;
    private readonly long _salaryEstimate;

    public RecurringDialog(RecurringService service, RecurringItem? editing, TxType type,
        List<Category> categories, List<AccountOption> accounts, long salaryEstimate)
    {
        _service = service;
        _editing = editing;
        _categories = categories;
        _accounts = accounts;
        _salaryEstimate = salaryEstimate;
        InitializeComponent();

        AccountBox.ItemsSource = accounts;
        Title = editing is null ? "Nuevo pago o cobro recurrente" : $"Editar «{editing.Name}»";
        TypeBox.SelectedIndex = (editing?.Type ?? type) == TxType.Income ? 1 : 0;
        TypeBox.IsEnabled = editing is null;
        TemplatesPanel.Visibility = editing is null ? Visibility.Visible : Visibility.Collapsed;

        NameBox.Text = editing?.Name ?? "";
        AccountBox.SelectedItem = accounts.FirstOrDefault(a => a.Id == editing?.AccountId) ?? accounts.FirstOrDefault();
        AmountBox.Text = editing is { EstimatedAmount: > 0 } ? Money.ToEditable(editing.EstimatedAmount) : "";
        VariableBox.IsChecked = editing?.VariableAmount ?? false;
        FrequencyBox.SelectedIndex = Math.Max(0, Array.IndexOf(Frequencies, editing?.Frequency ?? Frequency.Monthly));
        StartBox.Date = editing?.StartDate ?? Schedule.FirstDueFor(10, DateTime.Today);
        RemindBox.SelectedIndex = Math.Max(0, Array.IndexOf(RemindDays, editing?.RemindDaysBefore ?? 3));
        EndCheck.IsChecked = editing?.EndDate is not null;
        EndBox.Date = editing?.EndDate ?? DateTime.Today.AddYears(1);
        NoteBox.Text = editing?.Note ?? "";
        if (editing is not null)
        {
            StartBox.Header = "Primer vencimiento";
            StartHint.Text = "Cambiar esta fecha mueve todos los vencimientos. Los pagos ya registrados se conservan.";
        }

        RefreshForType();
        CategoryBox.SelectedItem = _categories.FirstOrDefault(c => c.Id == editing?.CategoryId) ?? CategoryBox.SelectedItem;
        ErrorBar.IsOpen = false;
    }

    private TxType Type => TypeBox.SelectedIndex == 1 ? TxType.Income : TxType.Expense;

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryBox is null) return;
        RefreshForType();
    }

    private void RefreshForType()
    {
        var kind = Type == TxType.Income ? CategoryKind.Income : CategoryKind.Expense;
        var list = _categories.Where(c => c.Kind == kind).ToList();
        CategoryBox.ItemsSource = list;
        CategoryBox.SelectedItem = list.FirstOrDefault();
        AmountLabel.Text = Type == TxType.Income ? "Monto estimado (neto)" : "Monto estimado";

        TemplatesList.Items.Clear();
        foreach (var t in Type == TxType.Income ? IncomeTemplates : ExpenseTemplates)
        {
            var button = new Button { Content = t.Label, Tag = t, Padding = new Thickness(12, 4, 12, 5) };
            button.Click += OnTemplateClick;
            TemplatesList.Items.Add(button);
        }
    }

    private void OnTemplateClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not QuickTemplate t) return;
        NameBox.Text = t.Name;
        CategoryBox.SelectedItem = (CategoryBox.ItemsSource as List<Category>)?.FirstOrDefault(c => c.Name == t.Category)
                                   ?? CategoryBox.SelectedItem;
        FrequencyBox.SelectedIndex = Array.IndexOf(Frequencies, t.Frequency);
        VariableBox.IsChecked = t.Variable;
        StartBox.Date = t.Name == "Aguinaldo" ? NextAguinaldo(DateTime.Today) : Schedule.FirstDueFor(t.Day, DateTime.Today);
        if (t.Name == "Aguinaldo" && _salaryEstimate > 0 && string.IsNullOrWhiteSpace(AmountBox.Text))
            AmountBox.Text = Money.ToEditable(_salaryEstimate / 2); // medio sueldo por semestre
        AmountBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Aguinaldo: 30 de junio o 30 de diciembre (la 2.ª cuota vence el 18/12; se puede ajustar).</summary>
    private static DateTime NextAguinaldo(DateTime today)
    {
        var june = new DateTime(today.Year, 6, 30);
        var december = new DateTime(today.Year, 12, 30);
        return today <= june ? june : today <= december ? december : june.AddYears(1);
    }

    private void OnAccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountBox.SelectedItem is AccountOption a) AmountSymbol.Text = a.Currency.Symbol();
    }

    private void OnEndChecked(object sender, RoutedEventArgs e) => EndBox.IsEnabled = EndCheck.IsChecked == true;

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            long amount = 0;
            if (!string.IsNullOrWhiteSpace(AmountBox.Text))
            {
                if (Money.Parse(AmountBox.Text) is not { } parsed || parsed < 0)
                {
                    ShowError("El monto no es un número válido.");
                    args.Cancel = true;
                    return;
                }
                amount = parsed;
            }

            var item = new RecurringItem
            {
                Id = _editing?.Id ?? 0,
                Name = NameBox.Text,
                Type = Type,
                CategoryId = (CategoryBox.SelectedItem as Category)?.Id ?? 0,
                AccountId = (AccountBox.SelectedItem as AccountOption)?.Id ?? 0,
                EstimatedAmount = amount,
                VariableAmount = VariableBox.IsChecked == true,
                Frequency = Frequencies[Math.Max(0, FrequencyBox.SelectedIndex)],
                StartDate = (StartBox.Date ?? DateTimeOffset.Now).Date,
                EndDate = EndCheck.IsChecked == true ? EndBox.Date?.Date : null,
                RemindDaysBefore = RemindDays[Math.Max(0, RemindBox.SelectedIndex)],
                Note = NoteBox.Text,
                Archived = _editing?.Archived ?? false,
            };

            if (_editing is null) await _service.CreateAsync(item);
            else await _service.UpdateAsync(item);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ShowError(ex.Message);
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
