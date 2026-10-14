using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Cuentitas.App.Services;
using Cuentitas.Core.Data;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;

namespace Cuentitas.App.ViewModels;

public sealed partial class SettingsViewModel(ThemeService theme, BackupService backup, QuoteService quotes) : ObservableObject
{
    // ── Cotización del dólar ─────────────────────────────────────────────────
    public static readonly Cuentitas.Core.Domain.DollarType[] ValuationTypes =
    [
        Cuentitas.Core.Domain.DollarType.Oficial, Cuentitas.Core.Domain.DollarType.Mep,
        Cuentitas.Core.Domain.DollarType.Blue, Cuentitas.Core.Domain.DollarType.Tarjeta,
        Cuentitas.Core.Domain.DollarType.Cripto,
    ];

    public List<string> ValuationOptions { get; } = ValuationTypes.Select(Helpers.DollarTypes.Description).ToList();

    [ObservableProperty] public partial int ValuationIndex { get; set; } = 1;
    [ObservableProperty] public partial bool QuotesEnabled { get; set; } = true;
    [ObservableProperty] public partial string QuotesText { get; set; } = "";
    [ObservableProperty] public partial string QuotesUpdated { get; set; } = "";
    [ObservableProperty] public partial bool Refreshing { get; set; }

    private bool _loadingQuotes;

    public async Task LoadQuotesAsync()
    {
        _loadingQuotes = true;
        QuotesEnabled = await quotes.IsEnabledAsync();
        ValuationIndex = Math.Max(0, Array.IndexOf(ValuationTypes, await quotes.GetValuationTypeAsync()));
        _loadingQuotes = false;
        ShowSnapshot(await quotes.GetCachedAsync());
    }

    public async Task RefreshQuotesAsync()
    {
        Refreshing = true;
        var snapshot = await quotes.RefreshAsync();
        Refreshing = false;
        if (snapshot is null)
        {
            QuotesUpdated = "No se pudo actualizar (¿hay internet?). Se sigue usando la última cotización guardada.";
            return;
        }
        ShowSnapshot(snapshot);
    }

    private void ShowSnapshot(QuoteSnapshot? s)
    {
        if (s is null)
        {
            QuotesText = "Todavía no hay cotizaciones guardadas.";
            QuotesUpdated = "";
            return;
        }
        QuotesText = string.Join("\n", ValuationTypes.Select(t => s.Get(t) is { } q
            ? $"{Helpers.DollarTypes.Description(t)}: compra {Cuentitas.Core.Money.FormatRate(q.Buy)} · venta {Cuentitas.Core.Money.FormatRate(q.Sell)}"
            : $"{Helpers.DollarTypes.Description(t)}: sin datos"));
        QuotesUpdated = $"Descargado {s.FetchedAt:dd/MM/yyyy HH:mm} desde dolarapi.com";
    }

    partial void OnValuationIndexChanged(int value)
    {
        if (_loadingQuotes || value < 0) return;
        _ = quotes.SetValuationTypeAsync(ValuationTypes[value]);
    }

    partial void OnQuotesEnabledChanged(bool value)
    {
        if (_loadingQuotes) return;
        _ = quotes.SetEnabledAsync(value);
    }

    // Reminders y Notifications son estáticas (también las usa el modo sin ventana).
    /// <summary>0 = Sistema, 1 = Claro, 2 = Oscuro.</summary>
    public int ThemeIndex
    {
        get => theme.Selected switch { ElementTheme.Light => 1, ElementTheme.Dark => 2, _ => 0 };
        set
        {
            var t = value switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
            if (t == theme.Selected) return;
            _ = theme.SetAsync(t);
            OnPropertyChanged();
        }
    }

    /// <summary>Hay una tarea programada de Windows para avisar con la app cerrada.</summary>
    [ObservableProperty] public partial bool RemindersEnabled { get; set; }
    [ObservableProperty] public partial bool RemindersReady { get; set; }
    [ObservableProperty] public partial string RemindersMessage { get; set; } = "";

    public bool RemindersAvailable => !Reminders.IsDevMode;

    public string RemindersHint => Reminders.IsDevMode
        ? "Desactivado en modo desarrollo (carpeta de datos de prueba)."
        : "Windows revisa tus vencimientos todos los días a las 10:00 y al iniciar sesión, y te avisa con una notificación aunque Cuentitas esté cerrada.";

    private bool _loadingReminders;

    public async Task LoadRemindersAsync()
    {
        _loadingReminders = true;
        RemindersEnabled = await Task.Run(Reminders.IsTaskEnabled);
        RemindersReady = RemindersAvailable;
        _loadingReminders = false;
    }

    partial void OnRemindersEnabledChanged(bool value)
    {
        if (_loadingReminders) return;
        _ = ApplyRemindersAsync(value);
    }

    private async Task ApplyRemindersAsync(bool enable)
    {
        RemindersReady = false;
        var error = await Task.Run(() => enable ? Reminders.EnableTask() : Reminders.DisableTask());
        RemindersMessage = error ?? (enable ? "Listo: te vamos a avisar de los vencimientos." : "Avisos desactivados.");
        if (error is not null)
        {
            _loadingReminders = true;
            RemindersEnabled = !enable;
            _loadingReminders = false;
        }
        RemindersReady = true;
    }

    public void TestNotification() =>
        RemindersMessage = Notifications.ShowTest()
            ? "Te mandamos una notificación de prueba."
            : "Windows no permitió mostrar la notificación. Revisá Configuración > Sistema > Notificaciones.";

    [ObservableProperty] public partial string BackupMessage { get; set; } = "";
    [ObservableProperty] public partial bool BackupOk { get; set; }

    public string DataFolder => Database.DataFolder;
    public string BackupFolder => BackupService.DefaultFolder;

    public string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.1.0";

    public async Task<string?> BackupAsync()
    {
        try
        {
            var path = await backup.CreateAsync();
            BackupOk = true;
            BackupMessage = $"Copia creada: {Path.GetFileName(path)}";
            return path;
        }
        catch (Exception ex)
        {
            BackupOk = false;
            BackupMessage = $"No se pudo crear la copia: {ex.Message}";
            return null;
        }
    }
}
