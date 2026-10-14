using System.Collections.ObjectModel;
using Cuentitas.App.Helpers;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Cuentitas.App.ViewModels;

/// <summary>Un movimiento listo para mostrar en una lista.</summary>
public sealed class TransactionItem(Transaction tx)
{
    public Transaction Transaction { get; } = tx;

    private Currency Currency => Transaction.Account?.Currency ?? Currency.ARS;

    public string IconKey => Transaction.Type == TxType.Transfer
        ? (Transaction.IsCurrencyExchange ? "dollar" : "transfer")
        : Transaction.Category?.Icon ?? "other";

    /// <summary>Color de marca para transferencias; el de la categoría para el resto.</summary>
    public uint IconColor => Transaction.Type == TxType.Transfer
        ? 0xFF1F8A7A
        : Transaction.Category?.Color ?? 0xFF6E7781;

    public string Title => Transaction.Type switch
    {
        TxType.Transfer when Transaction.IsCurrencyExchange =>
            (Transaction.ToAccount!.Currency == Currency.USD ? "Compra de dólares" : "Venta de dólares") +
            (Transaction.DollarType is { } d ? $" {DollarTypes.Label(d)}" : ""),
        TxType.Transfer => "Transferencia",
        _ => Transaction.Category?.Name ?? "Sin categoría",
    };

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (Transaction.Date.Date > DateTime.Today) parts.Add("Programado");
            if (Transaction.InstallmentNumber is { } n) parts.Add($"Cuota {n}/{Transaction.InstallmentCount}");
            if (Transaction.RecurringItem is { } r && Transaction.DueDate is { } due)
                parts.Add($"{r.Name} (vto. {due:dd/MM})");
            if (!string.IsNullOrWhiteSpace(Transaction.Note)) parts.Add(Transaction.Note);
            parts.Add(Transaction.Type == TxType.Transfer
                ? $"{Transaction.Account?.Name} → {Transaction.ToAccount?.Name}"
                : Transaction.Account?.Name ?? "");
            return string.Join(" · ", parts);
        }
    }

    public string AmountText => Transaction.Type switch
    {
        TxType.Income => Money.Format(Transaction.Amount, Currency, showSign: true),
        TxType.Expense => Money.Format(-Transaction.Amount, Currency),
        _ => Money.Format(Transaction.Amount, Currency),
    };

    /// <summary>Segunda línea del monto: lo recibido y el tipo de cambio en compras de dólares.</summary>
    public string SecondaryAmountText => Transaction.IsCurrencyExchange && Transaction.ToAmount is { } to
        ? $"{Money.Format(to, Transaction.ToAccount!.Currency, showSign: true)} · TC {Money.FormatRate(Rate())}"
        : "";

    private decimal Rate() =>
        Transaction.ToAccount!.Currency == Currency.USD
            ? Money.ImpliedRate(Transaction.Amount, Transaction.ToAmount!.Value)
            : Money.ImpliedRate(Transaction.ToAmount!.Value, Transaction.Amount);

    public Visibility SecondaryVisibility => SecondaryAmountText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Brush AmountBrush => Ui.ToneBrush(Transaction.Type switch
    {
        TxType.Income => Tone.Income,
        TxType.Expense => Tone.Neutral,
        _ => Tone.Neutral,
    });

    public Visibility OptionalVisibility =>
        Transaction.Necessity == Necessity.Optional ? Visibility.Visible : Visibility.Collapsed;

    public string DateText => Ui.ShortDate(Transaction.Date);

    public bool IsInstallment => Transaction.InstallmentGroup is not null;
}

/// <summary>Grupo de movimientos de un mismo día (para la lista agrupada).</summary>
public sealed class TransactionGroup(DateTime date, IEnumerable<TransactionItem> items)
    : ObservableCollection<TransactionItem>(items)
{
    public DateTime Date { get; } = date;
    public string Header => Ui.DayHeader(Date);

    /// <summary>Total del día por moneda, sólo gastos e ingresos (ej. "-$ 12.300").</summary>
    public string TotalText
    {
        get
        {
            var totals = this.Where(i => i.Transaction.Type != TxType.Transfer)
                .GroupBy(i => i.Transaction.Account?.Currency ?? Currency.ARS)
                .Select(g => (g.Key, Sum: g.Sum(i =>
                    i.Transaction.Type == TxType.Income ? i.Transaction.Amount : -i.Transaction.Amount)))
                .Where(t => t.Sum != 0)
                .Select(t => Money.Format(t.Sum, t.Key, showSign: true));
            return string.Join("   ", totals);
        }
    }
}
