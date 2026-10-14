using Cuentitas.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Cuentitas.App.Services;

/// <summary>Tema claro/oscuro/sistema. Se guarda en Settings y se aplica a la raíz de la ventana.</summary>
public sealed class ThemeService(SettingsService settings)
{
    private FrameworkElement? _root;
    private AppWindow? _appWindow;

    public ElementTheme Selected { get; private set; } = ElementTheme.Default;

    /// <summary>Tema efectivo (resuelve "Sistema" a claro u oscuro).</summary>
    public ElementTheme ActualTheme => _root?.ActualTheme ?? ElementTheme.Light;

    /// <summary>Se dispara cuando cambia el tema efectivo (las páginas recargan colores calculados).</summary>
    public event EventHandler? ThemeChanged;

    public async Task InitializeAsync(FrameworkElement root, AppWindow appWindow)
    {
        _root = root;
        _appWindow = appWindow;
        Selected = await settings.GetAsync(SettingsService.Keys.Theme) switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        root.RequestedTheme = Selected;
        root.ActualThemeChanged += (_, _) =>
        {
            UpdateCaptionButtons();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        };
        UpdateCaptionButtons();
    }

    public async Task SetAsync(ElementTheme theme)
    {
        Selected = theme;
        if (_root is not null) _root.RequestedTheme = theme;
        await settings.SetAsync(SettingsService.Keys.Theme, theme switch
        {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "System",
        });
    }

    /// <summary>Los botones minimizar/maximizar/cerrar de la barra de título personalizada siguen el tema.</summary>
    private void UpdateCaptionButtons()
    {
        if (_appWindow is null) return;
        var dark = ActualTheme == ElementTheme.Dark;
        var tb = _appWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0, 0, 0);
        tb.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x08, 0, 0, 0);
        tb.ButtonInactiveForegroundColor = dark ? Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0, 0, 0);
    }
}
