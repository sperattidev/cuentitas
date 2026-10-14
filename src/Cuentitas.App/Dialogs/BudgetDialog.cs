using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Dialogs;

/// <summary>Crear/editar el presupuesto de una categoría, con sugerencia según el promedio.</summary>
public sealed partial class BudgetDialog : ContentDialog
{
    private readonly BudgetService _service;
    private readonly ComboBox _category = new() { Header = "Categoría", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Name" };
    private readonly TextBox _amount = new() { PlaceholderText = "0", FontSize = 22 };
    private readonly HyperlinkButton _suggest = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(0) };
    private readonly InfoBar _error = new() { Severity = InfoBarSeverity.Error, IsClosable = false };
    private long _suggestion;

    public BudgetDialog(BudgetService service, List<Category> categories, Category? category, long? currentLimit)
    {
        _service = service;
        var editing = currentLimit is not null;
        Title = editing ? $"Presupuesto de «{category!.Name}»" : "Nuevo presupuesto";
        PrimaryButtonText = "Guardar";
        CloseButtonText = "Cancelar";
        if (editing) SecondaryButtonText = "Quitar presupuesto";
        DefaultButton = ContentDialogButton.Primary;
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];

        _category.ItemsSource = categories;
        _category.SelectedItem = categories.FirstOrDefault(c => c.Id == category?.Id) ?? categories.FirstOrDefault();
        _category.IsEnabled = !editing;
        _category.SelectionChanged += async (_, _) => await LoadSuggestionAsync();
        if (currentLimit is { } l) _amount.Text = Money.ToEditable(l);
        _suggest.Click += (_, _) => _amount.Text = Money.ToEditable(_suggestion);
        _error.IsOpen = false;

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = "$", FontSize = 22, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_amount, 1);
        row.Children.Add(_amount);

        Content = new StackPanel
        {
            Width = 420,
            Spacing = 12,
            Children =
            {
                _category,
                new TextBlock { Text = "Tope por mes" },
                row,
                _suggest,
                new TextBlock
                {
                    Text = "Cuenta los gastos en pesos de esa categoría, del 1 al último día de cada mes.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["LabelStyle"],
                },
                _error,
            },
        };
        PrimaryButtonClick += OnPrimaryClick;
        SecondaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            await _service.DeleteAsync(category!.Id);
            deferral.Complete();
        };
        Loaded += async (_, _) => await LoadSuggestionAsync();
    }

    private async Task LoadSuggestionAsync()
    {
        if (_category.SelectedItem is not Category c) return;
        _suggestion = await _service.SuggestAsync(c.Id, DateTime.Today);
        _suggest.Content = $"Usar tu promedio de los últimos meses: {Money.Format(_suggestion, compact: true)}";
        _suggest.Visibility = _suggestion > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(_amount.Text) && _suggestion > 0) _amount.PlaceholderText = Money.ToEditable(_suggestion);
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (_category.SelectedItem is not Category c) throw new ArgumentException("Elegí una categoría.");
            if (Money.Parse(_amount.Text) is not { } amount || amount <= 0) throw new ArgumentException("Ingresá un tope mayor a cero.");
            await _service.SetAsync(c.Id, amount);
        }
        catch (ArgumentException ex)
        {
            _error.Message = ex.Message;
            _error.IsOpen = true;
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
