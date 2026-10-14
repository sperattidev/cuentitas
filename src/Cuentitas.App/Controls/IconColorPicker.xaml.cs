using Cuentitas.App.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Cuentitas.App.Controls;

public sealed partial class IconColorPicker : UserControl
{
    private uint[] _colors = AppIcons.Colors;
    private IReadOnlyList<AppIcon> _icons = [];
    private bool _updating;

    public IconColorPicker()
    {
        InitializeComponent();
        ColorGrid.ItemsSource = _colors.Select(c => (SolidColorBrush)Ui.Brush(c)).ToList();
    }

    public string IconKey { get; private set; } = "other";
    public uint Color { get; private set; } = AppIcons.Colors[0];

    /// <summary>Configura el selector con los íconos disponibles y los valores actuales.</summary>
    public void Setup(IReadOnlyList<AppIcon> icons, string iconKey, uint color)
    {
        _updating = true;
        _icons = icons;
        IconGrid.ItemsSource = icons;
        IconKey = iconKey;
        Color = color;
        IconGrid.SelectedItem = icons.FirstOrDefault(i => i.Key == iconKey);

        var index = Array.IndexOf(_colors, color);
        if (index < 0)
        {
            _colors = [.. _colors, color];
            ColorGrid.ItemsSource = _colors.Select(c => (SolidColorBrush)Ui.Brush(c)).ToList();
            index = _colors.Length - 1;
        }
        ColorGrid.SelectedIndex = index;
        _updating = false;
        UpdatePreview();
    }

    /// <summary>Cambia el ícono desde código (ej. al cambiar el tipo de cuenta).</summary>
    public void SetIcon(string key)
    {
        IconKey = key;
        _updating = true;
        IconGrid.SelectedItem = _icons.FirstOrDefault(i => i.Key == key);
        _updating = false;
        UpdatePreview();
    }

    private void OnIconSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || IconGrid.SelectedItem is not AppIcon icon) return;
        IconKey = icon.Key;
        UpdatePreview();
    }

    private void OnColorSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ColorGrid.SelectedIndex < 0) return;
        Color = _colors[ColorGrid.SelectedIndex];
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        Preview.IconKey = IconKey;
        Preview.Color = Color;
        IconLabel.Text = AppIcons.Get(IconKey).Label;
    }
}
