using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;

namespace Cuentitas.App.ViewModels;

/// <summary>Una cuenta sugerida en la bienvenida (se marca y se completa el saldo).</summary>
public sealed partial class OnboardingAccount : ObservableObject
{
    public OnboardingAccount(string name, AccountType type, Currency currency, string icon, uint color, string hint, bool isCard = false)
    {
        Name = name;
        Type = type;
        Currency = currency;
        Icon = icon;
        Color = color;
        Hint = hint;
        IsCard = isCard;
    }

    [ObservableProperty] public partial bool Selected { get; set; }
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string BalanceText { get; set; } = "";

    public AccountType Type { get; }
    public Currency Currency { get; }
    public string Icon { get; }
    public uint Color { get; }
    public string Hint { get; }
    public bool IsCard { get; }
    public string Symbol => Currency.Symbol();

    partial void OnBalanceTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Selected = true;
    }
}

/// <summary>Un servicio sugerido en la bienvenida.</summary>
public sealed partial class OnboardingBill(string name, string category, Frequency frequency, int day, bool variable) : ObservableObject
{
    public string Name { get; } = name;
    public string Category { get; } = category;
    public Frequency Frequency { get; } = frequency;
    public string FrequencyText => Schedule.Label(Frequency).ToLowerInvariant();

    [ObservableProperty] public partial bool Selected { get; set; }
    [ObservableProperty] public partial string AmountText { get; set; } = "";
    [ObservableProperty] public partial double Day { get; set; } = day;
    [ObservableProperty] public partial bool Variable { get; set; } = variable;

    partial void OnAmountTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Selected = true;
    }
}

/// <summary>Bienvenida (v1.0): cuentas, sueldo, servicios y avisos, en 5 pasos.</summary>
public sealed partial class OnboardingViewModel(SetupService setup) : ObservableObject
{
    public const int Steps = 5;

    public ObservableCollection<OnboardingAccount> Accounts { get; } =
    [
        new("Caja de ahorro", AccountType.Bank, Currency.ARS, "bank", 0xFF2F6FB5, "Tu cuenta del banco (donde te pagan el sueldo)"),
        new("Mercado Pago", AccountType.Wallet, Currency.ARS, "wallet", 0xFF2B8FA8, "Billetera virtual"),
        new("Ualá", AccountType.Wallet, Currency.ARS, "wallet", 0xFF6B5BB5, "Billetera virtual"),
        new("Tarjeta de crédito", AccountType.CreditCard, Currency.ARS, "card", 0xFF6B5BB5, "Lo que debés del resumen (en positivo; lo cargamos como deuda)", isCard: true),
        new("Dólares", AccountType.Cash, Currency.USD, "dollar", 0xFF3F8F5A, "Dólares que tengas ahorrados"),
    ];

    public ObservableCollection<OnboardingBill> Bills { get; } =
    [
        new("Luz", "Luz", Frequency.Bimonthly, 15, true),
        new("Gas", "Gas", Frequency.Bimonthly, 15, true),
        new("Agua", "Agua", Frequency.Bimonthly, 20, true),
        new("Internet", "Internet y cable", Frequency.Monthly, 10, false),
        new("Celular", "Celular", Frequency.Monthly, 10, false),
        new("Alquiler", "Alquiler", Frequency.Monthly, 10, false),
        new("Expensas", "Expensas", Frequency.Monthly, 10, true),
        new("Prepaga", "Prepaga / obra social", Frequency.Monthly, 10, false),
        new("Streaming", "Streaming y suscripciones", Frequency.Monthly, 5, false),
    ];

    /// <summary>Cuentas para elegir dónde cobrás / con qué pagás (se arma al pasar al paso 3).</summary>
    public ObservableCollection<string> PayAccounts { get; } = [];

