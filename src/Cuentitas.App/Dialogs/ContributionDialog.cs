using CommunityToolkit.WinUI.Controls;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Dialogs;

/// <summary>Aportar a (o retirar de) una meta medida por aportes.</summary>
public sealed partial class ContributionDialog : ContentDialog
{
    private readonly GoalService _service;
    private readonly Goal _goal;
    private readonly Segmented _kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _amount = new() { PlaceholderText = "0", FontSize = 24, InputScope = Number() };
    private readonly CalendarDatePicker _date = new() { Date = DateTimeOffset.Now.Date, FirstDayOfWeek = Windows.Globalization.DayOfWeek.Monday };
    private readonly TextBox _note = new() { Header = "Nota (opcional)", MaxLength = 200, PlaceholderText = "Ej.: lo que sobró del aguinaldo" };
    private readonly InfoBar _error = new() { Severity = InfoBarSeverity.Error, IsClosable = false };

    public ContributionDialog(GoalService service, Goal goal, long suggested)
    {
        _service = service;
        _goal = goal;
        Title = $"Aportar a «{goal.Name}»";
        PrimaryButtonText = "Guardar";
        CloseButtonText = "Cancelar";
        DefaultButton = ContentDialogButton.Primary;
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];

        _kind.Items.Add(new SegmentedItem { Content = "Aportar" });
        _kind.Items.Add(new SegmentedItem { Content = "Retirar" });
        _kind.SelectedIndex = 0;
        _kind.SelectionChanged += (_, _) => Title = _kind.SelectedIndex == 1 ? $"Retirar de «{goal.Name}»" : $"Aportar a «{goal.Name}»";
        if (suggested > 0) _amount.Text = Money.ToEditable(suggested);

        var amountRow = new Grid { ColumnSpacing = 8 };
        amountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        amountRow.ColumnDefinitions.Add(new ColumnDefinition());
        amountRow.Children.Add(new TextBlock { Text = goal.Currency.Symbol(), FontSize = 24, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_amount, 1);
        amountRow.Children.Add(_amount);
        _error.IsOpen = false;

        Content = new StackPanel
        {
            Width = 440,
            Spacing = 14,
            Children =
            {
                _kind,
                amountRow,
                new TextBlock
                {
                    Text = "La plata queda en tus cuentas: el aporte sólo separa cuánto es para esta meta.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["LabelStyle"],
                },
                _date,
                _note,
                _error,
            },
        };
        PrimaryButtonClick += OnPrimaryClick;
        Opened += (_, _) => _amount.Focus(FocusState.Programmatic);
    }

    private static Microsoft.UI.Xaml.Input.InputScope Number()
    {
        var scope = new Microsoft.UI.Xaml.Input.InputScope();
        scope.Names.Add(new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number));
        return scope;
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (Money.Parse(_amount.Text) is not { } amount || amount <= 0)
                throw new ArgumentException("Ingresá un monto mayor a cero.");
            if (_kind.SelectedIndex == 1) amount = -amount;
            await _service.AddContributionAsync(_goal.Id, amount, (_date.Date ?? DateTimeOffset.Now).Date, _note.Text);
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

/// <summary>Historial de aportes de una meta, con opción de borrar.</summary>
public sealed partial class GoalHistoryDialog : ContentDialog
{
    private readonly GoalService _service;
    private readonly Goal _goal;
    private readonly StackPanel _list = new() { Spacing = 4 };

    public GoalHistoryDialog(GoalService service, Goal goal)
    {
        _service = service;
        _goal = goal;
        Title = $"Aportes a «{goal.Name}»";
        CloseButtonText = "Cerrar";
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];
        Content = new ScrollViewer { Content = _list, MaxHeight = 480, Width = 460, Padding = new Thickness(0, 0, 12, 0) };
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _list.Children.Clear();
        var items = await _service.GetContributionsAsync(_goal.Id);
        if (items.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "Todavía no hay aportes.", Style = (Style)Application.Current.Resources["LabelStyle"] });
            return;
        }
        foreach (var c in items)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = Helpers.Ui.Capitalize(c.Date.ToString("dddd d 'de' MMMM 'de' yyyy", Helpers.Ui.Culture)) },
                    new TextBlock { Text = c.Note, Style = (Style)Application.Current.Resources["LabelStyle"] },
                },
            });
            var amount = new TextBlock
            {
                Text = Money.Format(c.Amount, _goal.Currency, showSign: true),
                Foreground = Helpers.Ui.ToneBrush(c.Amount >= 0 ? Helpers.Tone.Income : Helpers.Tone.Expense),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(amount, 1);
            row.Children.Add(amount);
            var delete = new Button { Content = new SymbolIcon(Symbol.Delete), Tag = c.Id };
            ToolTipService.SetToolTip(delete, "Borrar este aporte");
            delete.Click += async (_, _) =>
            {
                await _service.DeleteContributionAsync((int)delete.Tag);
                await RefreshAsync();
            };
            Grid.SetColumn(delete, 2);
            row.Children.Add(delete);
            _list.Children.Add(row);
        }
    }
}
