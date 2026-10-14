using Cuentitas.App.Dialogs;
using Cuentitas.App.ViewModels;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Services;

/// <summary>Punto único para abrir diálogos (WinUI permite sólo uno a la vez).</summary>
public sealed class DialogService(IServiceProvider services)
{
    private bool _open;

    public Task NewTransactionAsync(TransactionPreset? preset = null) => OpenTransactionAsync(null, preset);

    public Task EditTransactionAsync(Transaction tx) => OpenTransactionAsync(tx, null);

    /// <summary>Abre el diálogo "Nueva cuenta". Devuelve el id creado, o null si se canceló.</summary>
    public async Task<int?> EditAccountAsync(Account? account, Currency? presetCurrency = null)
    {
        var service = services.GetRequiredService<AccountService>();
        var locked = account is not null && await service.HasTransactionsAsync(account.Id);
        var dialog = new AccountDialog(service, account, locked, presetCurrency);
        return await ShowAsync(dialog) == ContentDialogResult.Primary ? dialog.SavedId : null;
    }

    /// <summary>Alta/edición de un pago o cobro recurrente.</summary>
    public async Task EditRecurringAsync(RecurringItem? item, TxType type = TxType.Expense)
    {
        var recurring = services.GetRequiredService<RecurringService>();
        var accounts = (await services.GetRequiredService<AccountService>().GetAllWithBalancesAsync())
            .Select(AccountOption.From).ToList();
        if (accounts.Count == 0)
        {
            await ShowMessageAsync("Primero creá una cuenta", "Necesitás al menos una cuenta para pagar o cobrar.");
            return;
        }
        var categories = await services.GetRequiredService<CategoryService>().GetAsync();
        var salary = (await recurring.GetAllAsync())
            .FirstOrDefault(r => r.Type == TxType.Income && r.Name.Contains("sueldo", StringComparison.OrdinalIgnoreCase))
            ?.EstimatedAmount ?? 0;
        await ShowAsync(new RecurringDialog(recurring, item, type, categories, accounts, salary));
    }

    /// <summary>Alta/edición de una meta. <paramref name="stats"/> sugiere el fondo de emergencia.</summary>
    public async Task EditGoalAsync(Goal? goal, SavingsStats? stats)
    {
        var accounts = (await services.GetRequiredService<AccountService>().GetAllWithBalancesAsync())
            .Select(AccountOption.From).ToList();
        await ShowAsync(new GoalDialog(services.GetRequiredService<GoalService>(), goal, accounts, stats));
    }

    /// <summary>Aportar a una meta. Sugiere el aporte planificado.</summary>
    public Task ContributeAsync(Goal goal) =>
        ShowAsync(new ContributionDialog(services.GetRequiredService<GoalService>(), goal, goal.PlannedMonthly ?? 0));

    public Task GoalHistoryAsync(Goal goal) =>
        ShowAsync(new GoalHistoryDialog(services.GetRequiredService<GoalService>(), goal));

    /// <summary>Pagar (o cobrar) un vencimiento: abre "Nuevo movimiento" precargado y vinculado.</summary>
    public Task PayAsync(DueOccurrence occurrence)
    {
        var item = occurrence.Item;
        var income = item.Type == TxType.Income;
        return OpenTransactionAsync(null, new TransactionPreset(item.Type, item.AccountId)
        {
            CategoryId = item.CategoryId,
            Amount = item.EstimatedAmount > 0 ? item.EstimatedAmount : null,
            RecurringItemId = item.Id,
            DueDate = occurrence.DueDate,
            Title = income ? $"Cobrar {item.Name}" : $"Pagar {item.Name}",
        });
    }

    public async Task EditCategoryAsync(Category? category, CategoryKind kind)
    {
        var dialog = new CategoryDialog(services.GetRequiredService<CategoryService>(), category, kind);
        await ShowAsync(dialog);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Eliminar")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>Pregunta con dos opciones + Cancelar. Devuelve Primary, Secondary o None.</summary>
    public Task<ContentDialogResult> ChooseAsync(string title, string message, string primary, string secondary) =>
        ShowAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
            PrimaryButtonText = primary,
            SecondaryButtonText = secondary,
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        });

    /// <summary>Presupuesto de una categoría (null = elegir). <paramref name="currentLimit"/> != null = editar.</summary>
    public async Task EditBudgetAsync(Category? category, long? currentLimit)
    {
        var categories = await services.GetRequiredService<CategoryService>().GetAsync(CategoryKind.Expense);
        await ShowAsync(new BudgetDialog(services.GetRequiredService<BudgetService>(), categories, category, currentLimit));
    }

    /// <summary>Muestra un diálogo armado afuera (respeta la regla de uno a la vez).</summary>
    public Task<ContentDialogResult> ShowCustomAsync(ContentDialog dialog) => ShowAsync(dialog);

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
            CloseButtonText = "Entendido",
        };
        await ShowAsync(dialog);
    }

    private async Task OpenTransactionAsync(Transaction? tx, TransactionPreset? preset)
    {
        if (_open) return;
        var vm = new TransactionEditorViewModel(
            services.GetRequiredService<AccountService>(),
            services.GetRequiredService<CategoryService>(),
            services.GetRequiredService<TransactionService>());
        await vm.LoadAsync(tx, preset);
        if (vm.Accounts.Count == 0)
        {
            await ShowMessageAsync("Primero creá una cuenta", "Para cargar movimientos necesitás al menos una cuenta.");
            return;
        }
        await ShowAsync(new TransactionDialog(vm));
    }

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (_open) return ContentDialogResult.None;
        _open = true;
        try
        {
            dialog.XamlRoot = App.MainWindow.XamlRoot;
            dialog.RequestedTheme = App.Theme.Selected;
            return await dialog.ShowAsync();
        }
        finally
        {
            _open = false;
        }
    }
}