    [ObservableProperty] public partial int Step { get; set; }
    [ObservableProperty] public partial string CashText { get; set; } = "";
    [ObservableProperty] public partial string SalaryText { get; set; } = "";
    [ObservableProperty] public partial double SalaryDay { get; set; } = 1;
    [ObservableProperty] public partial int SalaryAccountIndex { get; set; }
    [ObservableProperty] public partial bool Aguinaldo { get; set; } = true;
    [ObservableProperty] public partial int BillsAccountIndex { get; set; }
    [ObservableProperty] public partial bool EnableReminders { get; set; } = true;
    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty] public partial bool Busy { get; set; }
    public bool CanContinue => !Busy;
    public uint CashColor => 0xFF3F8F5A;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanContinue));

    public Visibility Step0 => Step == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Step1 => Step == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Step2 => Step == 2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Step3 => Step == 3 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Step4 => Step == 4 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BackVisibility => Step is > 0 and < Steps ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NavVisibility => Step > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string NextText => Step == Steps - 1 ? "Empezar a usar Cuentitas" : "Siguiente";
    public string StepText => $"Paso {Step} de {Steps - 1}";
    public double Progress => 100.0 * Step / (Steps - 1);

    /// <summary>Avanza (validando el paso actual). En el último paso guarda todo; devuelve true al terminar.</summary>
    public async Task<bool> NextAsync()
    {
        Error = "";
        switch (Step)
        {
            case 1:
                if (ParseOrError(CashText, "efectivo") is null) return false;
                foreach (var a in Accounts.Where(a => a.Selected))
                {
                    if (string.IsNullOrWhiteSpace(a.Name)) { Error = "Poné un nombre a cada cuenta marcada."; return false; }
                    if (ParseOrError(a.BalanceText, a.Name) is null) return false;
                }
                RebuildPayAccounts();
                break;
            case 2:
                if (ParseOrError(SalaryText, "sueldo") is null) return false;
                break;
            case 3:
                foreach (var b in Bills.Where(b => b.Selected))
                    if (ParseOrError(b.AmountText, b.Name) is null) return false;
                break;
            case 4:
                return await FinishAsync();
        }
        Step++;
        return false;
    }

    public void Back()
    {
        Error = "";
        if (Step > 0) Step--;
    }

    /// <summary>Saltear: no carga nada, pero no vuelve a mostrar la bienvenida.</summary>
    public Task SkipAsync() => setup.MarkDoneAsync();

    private async Task<bool> FinishAsync()
    {
        var selected = Accounts.Where(a => a.Selected).ToList();
        // La opción del combo es un nombre de PayAccounts; se traduce a su posición en "selected" (-1 = Efectivo).
        int Index(int comboIndex) => comboIndex <= 0 || comboIndex >= PayAccounts.Count
            ? -1
            : selected.FindIndex(a => a.Name == PayAccounts[comboIndex]);
        var data = new SetupData
        {
            CashBalance = Money.Parse(CashText) ?? 0,
            Accounts = selected.Select(a =>
            {
                var balance = Money.Parse(a.BalanceText) ?? 0;
                return new SetupAccount(a.Name, a.Type, a.Currency, a.IsCard ? -Math.Abs(balance) : balance, a.Icon, a.Color);
            }).ToList(),
            Salary = Money.Parse(SalaryText) ?? 0,
            SalaryDay = (int)SalaryDay,
            SalaryAccountIndex = Index(SalaryAccountIndex),
            Aguinaldo = Aguinaldo,
            Bills = Bills.Where(b => b.Selected)
                .Select(b => new SetupBill(b.Name, b.Category, b.Frequency, (int)b.Day, Money.Parse(b.AmountText) ?? 0, b.Variable))
                .ToList(),
            BillsAccountIndex = Index(BillsAccountIndex),
        };
        Busy = true;
        try
        {
            await setup.ApplyAsync(data, DateTime.Today);
            return true;
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Opciones de cuenta en pesos para sueldo y servicios: Efectivo + las marcadas.</summary>
    private void RebuildPayAccounts()
    {
        PayAccounts.Clear();
        PayAccounts.Add("Efectivo");
        foreach (var a in Accounts.Where(a => a.Selected && a.Currency == Currency.ARS && !a.IsCard)) PayAccounts.Add(a.Name);
        // Por defecto: cobrar y pagar con la primera cuenta bancaria/billetera marcada.
        SalaryAccountIndex = BillsAccountIndex = PayAccounts.Count > 1 ? 1 : 0;
    }

    private long? ParseOrError(string text, string what)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (Money.Parse(text) is { } v && v >= 0) return v;
        Error = $"El monto de «{what}» no es válido (ej. 150.000 o 150000,50).";
        return null;
    }

    partial void OnStepChanged(int value)
    {
        foreach (var name in new[] { nameof(Step0), nameof(Step1), nameof(Step2), nameof(Step3), nameof(Step4),
                     nameof(BackVisibility), nameof(NavVisibility), nameof(NextText), nameof(StepText), nameof(Progress) })
            OnPropertyChanged(name);
    }

    /// <summary>Índices reales (en Accounts) de las opciones de PayAccounts, para el resumen.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            var accounts = Accounts.Count(a => a.Selected) + 1;
            parts.Add($"{accounts} {(accounts == 1 ? "cuenta" : "cuentas")}");
            if (Money.Parse(SalaryText) is > 0) parts.Add(Aguinaldo ? "sueldo y aguinaldo" : "sueldo");
            var bills = Bills.Count(b => b.Selected);
            if (bills > 0) parts.Add($"{bills} {(bills == 1 ? "servicio" : "servicios")} con aviso de vencimiento");
            return "Vamos a crear: " + string.Join(", ", parts) + ".";
        }
    }

    public void RefreshSummary() => OnPropertyChanged(nameof(Summary));
}
