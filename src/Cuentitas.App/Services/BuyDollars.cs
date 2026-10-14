using Cuentitas.App.ViewModels;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;

namespace Cuentitas.App.Services;

/// <summary>
/// Flujo "Comprar dólares": una transferencia de una cuenta en pesos a una en dólares (ADR-004).
/// Si todavía no hay cuenta en dólares, ofrece crearla primero.
/// </summary>
public static class BuyDollars
{
    public static async Task StartAsync()
    {
        var dialogs = App.Get<DialogService>();
        var balances = await App.Get<AccountService>().GetAllWithBalancesAsync();

        var from = balances.Where(b => b.Account.Currency == Currency.ARS)
            .OrderByDescending(b => b.Balance).FirstOrDefault();
        if (from is null)
        {
            await dialogs.ShowMessageAsync("Necesitás una cuenta en pesos",
                "Creá una cuenta en pesos (por ejemplo tu banco) para registrar de dónde sale la plata.");
            return;
        }

        var toId = balances.FirstOrDefault(b => b.Account.Currency == Currency.USD)?.Account.Id;
        if (toId is null)
        {
            var create = await dialogs.ConfirmAsync("Creá tu cuenta en dólares",
                "Para guardar los dólares que compres necesitás una cuenta en dólares " +
                "(por ejemplo «Dólares en efectivo» o tu caja de ahorro en USD). ¿La creamos ahora?",
                "Crear cuenta");
            if (!create) return;
            toId = await dialogs.EditAccountAsync(null, Currency.USD);
            if (toId is null) return;
        }

        await dialogs.NewTransactionAsync(new TransactionPreset(TxType.Transfer, from.Account.Id, toId));
    }
}
