using Windows.Security.Credentials.UI;

namespace Cuentitas.App.Services;

/// <summary>
/// Windows Hello (huella, cara o el PIN de Windows) para desbloquear la app.
/// En apps sin empaquetar hay que pasar la ventana dueña del diálogo (UserConsentVerifierInterop).
/// </summary>
public static class WindowsHello
{
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return false;
        }
    }

    /// <summary>Pide verificación. Devuelve true sólo si Windows confirmó la identidad.</summary>
    public static async Task<bool> VerifyAsync(string message)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd, message);
            return result == UserConsentVerificationResult.Verified;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return false;
        }
    }
}
