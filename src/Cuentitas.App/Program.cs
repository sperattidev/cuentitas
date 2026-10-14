using System.Security.Cryptography;
using System.Text;
using Cuentitas.App.Services;
using Cuentitas.Core.Data;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Cuentitas.App;

/// <summary>
/// Punto de entrada propio (reemplaza el Main generado por XAML):
/// - <c>--check-due</c>: modo sin ventana para la tarea programada de Windows; avisa vencimientos y sale.
/// - Instancia única por carpeta de datos: si ya hay una ventana abierta, se la trae al frente.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (args.Contains("--check-due"))
            return Reminders.RunHeadlessAsync().GetAwaiter().GetResult();

        var main = AppInstance.FindOrRegisterForKey(InstanceKey());
        if (!main.IsCurrent)
        {
            // Ya hay una Cuentitas abierta: le pasamos la activación (abrir, clic en notificación) y salimos.
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

    /// <summary>Clave de instancia: una por carpeta de datos (la de desarrollo no choca con la real).</summary>
    private static string InstanceKey()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Database.DataFolder.ToLowerInvariant()));
        return "Cuentitas-" + Convert.ToHexString(hash)[..12];
    }
}
