using Cuentitas.App.ViewModels;
using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;

namespace Cuentitas.App.Services;

/// <summary>
/// Opciones de línea de comandos para desarrollo (docs/DESARROLLO.md § Opciones de desarrollo).
/// Sólo funcionan si CUENTITAS_DATA_DIR está definida, para no tocar nunca los datos reales.
///   --demo            carga datos de ejemplo
///   --theme=light|dark
///   --page=home|transactions|bills|goals|budgets|reports|accounts|categories|settings
///   --open=new|edit|buy|account|category|recurring|pay|goal|budget
/// </summary>
public static class DevOptions
{
    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindScrollViewer(DependencyObject node)
    {
        if (node is Microsoft.UI.Xaml.Controls.ScrollViewer sv) return sv;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindScrollViewer(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    public static async Task ApplyAsync(MainWindow window)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUENTITAS_DATA_DIR"))) return;
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        string? Arg(string name) =>
            args.FirstOrDefault(a => a.StartsWith($"--{name}=", StringComparison.Ordinal))?.Split('=', 2)[1];

        if (args.Contains("--demo"))
        {
            await using var db = await App.Get<IDbContextFactory<CuentitasDbContext>>().CreateDbContextAsync();
            await DemoData.LoadAsync(db, DateTime.Today);
            App.Get<Core.Services.DataChanges>().Notify();
        }

        switch (Arg("theme"))
        {
            case "light": await App.Theme.SetAsync(ElementTheme.Light); break;
            case "dark": await App.Theme.SetAsync(ElementTheme.Dark); break;
        }

        if (Arg("page") is { } page) window.NavigateTo(page);
        // --onboarding[=N]: muestra la bienvenida (y salta al paso N, para capturas).
        if (args.Any(a => a.StartsWith("--onboarding", StringComparison.Ordinal)))
        {
            window.ShowOnboarding();
            if (Arg("onboarding") is { } step && int.TryParse(step, out var n))
            {
                await Task.Delay(500);
                if (window.FullPage is Views.OnboardingPage ob)
                    for (var i = 0; i < n; i++) await ob.ViewModel.NextAsync();
            }
        }
        // --onboarding-fill: completa la bienvenida con datos de prueba y la termina (prueba de punta a punta).
        if (args.Contains("--onboarding-fill"))
        {
            window.ShowOnboarding();
            await Task.Delay(500);
            if (window.FullPage is Views.OnboardingPage ob)
            {
                var vm = ob.ViewModel;
                await vm.NextAsync();                                   // 0 → 1
                vm.CashText = "25.000";
                vm.Accounts[0].BalanceText = "480.000";                 // Caja de ahorro
                vm.Accounts[3].BalanceText = "120.000";                 // Tarjeta (deuda)
                vm.Accounts[4].BalanceText = "350";                     // Dólares
                await vm.NextAsync();                                   // 1 → 2
                vm.SalaryText = "1.150.000";
                vm.SalaryDay = 3;
                await vm.NextAsync();                                   // 2 → 3
                vm.Bills[0].Selected = true;                            // Luz (varía)
                vm.Bills[6].AmountText = "98.000";                      // Expensas
                vm.Bills[7].AmountText = "152.000";                     // Prepaga
                await vm.NextAsync();                                   // 3 → 4
                if (await vm.NextAsync()) window.EndOnboarding();       // guardar
            }
        }

        // --set-pin=NNNN: configura un PIN de prueba (sólo en la carpeta de datos de prueba).
        if (Arg("set-pin") is { } testPin) await App.Get<Core.Services.PinService>().SetPinAsync(testPin);
        // --lock: bloquea (si hay PIN) para capturar la pantalla de bloqueo.
        if (args.Contains("--lock")) await window.LockAsync(offerHello: false);
        // --cut=50: simulador de Metas con ese recorte (para capturas).
        if (Arg("cut") is { } cut && double.TryParse(cut, out var pct))
        {
            await Task.Delay(800);
            if (window.CurrentPage is Views.GoalsPage goalsPage) goalsPage.ViewModel.CutPercent = pct;
        }

        await Task.Delay(600);

        // --scroll=bottom: baja hasta el final de la página (para capturas).
        if (Arg("scroll") == "bottom" && window.CurrentPage is DependencyObject root)
        {
            await Task.Delay(1500);
            if (FindScrollViewer(root) is { } sv) sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true);
        }

        var dialogs = App.Get<DialogService>();
        switch (Arg("open"))
        {
            case "new": await dialogs.NewTransactionAsync(); break;
            case "buy": await BuyDollars.StartAsync(); break;
            case "account": await dialogs.EditAccountAsync(null); break;
            case "category": await dialogs.EditCategoryAsync(null, CategoryKind.Expense); break;
            case "recurring": await dialogs.EditRecurringAsync(null); break;
            case "budget": await dialogs.EditBudgetAsync(null, null); break;
            case "goal":
                var stats = (await App.Get<Core.Services.GoalService>().GetSavingsStatsAsync(DateTime.Today))[Core.Currency.ARS];
                await dialogs.EditGoalAsync(null, stats);
                break;
            case "pay":
                var next = (await App.Get<Core.Services.RecurringService>().GetRemindersAsync(DateTime.Today)).FirstOrDefault();
                if (next is not null) await dialogs.PayAsync(next);
                break;
            case "edit":
                var last = (await App.Get<Core.Services.TransactionService>()
                    .QueryAsync(new Core.Services.TxFilter { Limit = 1 })).FirstOrDefault();
                if (last is not null) await dialogs.EditTransactionAsync(last);
                break;
        }
    }
}
