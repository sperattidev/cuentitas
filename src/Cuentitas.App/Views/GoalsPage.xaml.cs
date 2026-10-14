using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Cuentitas.App.Views;

public sealed partial class GoalsPage : Page
{
    public GoalsPage()
    {
        ViewModel = App.Get<GoalsViewModel>();
        InitializeComponent();
    }

    public GoalsViewModel ViewModel { get; }

    private DialogService Dialogs => App.Get<DialogService>();

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();

    private async void OnParamsLostFocus(object sender, RoutedEventArgs e) => await ViewModel.ApplyParametersAsync();

    private async void OnNewClick(object sender, RoutedEventArgs e) => await Dialogs.EditGoalAsync(null, ViewModel.Stats);

    private static GoalCard? CardOf(object sender) => (sender as FrameworkElement)?.Tag as GoalCard;

    private async void OnContributeClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) await Dialogs.ContributeAsync(card.Goal);
    }

    private async void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) await Dialogs.GoalHistoryAsync(card.Goal);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } card) return;
        var menu = new MenuFlyout();
        void Add(string text, Symbol icon, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new SymbolIcon(icon) };
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }

        Add("Editar", Symbol.Edit, () => Dialogs.EditGoalAsync(card.Goal, ViewModel.Stats));
        Add("Marcar como cumplida", Symbol.Accept, async () =>
        {
            if (await Dialogs.ConfirmAsync($"¿Cumpliste «{card.Name}»?",
                    "La meta se archiva como cumplida. Sus aportes se conservan.", "Marcar cumplida"))
                await ViewModel.CompleteAsync(card.Goal);
        });
        Add("Archivar", Symbol.Folder, () => ViewModel.ArchiveAsync(card.Goal));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Eliminar", Symbol.Delete, async () =>
        {
            if (await Dialogs.ConfirmAsync($"¿Eliminar «{card.Name}»?",
                    "Se borran la meta y sus aportes. Tus cuentas y movimientos no cambian."))
                await ViewModel.DeleteAsync(card.Goal);
        });
        menu.ShowAt((FrameworkElement)sender, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }
}
