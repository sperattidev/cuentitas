using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Cuentitas.Core.Data;
using Cuentitas.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Cuentitas.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.Handled = true;
        };
    }

    /// <summary>Contenedor de dependencias (servicios del Core y ViewModels).</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public static MainWindow MainWindow => ((App)Current)._window!;

    public static ThemeService Theme => Services.GetRequiredService<ThemeService>();

    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Directory.CreateDirectory(Database.DataFolder);
        Services = BuildServices(Database.DefaultPath);

        try
        {
            await Database.InitializeAsync(Get<IDbContextFactory<CuentitasDbContext>>());
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            throw;
        }

        _window = new MainWindow();
        await _window.InitializeAsync();
        await _window.LockAsync(offerHello: false); // antes de mostrar nada, si hay PIN
        _window.Activate();
        if (await Get<SetupService>().ShouldShowAsync() && !Environment.GetCommandLineArgs().Contains("--demo"))
            _window.ShowOnboarding();
        if (_window.IsLocked && await Get<PinService>().IsHelloEnabledAsync())
            _ = _window.LockAsync(); // ya con la ventana visible: ofrece Windows Hello

        Notifications.Register();

        // Otra instancia (doble clic al .exe, clic en una notificación → "cuentitas:bills") nos redirige su activación.
        AppInstance.GetCurrent().Activated += (_, e) => _window.DispatcherQueue.TryEnqueue(() =>
            _window.BringToFront(Notifications.PageFromArguments(
                (e.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments)));

        // Abierta desde una notificación.
        if (Notifications.PageFromArguments(string.Join(' ', Environment.GetCommandLineArgs().Skip(1))) is { } page)
            _window.NavigateTo(page);

        _ = Task.Run(Reminders.RefreshTaskPathIfNeeded);
        // Cotización del dólar: si la guardada tiene más de 3 horas, se actualiza en segundo plano.
        _ = Get<QuoteService>().GetFreshAsync();
        await DevOptions.ApplyAsync(_window);
    }

    /// <summary>Contenedor de servicios. También lo usa el modo sin ventana (--check-due).</summary>
    public static ServiceProvider BuildServices(string dbPath)
    {
        var s = new ServiceCollection();
        s.AddDbContextFactory<CuentitasDbContext>(o => Database.Configure(o, dbPath));

        // Core
        s.AddSingleton<DataChanges>();
        s.AddSingleton<AccountService>();
        s.AddSingleton<CategoryService>();
        s.AddSingleton<TransactionService>();
        s.AddSingleton<SummaryService>();
        s.AddSingleton<SettingsService>();
        s.AddSingleton<BackupService>();
        s.AddSingleton<RecurringService>();
        s.AddSingleton<GoalService>();
        s.AddSingleton<BudgetService>();
        s.AddSingleton<ReportService>();
        s.AddSingleton<ExportService>();
        s.AddSingleton(_ =>
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Cuentitas/1.0 (+app de escritorio)");
            return http;
        });
        s.AddSingleton<QuoteService>();
        s.AddSingleton<PinService>();
        s.AddSingleton<SetupService>();

        // App
        s.AddSingleton<ThemeService>();
        s.AddSingleton<DialogService>();
        s.AddTransient<HomeViewModel>();
        s.AddTransient<TransactionsViewModel>();
        s.AddTransient<AccountsViewModel>();
        s.AddTransient<CategoriesViewModel>();
        s.AddTransient<SettingsViewModel>();
        s.AddTransient<BillsViewModel>();
        s.AddTransient<GoalsViewModel>();
        s.AddTransient<BudgetsViewModel>();
        s.AddTransient<ReportsViewModel>();
        s.AddSingleton<ExportFlow>();
        s.AddTransient<OnboardingViewModel>();
        return s.BuildServiceProvider();
    }
}

/// <summary>Registro de errores no controlados en %LOCALAPPDATA%\Cuentitas\errores.log.</summary>
public static class CrashLog
{
    public static string PathFile => System.IO.Path.Combine(Database.DataFolder, "errores.log");

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Database.DataFolder);
            File.AppendAllText(PathFile, $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch
        {
            // Nunca fallar al registrar un fallo.
        }
    }
}
