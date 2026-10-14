using Cuentitas.App.Helpers;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Dialogs;

/// <summary>Alta / edición de una categoría.</summary>
public sealed partial class CategoryDialog : ContentDialog
{
    private readonly CategoryService _service;
    private readonly Category? _editing;
    private readonly CategoryKind _kind;

    public CategoryDialog(CategoryService service, Category? editing, CategoryKind kind)
    {
        _service = service;
        _editing = editing;
        _kind = editing?.Kind ?? kind;
        InitializeComponent();

        var kindLabel = _kind == CategoryKind.Expense ? "gasto" : "ingreso";
        Title = editing is null ? $"Nueva categoría de {kindLabel}" : "Editar categoría";
        NameBox.Text = editing?.Name ?? "";
        NecessityPanel.Visibility = _kind == CategoryKind.Expense ? Visibility.Visible : Visibility.Collapsed;
        NecessityBox.SelectedIndex = editing?.DefaultNecessity == Necessity.Optional ? 1 : 0;
        Picker.Setup(AppIcons.ForCategories, editing?.Icon ?? (_kind == CategoryKind.Expense ? "other" : "add"),
            editing?.Color ?? AppIcons.Colors[0]);
        ErrorBar.IsOpen = false;
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var category = new Category
            {
                Id = _editing?.Id ?? 0,
                Name = NameBox.Text,
                Kind = _kind,
                Icon = Picker.IconKey,
                Color = Picker.Color,
                DefaultNecessity = _kind == CategoryKind.Expense
                    ? (NecessityBox.SelectedIndex == 1 ? Necessity.Optional : Necessity.Necessary)
                    : null,
                Archived = _editing?.Archived ?? false,
            };
            if (_editing is null) await _service.CreateAsync(category);
            else await _service.UpdateAsync(category);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
