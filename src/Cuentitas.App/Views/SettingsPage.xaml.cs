using System.Diagnostics;
using Cuentitas.App.Dialogs;
using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Cuentitas.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loadingSecurity;

    public SettingsPage()
    {
        ViewModel = App.Get<SettingsViewModel>();
        InitializeComponent();
        BackupBar.IsOpen = false;
        Loaded += async (_, _) =>
        {
            await LoadSecurityAsync();
            await ViewModel.LoadRemindersAsync();
            await ViewModel.LoadQuotesAsync();
        };
    }

    public SettingsViewModel ViewModel { get; }

    private PinService Pins => App.Get<PinService>();
    private DialogService Dialogs => App.Get<DialogService>();

    // ── Seguridad ───────────────────────────────────────────────────────────

    private async Task LoadSecurityAsync()
    {
        _loadingSecurity = true;
        var enabled = await Pins.IsEnabledAsync();
        var hello = await WindowsHello.IsAvailableAsync();
        PinToggle.IsOn = enabled;
        HelloToggle.IsOn = enabled && await Pins.IsHelloEnabledAsync();
        HelloToggle.IsEnabled = enabled && hello;
        ChangePinButton.IsEnabled = LockNowButton.IsEnabled = enabled;
        HelloHint.Text = hello
            ? "Windows Hello está disponible: podés desbloquear con tu huella, tu cara o el PIN de Windows."
            : "Windows Hello no está configurado en esta computadora (Configuración de Windows > Cuentas > Opciones de inicio de sesión).";
        _loadingSecurity = false;
    }

    private async void OnPinToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSecurity) return;
        if (PinToggle.IsOn)
        {
            await Dialogs.ShowCustomAsync(new SetPinDialog(Pins));
        }
        else if (await Dialogs.ShowCustomAsync(new VerifyPinDialog(Pins, "Desactivar el PIN")) == ContentDialogResult.Primary)
        {
            await Pins.DisableAsync();
        }
        await LoadSecurityAsync(); // refleja lo que realmente quedó (si canceló, vuelve el interruptor)
    }

    private async void OnHelloToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSecurity) return;
        if (HelloToggle.IsOn && !await WindowsHello.VerifyAsync("Confirmá que sos vos para activar Windows Hello en Cuentitas"))
        {
            await LoadSecurityAsync();
            return;
        }
        await Pins.SetHelloEnabledAsync(HelloToggle.IsOn);
    }

    private async void OnChangePinClick(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ShowCustomAsync(new VerifyPinDialog(Pins, "Cambiar el PIN")) == ContentDialogResult.Primary)
            await Dialogs.ShowCustomAsync(new SetPinDialog(Pins));
    }

    private async void OnLockNowClick(object sender, RoutedEventArgs e) => await App.MainWindow.LockAsync(offerHello: false);

    // ── Datos ───────────────────────────────────────────────────────────────

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".db");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        BackupInfo info;
        try
        {
            info = await BackupService.InspectAsync(file.Path);
        }
        catch (InvalidDataException ex)
        {
            await Dialogs.ShowMessageAsync("No se puede restaurar", ex.Message);
            return;
        }

        var last = info.LastMovement is { } d ? $"el último movimiento es del {d:dd/MM/yyyy}" : "no tiene movimientos";
        if (!await Dialogs.ConfirmAsync("¿Restaurar esta copia?",
                $"{file.Name}\n{info.Accounts} cuentas y {info.Transactions} movimientos; {last}.\n\n" +
                "Lo que tenés ahora se reemplaza por la copia. Antes guardamos automáticamente una copia de seguridad de lo actual.",
                "Restaurar"))
            return;

        try
        {
            var safety = await App.Get<BackupService>().RestoreAsync(file.Path);
            App.Get<DataChanges>().Notify();
            await Dialogs.ShowMessageAsync("Copia restaurada",
                $"Listo. Lo que tenías antes quedó guardado en «{Path.GetFileName(safety)}», por si lo necesitás.");
            App.MainWindow.NavigateTo("home");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException)
        {
            CrashLog.Write(ex);
            await Dialogs.ShowMessageAsync("No se pudo restaurar", ex.Message);
        }
    }

    private async void OnRefreshQuotesClick(object sender, RoutedEventArgs e) => await ViewModel.RefreshQuotesAsync();

    private async void OnExportClick(object sender, RoutedEventArgs e) => await App.Get<ExportFlow>().RunAsync();

    private void OnTestNotificationClick(object sender, RoutedEventArgs e) => ViewModel.TestNotification();

    private async void OnBackupClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.BackupAsync();
        BackupBar.Severity = ViewModel.BackupOk ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        BackupBar.Message = ViewModel.BackupMessage;
        BackupBar.IsOpen = true;
    }

    private void OnOpenBackupsClick(object sender, RoutedEventArgs e) => OpenFolder(ViewModel.BackupFolder);

    private void OnOpenDataClick(object sender, RoutedEventArgs e) => OpenFolder(ViewModel.DataFolder);

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
