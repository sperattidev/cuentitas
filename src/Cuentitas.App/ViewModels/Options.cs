using Cuentitas.App.Helpers;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Cuentitas.App.ViewModels;

/// <summary>Cuenta para mostrar en selectores y listas.</summary>
public sealed class AccountOption(Account account, long? balance = null, long future = 0)
{
    /// <summary>Movimientos con fecha futura (ej. cuotas que faltan). Negativo = comprometido.</summary>
    public long Future { get; } = future;

    public string FutureText => Future switch
    {
        < 0 => $"Cuotas y pagos futuros: {Money.Format(Future, Account.Currency)}",
        > 0 => $"Ingresos futuros: {Money.Format(Future, Account.Currency, showSign: true)}",
        _ => "",
    };

    public Visibility FutureVisibility => Future != 0 ? Visibility.Visible : Visibility.Collapsed;

    public Account Account { get; } = account;
    public int Id => Account.Id;
    public string Name => Account.Name;
    public string IconKey => Account.Icon;
    public uint Color => Account.Color;
    public Currency Currency => Account.Currency;
    public long? Balance { get; } = balance;
    public string BalanceText => Balance is { } b ? Money.Format(b, Currency) : "";
    public string CurrencyCode => Currency.ToString();
    public Brush ColorBrush => Ui.Brush(Account.Color);

    public string TypeLabel => TypeLabelOf(Account.Type);

    public static string TypeLabelOf(AccountType type) => type switch
    {
        AccountType.Cash => "Efectivo",
        AccountType.Bank => "Cuenta bancaria",
        AccountType.Wallet => "Billetera virtual",
        AccountType.CreditCard => "Tarjeta de crédito",
        AccountType.Investment => "Inversión",
        _ => "",
    };

    public string Subtitle => $"{TypeLabel} · {Currency.Label()}";

    public static AccountOption From(AccountBalance b) => new(b.Account, b.Balance, b.Future);

    public override string ToString() => Name;
}

/// <summary>Categoría para selectores y listas.</summary>
public sealed class CategoryOption(Category category)
{
    public Category Category { get; } = category;
    public int Id => Category.Id;
    public string Name => Category.Name;
    public string IconKey => Category.Icon;
    public uint Color => Category.Color;
    public bool IsOptional => Category.DefaultNecessity == Necessity.Optional;

    public string NecessityLabel => Category.DefaultNecessity switch
    {
        Necessity.Necessary => "Necesario",
        Necessity.Optional => "Prescindible",
        _ => "",
    };

    public override string ToString() => Name;
}

/// <summary>Opción de filtro genérica para ComboBox (valor null = "Todos").</summary>
public sealed record FilterOption<T>(string Label, T? Value)
{
    public override string ToString() => Label;
}
