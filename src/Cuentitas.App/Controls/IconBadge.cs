using Cuentitas.App.Helpers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Controls;

/// <summary>
/// Ícono de cuenta/categoría: glifo del color elegido sobre un círculo del mismo color suavizado.
/// Uso: &lt;controls:IconBadge IconKey="cart" Color="{x:Bind Color}" Size="40" /&gt;
/// </summary>
public sealed partial class IconBadge : UserControl
{
    private readonly Border _circle = new();
    private readonly FontIcon _icon = new();

    public IconBadge()
    {
        _circle.Child = _icon;
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.VerticalAlignment = VerticalAlignment.Center;
        Content = _circle;
        IsTabStop = false;
        Update();
    }

    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(IconBadge), new PropertyMetadata("other", Changed));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(uint), typeof(IconBadge), new PropertyMetadata(0xFF6E7781u, Changed));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(IconBadge), new PropertyMetadata(40.0, Changed));

    public string IconKey
    {
        get => (string)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public uint Color
    {
        get => (uint)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((IconBadge)d).Update();

    private void Update()
    {
        var icon = AppIcons.Get(IconKey);
        var size = Size;
        _circle.Width = _circle.Height = size;
        _circle.CornerRadius = new CornerRadius(size / 2);
        _circle.Background = Ui.SoftBrush(Color, 0x2A);
        _icon.Glyph = icon.Glyph;
        _icon.FontFamily = icon.Font;
        _icon.Foreground = Ui.Brush(Color);
        var isText = icon.Glyph.Length > 1 || !char.IsSurrogate(icon.Glyph[0]) && icon.Glyph[0] < '';
        _icon.FontWeight = isText ? FontWeights.Bold : FontWeights.Normal;
        _icon.FontSize = isText ? size * (icon.Glyph.Length > 2 ? 0.28 : 0.42) : size * 0.45;
    }
}
