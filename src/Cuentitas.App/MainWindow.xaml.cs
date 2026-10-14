using Cuentitas.App.Services;
using Cuentitas.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Cuentitas.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["home"] = typeof(HomePage),
        ["transactions"] = typeof(TransactionsPage),
        ["bills"] = typeof(BillsPage),
        ["goals"] = typeof(GoalsPage),
        ["budgets"] = typeof(BudgetsPage),
        ["reports"] = typeof(ReportsPage),
        ["accounts"] = typeof(AccountsPage),
        ["categories"] = typeof(CategoriesPage),
        ["settings"] = typeof(SettingsPage),
    };

    public MainWindow()
    {
        InitializeComponent();
    }

    public XamlRoot XamlRoot => RootGrid.XamlRoot;

    public object? CurrentPage => ContentFrame.Content;

    public object? FullPage => FullFrame.Content;

    public async Task InitializeAsync()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "cuentitas.ico"));
        // Tamaños en píxeles efectivos: se escalan según la escala de pantalla (125%, 150%...).
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(1320 * scale), area.Width),
            Math.Min((int)(860 * scale), area.Height)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(760 * scale);
            presenter.PreferredMinimumHeight = (int)(560 * scale);
        }
        CenterOnScreen();

        await App.Theme.InitializeAsync(RootGrid, AppWindow);
        App.Theme.ThemeChanged += (_, _) => ReloadCurrentPage();

        await RefreshBillsBadgeAsync();
        App.Get<Core.Services.DataChanges>().Changed += async (_, _) => await RefreshBillsBadgeAsync();

        Nav.SelectedItem = Nav.MenuItems[0];
        Nav.Loaded += (_, _) => FixSettingsLabel();
        Nav.LayoutUpdated += (_, _) => FixSettingsLabel();
        Nav.DisplayModeChanged += (_, e) =>
            NewButtonText.Visibility = e.DisplayMode == NavigationViewDisplayMode.Expanded
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>Navega a una sección por su clave ("home", "transactions", ...).</summary>
    public void NavigateTo(string key, object? parameter = null)
    {
        var item = key == "settings"
            ? Nav.SettingsItem as NavigationViewItem
            : Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == key);
        if (item is not null && !ReferenceEquals(Nav.SelectedItem, item))
        {
            _pendingParameter = parameter;
            Nav.SelectedItem = item;
        }
        else
        {
            ContentFrame.Navigate(Pages[key], parameter, new SuppressNavigationTransitionInfo());
            // El ítem "Ajustes" lo crea el NavigationView recién al cargarse: lo marcamos después.
            if (item is null && key == "settings")
                DispatcherQueue.TryEnqueue(() => Nav.SelectedItem = Nav.SettingsItem);
        }
    }

    private object? _pendingParameter;

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var key = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (key is null || !Pages.TryGetValue(key, out var page)) return;
        var parameter = _pendingParameter;
        _pendingParameter = null;
        ContentFrame.Navigate(page, parameter, new EntranceNavigationTransitionInfo());
    }

    /// <summary>Recarga la página actual (p. ej. al cambiar de tema, para recalcular colores).</summary>
    public void ReloadCurrentPage()
    {
        if (ContentFrame.CurrentSourcePageType is { } type)
        {
            ContentFrame.Navigate(type, null, new SuppressNavigationTransitionInfo());
            ContentFrame.BackStack.Clear();
        }
    }

    private async void OnNewTransactionClick(object sender, RoutedEventArgs e) =>
        await App.Get<DialogService>().NewTransactionAsync();

    private async void OnNewTransactionAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (IsLocked || FullFrame.Visibility == Visibility.Visible) return;
        await App.Get<DialogService>().NewTransactionAsync();
    }

    /// <summary>El NavigationView recrea el ítem de configuración con el texto de Windows ("Configuración").</summary>
    private void FixSettingsLabel()
    {
        if (Nav.SettingsItem is NavigationViewItem { Content: not "Ajustes" } settings) settings.Content = "Ajustes";
    }

    // ── Bloqueo (ADR-017) ───────────────────────────────────────────────────
    private int _failedAttempts;
    private DateTime _lockedUntil;

    public bool IsLocked => LockOverlay.Visibility == Visibility.Visible;

    /// <summary>Bloquea si hay PIN configurado. Con Windows Hello activo lo ofrece de una.</summary>
    public async Task LockAsync(bool offerHello = true)
    {
        var pins = App.Get<Core.Services.PinService>();
        if (!await pins.IsEnabledAsync()) return;
        Nav.Visibility = Visibility.Collapsed;
        LockOverlay.Visibility = Visibility.Visible;
        PinBox.Password = "";
        LockError.Text = "";
        var hello = await pins.IsHelloEnabledAsync() && await WindowsHello.IsAvailableAsync();
        HelloButton.Visibility = hello ? Visibility.Visible : Visibility.Collapsed;
        PinBox.Focus(FocusState.Programmatic);
        if (hello && offerHello) await TryHelloAsync();
    }

    private void Unlock()
    {
        _failedAttempts = 0;
        LockOverlay.Visibility = Visibility.Collapsed;
        PinBox.Password = "";
        if (FullFrame.Visibility != Visibility.Visible) Nav.Visibility = Visibility.Visible;
    }

    private async Task TryPinAsync()
    {
        if (DateTime.Now < _lockedUntil)
        {
            LockError.Text = $"Demasiados intentos. Esperá {(int)(_lockedUntil - DateTime.Now).TotalSeconds + 1} segundos.";
            return;
        }
        if (await App.Get<Core.Services.PinService>().VerifyAsync(PinBox.Password))
        {
            Unlock();
            return;
        }
        _failedAttempts++;
        PinBox.Password = "";
        if (_failedAttempts >= Core.Services.PinService.MaxAttempts)
        {
            _lockedUntil = DateTime.Now.AddSeconds(30);
            _failedAttempts = 0;
            LockError.Text = "Demasiados intentos. Esperá 30 segundos.";
        }
        else
        {
            LockError.Text = "PIN incorrecto.";
        }
    }

    private async Task TryHelloAsync()
    {
        if (await WindowsHello.VerifyAsync("Desbloquear Cuentitas")) Unlock();
    }

    private async void OnUnlockClick(object sender, RoutedEventArgs e) => await TryPinAsync();

    private async void OnHelloClick(object sender, RoutedEventArgs e) => await TryHelloAsync();

    private async void OnPinKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await TryPinAsync();
    }

    private async void OnLockAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await LockAsync(offerHello: false);
    }

    // ── Bienvenida ──────────────────────────────────────────────────────────

    /// <summary>Muestra la bienvenida a pantalla completa (sin menú).</summary>
    public void ShowOnboarding()
    {
        Nav.Visibility = Visibility.Collapsed;
        FullFrame.Visibility = Visibility.Visible;
        FullFrame.Navigate(typeof(OnboardingPage), null, new EntranceNavigationTransitionInfo());
    }

    /// <summary>Termina la bienvenida y vuelve al Inicio.</summary>
    public void EndOnboarding()
    {
        FullFrame.Visibility = Visibility.Collapsed;
        FullFrame.Content = null;
        Nav.Visibility = Visibility.Visible;
        NavigateTo("home");
        ReloadCurrentPage();
    }

    /// <summary>Restaura y trae la ventana al frente; opcionalmente navega a una sección.</summary>
    public void BringToFront(string? page)
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (page is not null) NavigateTo(page);
    }

    /// <summary>Número en el ícono de "Vencimientos" (vencidos + por vencer). 0 lo oculta.</summary>
    public void SetBillsBadge(int count, bool overdue)
    {
        BillsBadge.Value = count;
        BillsBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BillsBadge.Style = (Style)Application.Current.Resources[overdue ? "CriticalValueInfoBadgeStyle" : "AttentionValueInfoBadgeStyle"];
    }

    public async Task RefreshBillsBadgeAsync()
    {
        var reminders = await App.Get<Core.Services.RecurringService>().GetRemindersAsync(DateTime.Today);
        SetBillsBadge(reminders.Count, reminders.Any(r => r.Status == Core.Services.DueStatus.Overdue));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            area.X + Math.Max(0, (area.Width - size.Width) / 2),
            area.Y + Math.Max(0, (area.Height - size.Height) / 2)));
    }
}
