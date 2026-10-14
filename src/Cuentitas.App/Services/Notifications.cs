using System.Runtime.InteropServices;
using System.Security;
using Cuentitas.App.Helpers;
using Cuentitas.Core.Services;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Cuentitas.App.Services;

/// <summary>
/// Notificaciones de Windows para una app sin empaquetar y autocontenida.
///
/// Se usa la API clásica (Windows.UI.Notifications) porque AppNotificationManager del Windows App SDK
/// necesita el paquete "Singleton" del runtime, que una app autocontenida no trae (ADR-010).
/// Requisitos que se registran solos en HKCU (sin permisos de administrador):
/// - un AppUserModelID con nombre e ícono (para que Windows muestre "Cuentitas");
/// - el protocolo <c>cuentitas:</c>, para que el clic en la notificación abra la app en Vencimientos.
/// </summary>
public static class Notifications
{
    public const string AppId = "Cuentitas.App";

    /// <summary>Esquema del enlace de activación. En modo desarrollo no se registra (no pisa al real).</summary>
    public const string Scheme = "cuentitas";

    private static bool _registered;

    /// <summary>Registra AUMID y protocolo. Idempotente; nunca lanza excepciones.</summary>
    public static bool Register()
    {
        if (_registered) return true;
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            var exe = Environment.ProcessPath!;
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");

            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
            {
                key.SetValue("DisplayName", "Cuentitas");
                if (File.Exists(icon)) key.SetValue("IconUri", icon);
            }

            if (!Reminders.IsDevMode)
            {
                using var proto = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
                proto.SetValue("", "URL:Cuentitas");
                proto.SetValue("URL Protocol", "");
                using var command = proto.CreateSubKey(@"shell\open\command");
                command.SetValue("", $"\"{exe}\" \"%1\"");
            }
            _registered = true;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
        return _registered;
    }

    /// <summary>Página pedida por un enlace <c>cuentitas:bills</c> en los argumentos, o null.</summary>
    public static string? PageFromArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        var i = arguments.IndexOf(Scheme + ":", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var page = arguments[(i + Scheme.Length + 1)..].Trim(' ', '"', '/');
        return page.Length > 0 ? page : null;
    }

    /// <summary>Muestra un aviso con los vencimientos (uno detallado, o un resumen si son varios).</summary>
    public static bool ShowReminders(IReadOnlyList<DueOccurrence> reminders, DateTime today,
        IReadOnlyList<BudgetStatus>? budgets = null)
    {
        budgets ??= [];
        if (reminders.Count == 0 && budgets.Count == 0) return false;
        string title, body;
        if (reminders.Count == 0)
        {
            var over = budgets.Count(b => b.Level == BudgetLevel.Over);
            title = over > 0 ? "Te pasaste en un presupuesto" : "Un presupuesto viene justo";
            if (budgets.Count > 1) title = over > 0 ? $"Te pasaste en {over} de tus presupuestos" : $"{budgets.Count} presupuestos vienen justos";
            body = string.Join("\n", budgets.Take(3).Select(b => $"{b.Category.Name}: {b.Ratio * 100:0} % del presupuesto"));
            return Show(title, body, withButton: false);
        }
        if (reminders.Count == 1)
        {
            var o = reminders[0];
            title = $"{o.Item.Name}: {DueText.Relative(o, today).ToLowerInvariant()}";
            body = $"{DueText.Amount(o)} · {o.Item.Account?.Name}";
        }
        else
        {
            var overdue = reminders.Count(r => r.Status == DueStatus.Overdue);
            title = overdue > 0
                ? $"Tenés {reminders.Count} vencimientos ({overdue} {(overdue == 1 ? "vencido" : "vencidos")})"
                : $"Tenés {reminders.Count} vencimientos próximos";
            body = string.Join("\n", reminders.Take(3)
                .Select(o => $"{o.Item.Name}: {DueText.Relative(o, today).ToLowerInvariant()}"));
        }
        if (budgets.FirstOrDefault(b => b.Level == BudgetLevel.Over) is { } worst)
            body += $"\nPresupuesto de {worst.Category.Name}: {worst.Ratio * 100:0} %";
        return Show(title, body, withButton: true);
    }

    /// <summary>Notificación de prueba (botón en Ajustes).</summary>
    public static bool ShowTest() =>
        Show("Así te vamos a avisar", "Cuando se acerque un vencimiento vas a ver un aviso como este.", withButton: false);

    private static bool Show(string title, string body, bool withButton)
    {
        if (!Register()) return false;
        try
        {
            // Sin protocolo registrado (modo desarrollo) el clic sólo cierra el aviso.
            var launch = Reminders.IsDevMode ? "" : $" launch=\"{Scheme}:bills\" activationType=\"protocol\"";
            var actions = withButton && !Reminders.IsDevMode
                ? $"<actions><action content=\"Ver vencimientos\" activationType=\"protocol\" arguments=\"{Scheme}:bills\" /></actions>"
                : "";
            var xml = new XmlDocument();
            xml.LoadXml($"""
                <toast{launch}>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{SecurityElement.Escape(title)}</text>
                      <text>{SecurityElement.Escape(body)}</text>
                    </binding>
                  </visual>
                  {actions}
                </toast>
                """);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);
}
