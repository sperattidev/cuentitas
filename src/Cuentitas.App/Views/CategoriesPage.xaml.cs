using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Views;

public sealed partial class CategoriesPage : Page
{
    private CategoryOption? _menuTarget;

    public CategoriesPage()
    {
        ViewModel = App.Get<CategoriesViewModel>();
        InitializeComponent();
        foreach (var list in new[] { ItemsList, ArchivedList })
            list.ContainerContentChanging += (_, args) =>
                args.ItemContainer.ContextFlyout = (MenuFlyout)Resources["CategoryMenu"];
    }

    public CategoriesViewModel ViewModel { get; }

    private DialogService Dialogs => App.Get<DialogService>();

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();

    private async void OnNewClick(object sender, RoutedEventArgs e) =>
        await Dialogs.EditCategoryAsync(null, ViewModel.Kind);

    private async void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CategoryOption c) await Dialogs.EditCategoryAsync(c.Category, c.Category.Kind);
    }

    private void OnMenuOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        _menuTarget = menu.Target is ListViewItem { Content: CategoryOption c } ? c : null;
        if (menu.Items[1] is MenuFlyoutItem archive)
            archive.Text = _menuTarget?.Category.Archived == true ? "Desarchivar" : "Archivar";
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is { } c) await Dialogs.EditCategoryAsync(c.Category, c.Category.Kind);
    }

    private async void OnArchiveClick(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is { } c) await ViewModel.SetArchivedAsync(c, !c.Category.Archived);
    }
}
