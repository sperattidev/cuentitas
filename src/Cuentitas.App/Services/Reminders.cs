using System.Diagnostics;
using System.Security.Principal;
using Cuentitas.Core.Data;
using Cuentitas.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cuentitas.App.Services;

/// <summary>
/// Avisos de vencimientos aunque la app esté cerrada: una tarea programada de Windows ejecuta
/// <c>Cuentitas.exe --check-due</c> todos los días a las 10:00 y al iniciar sesión.
/// Se activa/desactiva desde Ajustes. Ver docs/ARQUITECTURA.md § Recordatorios.
/// </summary>
public static class Reminders
{
    public const string TaskName = @"Cuentitas\Recordatorios";
    private const string LastNotifiedKey = "reminders.lastNotified";

    /// <summary>En modo desarrollo (carpeta de datos de prueba) no se toca la tarea de Windows.</summary>
    public static bool IsDevMode => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUENTITAS_DATA_DIR"));

    /// <summary>Modo sin ventana: revisa vencimientos y muestra un aviso (máximo uno por día).</summary>
    public static async Task<int> RunHeadlessAsync()
    {
        try
        {
            var services = App.BuildServices(Database.DefaultPath);
            await Database.InitializeAsync(services.GetRequiredService<IDbContextFactory<CuentitasDbContext>>());
            var settings = services.GetRequiredService<SettingsService>();
            var today = DateTime.Today;
            var force = Environment.GetCommandLineArgs().Contains("--force");

            if (!force && await settings.GetAsync(LastNotifiedKey) == today.ToString("yyyy-MM-dd")) return 0;

            var reminders = await services.GetRequiredService<RecurringService>().GetRemindersAsync(today);
            var budgets = await services.GetRequiredService<BudgetService>().GetAlertsAsync(today);
            if ((reminders.Count > 0 || budgets.Count > 0) && Notifications.ShowReminders(reminders, today, budgets))
                await settings.SetAsync(LastNotifiedKey, today.ToString("yyyy-MM-dd"));
            return 0;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return 1;
        }
    }

    public static bool IsTaskEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>Crea (o actualiza) la tarea programada. Devuelve un mensaje de error o null si salió bien.</summary>
    public static string? EnableTask()
    {
        if (IsDevMode) return "En modo desarrollo no se programan avisos.";
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("No se encontró Cuentitas.exe");

        // Primero con aviso al iniciar sesión; si Windows no lo permite sin permisos de administrador, sólo diario.
        foreach (var withLogon in new[] { true, false })
        {
            var xml = Path.Combine(Path.GetTempPath(), "cuentitas-tarea.xml");
            File.WriteAllText(xml, TaskXml(exe, withLogon), System.Text.Encoding.Unicode);
            var code = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xml}\" /F");
            File.Delete(xml);
            if (code == 0) return null;
        }
        return "Windows no permitió crear la tarea programada.";
    }

    public static string? DisableTask() =>
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0 ? null : "No se pudo quitar la tarea programada.";

    /// <summary>Si la app se movió de carpeta, actualiza la ruta de la tarea.</summary>
    public static void RefreshTaskPathIfNeeded()
    {
        if (IsDevMode || !IsTaskEnabled()) return;
        var output = RunSchtasksOutput($"/Query /TN \"{TaskName}\" /XML");
        if (output is not null && Environment.ProcessPath is { } exe && !output.Contains(exe, StringComparison.OrdinalIgnoreCase))
            EnableTask();
    }

    private static string TaskXml(string exe, bool withLogon)
    {
        var user = WindowsIdentity.GetCurrent().Name;
        var logon = withLogon
            ? $"<LogonTrigger><UserId>{Escape(user)}</UserId><Delay>PT2M</Delay></LogonTrigger>"
            : "";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Avisos de vencimientos de Cuentitas</Description></RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>2026-01-01T10:00:00</StartBoundary>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
                {logon}
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <StartWhenAvailable>true</StartWhenAvailable>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(exe)}</Command>
                  <Arguments>--check-due</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string Escape(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    private static int RunSchtasks(string args)
    {
        using var p = Start(args);
        p.WaitForExit(15000);
        return p.HasExited ? p.ExitCode : -1;
    }

    private static string? RunSchtasksOutput(string args)
    {
        using var p = Start(args);
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        return p.ExitCode == 0 ? output : null;
    }

    private static Process Start(string args) => Process.Start(new ProcessStartInfo("schtasks.exe", args)
    {
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    })!;
}
