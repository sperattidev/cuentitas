using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;

namespace Cuentitas.App.ViewModels;

public sealed partial class CategoriesViewModel(DataChanges changes, CategoryService categories) : PageViewModel(changes)
{
    public ObservableCollection<CategoryOption> Items { get; } = [];
    public ObservableCollection<CategoryOption> ArchivedItems { get; } = [];

    /// <summary>0 = Gastos, 1 = Ingresos.</summary>
    [ObservableProperty] public partial int KindIndex { get; set; }
    [ObservableProperty] public partial Visibility ArchivedVisibility { get; set; } = Visibility.Collapsed;

    public CategoryKind Kind => KindIndex == 1 ? CategoryKind.Income : CategoryKind.Expense;

    protected override async Task LoadAsync()
    {
        var all = await categories.GetAsync(Kind, includeArchived: true);
        Items.Clear();
        ArchivedItems.Clear();
        foreach (var c in all) (c.Archived ? ArchivedItems : Items).Add(new CategoryOption(c));
        ArchivedVisibility = ArchivedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public Task SetArchivedAsync(CategoryOption c, bool archived) => categories.SetArchivedAsync(c.Id, archived);

    partial void OnKindIndexChanged(int value) => _ = ReloadAsync();
}
