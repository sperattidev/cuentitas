using Cuentitas.App.Helpers;
using Cuentitas.App.ViewModels;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Dialogs;

/// <summary>Alta / edición de una meta de ahorro.</summary>
public sealed partial class GoalDialog : ContentDialog
{
    private sealed record Idea(string Label, string Name, string Icon, uint Color, Currency Currency, long Target);

    private readonly GoalService _service;
    private readonly Goal? _editing;
    private readonly List<AccountOption> _accounts;

    public GoalDialog(GoalService service, Goal? editing, List<AccountOption> accounts, SavingsStats? stats)
    {
        _service = service;
        _editing = editing;
        _accounts = accounts;
        InitializeComponent();

        Title = editing is null ? "Nueva meta" : $"Editar «{editing.Name}»";
        TemplatesPanel.Visibility = editing is null ? Visibility.Visible : Visibility.Collapsed;
        NameBox.Text = editing?.Name ?? "";
        CurrencyBox.SelectedIndex = (int)(editing?.Currency ?? Currency.ARS);
        TargetBox.Text = editing is not null ? Money.ToEditable(editing.TargetAmount) : "";
        DateCheck.IsChecked = editing?.TargetDate is not null;
        DateBox.Date = editing?.TargetDate ?? DateTime.Today.AddYears(1);
        TrackingBox.SelectedIndex = editing?.Tracking == GoalTracking.LinkedAccount ? 1 : 0;
        PlannedBox.Text = editing?.PlannedMonthly is { } p ? Money.ToEditable(p) : "";
        InflationToggle.IsOn = editing?.AdjustForInflation ?? true;
        NoteBox.Text = editing?.Note ?? "";
        Picker.Setup(AppIcons.ForCategories, editing?.Icon ?? "star", editing?.Color ?? AppIcons.Colors[0]);
        RefreshCurrency();
        AccountBox.SelectedItem = AccountBox.Items.OfType<AccountOption>().FirstOrDefault(a => a.Id == editing?.AccountId);
        ErrorBar.IsOpen = false;

        // Ideas rápidas. El fondo de emergencia usa 3 meses de tus gastos promedio.
        var emergency = stats is { AvgExpense: > 0 } ? stats.AvgExpense * 3 : 0;
        Idea[] ideas =
        [
            new("Fondo de emergencia", "Fondo de emergencia", "heart", 0xFF1F8A7A, Currency.ARS, emergency),
            new("Vacaciones", "Vacaciones", "plane", 0xFF2F6FB5, Currency.ARS, 0),
            new("Juntar dólares", "Ahorro en dólares", "dollar", 0xFF3F8F5A, Currency.USD, 0),
            new("Auto", "Auto", "car", 0xFF56657A, Currency.USD, 0),
            new("Celular nuevo", "Celular nuevo", "phone", 0xFF6B5BB5, Currency.ARS, 0),
            new("Mudanza", "Mudanza", "home", 0xFFC2603E, Currency.ARS, 0),
            new("Estudios", "Estudios", "education", 0xFF6B5BB5, Currency.ARS, 0),
        ];
        foreach (var idea in ideas)
        {
            var button = new Button { Content = idea.Label, Tag = idea, Padding = new Thickness(12, 4, 12, 5) };
            button.Click += OnIdeaClick;
            TemplatesList.Items.Add(button);
        }
    }

    private Currency Currency => (Currency)Math.Max(0, CurrencyBox.SelectedIndex);

    private void OnIdeaClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Idea idea) return;
        NameBox.Text = idea.Name;
        CurrencyBox.SelectedIndex = (int)idea.Currency;
        Picker.Setup(AppIcons.ForCategories, idea.Icon, idea.Color);
        if (idea.Target > 0) TargetBox.Text = Money.ToEditable(idea.Target);
        if (idea.Icon == "dollar")
        {
            var usd = _accounts.FirstOrDefault(a => a.Currency == Currency.USD);
            if (usd is not null)
            {
                TrackingBox.SelectedIndex = 1;
                AccountBox.SelectedItem = usd;
            }
        }
        TargetBox.Focus(FocusState.Programmatic);
    }

    private void OnCurrencyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetSymbol is null) return;
        RefreshCurrency();
    }

    private void RefreshCurrency()
    {
        TargetSymbol.Text = PlannedSymbol.Text = Currency.Symbol();
        InflationToggle.Visibility = Currency == Currency.ARS ? Visibility.Visible : Visibility.Collapsed;
        var keep = AccountBox.SelectedItem as AccountOption;
        var list = _accounts.Where(a => a.Currency == Currency).ToList();
        AccountBox.ItemsSource = list;
        AccountBox.SelectedItem = list.Contains(keep!) ? keep : list.FirstOrDefault();
    }

    private void OnTrackingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountBox is null) return;
        var linked = TrackingBox.SelectedIndex == 1;
        AccountBox.Visibility = AccountHint.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDateChecked(object sender, RoutedEventArgs e) => DateBox.IsEnabled = DateCheck.IsChecked == true;

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (Money.Parse(TargetBox.Text) is not { } target || target <= 0)
            {
                ShowError("Indicá cuánto querés juntar (ej. 1.500.000).");
                args.Cancel = true;
                return;
            }
            long? planned = null;
            if (!string.IsNullOrWhiteSpace(PlannedBox.Text))
            {
                if (Money.Parse(PlannedBox.Text) is not { } p || p < 0)
                {
                    ShowError("El aporte mensual no es un número válido.");
                    args.Cancel = true;
                    return;
                }
                planned = p;
            }

            var goal = new Goal
            {
                Id = _editing?.Id ?? 0,
                Name = NameBox.Text,
                Icon = Picker.IconKey,
                Color = Picker.Color,
                Currency = Currency,
                TargetAmount = target,
                TargetDate = DateCheck.IsChecked == true ? DateBox.Date?.Date : null,
                Tracking = TrackingBox.SelectedIndex == 1 ? GoalTracking.LinkedAccount : GoalTracking.Contributions,
                AccountId = (AccountBox.SelectedItem as AccountOption)?.Id,
                AdjustForInflation = InflationToggle.IsOn,
                PlannedMonthly = planned,
                Note = NoteBox.Text,
                CompletedAt = _editing?.CompletedAt,
                Archived = _editing?.Archived ?? false,
            };
            if (_editing is null) await _service.CreateAsync(goal);
            else await _service.UpdateAsync(goal);
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
