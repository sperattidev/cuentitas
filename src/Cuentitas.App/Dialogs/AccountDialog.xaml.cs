using Cuentitas.App.Helpers;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Dialogs;

/// <summary>Alta / edición de una cuenta.</summary>
public sealed partial class AccountDialog : ContentDialog
{
    /// <summary>Ícono sugerido por tipo de cuenta (mismo orden que <see cref="AccountType"/>).</summary>
    private static readonly string[] DefaultIconByType = ["cash", "bank", "wallet", "card", "growth"];
    private readonly AccountService _service;
    private readonly Account? _editing;

    public AccountDialog(AccountService service, Account? editing, bool currencyLocked, Currency? presetCurrency = null)
    {
        _service = service;
        _editing = editing;
        InitializeComponent();

        Title = editing is null ? "Nueva cuenta" : "Editar cuenta";
        NameBox.Text = editing?.Name ?? "";
        TypeBox.SelectedIndex = (int)(editing?.Type ?? (presetCurrency == Currency.USD ? AccountType.Cash : AccountType.Bank));
        CurrencyBox.SelectedIndex = (int)(editing?.Currency ?? presetCurrency ?? Currency.ARS);
        CurrencyBox.IsEnabled = !currencyLocked;
        CurrencyLockedText.Visibility = currencyLocked ? Visibility.Visible : Visibility.Collapsed;
        BalanceBox.Text = editing is { InitialBalance: not 0 } ? Money.ToEditable(editing.InitialBalance) : "";
        IncludeToggle.IsOn = editing?.IncludeInTotal ?? true;
        if (editing is not null) BalanceLabel.Text = "Saldo inicial (al crear la cuenta)";

        var icon = editing?.Icon ?? DefaultIcon();
        Picker.Setup(AppIcons.ForAccounts, icon, editing?.Color ?? AppIcons.Colors[1]);
        ErrorBar.IsOpen = false;
        UpdateSymbol();
    }

    /// <summary>Id de la cuenta creada o editada (válido si se guardó).</summary>
    public int SavedId { get; private set; }

    private string DefaultIcon() =>
        CurrencyBox.SelectedIndex == (int)Currency.USD && TypeBox.SelectedIndex == (int)AccountType.Cash
            ? "dollar"
            : DefaultIconByType[Math.Max(0, TypeBox.SelectedIndex)];

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e) => SuggestIcon();

    private void OnCurrencyChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSymbol();
        SuggestIcon();
    }

    /// <summary>Sugiere el ícono del tipo de cuenta, salvo que el usuario haya elegido otro.</summary>
    private void SuggestIcon()
    {
        if (_editing is not null || Picker is null || CurrencyBox is null || TypeBox.SelectedIndex < 0) return;
        if (!DefaultIconByType.Contains(Picker.IconKey) && Picker.IconKey != "dollar") return;
        Picker.SetIcon(DefaultIcon());
    }

    private void UpdateSymbol()
    {
        if (BalanceSymbol is null) return;
        BalanceSymbol.Text = ((Currency)Math.Max(0, CurrencyBox.SelectedIndex)).Symbol();
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            long balance = 0;
            if (!string.IsNullOrWhiteSpace(BalanceBox.Text))
            {
                if (Money.Parse(BalanceBox.Text) is not { } parsed)
                {
                    ShowError("El saldo inicial no es un número válido.");
                    args.Cancel = true;
                    return;
                }
                balance = parsed;
            }

            var account = new Account
            {
                Id = _editing?.Id ?? 0,
                Name = NameBox.Text,
                Type = (AccountType)TypeBox.SelectedIndex,
                Currency = (Currency)CurrencyBox.SelectedIndex,
                InitialBalance = balance,
                Icon = Picker.IconKey,
                Color = Picker.Color,
                IncludeInTotal = IncludeToggle.IsOn,
                Archived = _editing?.Archived ?? false,
            };

            if (_editing is null) SavedId = await _service.CreateAsync(account);
            else
            {
                await _service.UpdateAsync(account);
                SavedId = account.Id;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ShowError(ex.Message);
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
