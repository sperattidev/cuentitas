using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;

namespace Cuentitas.App.Helpers;

/// <summary>Textos de vencimientos en castellano: "vence mañana", "venció hace 3 días"…</summary>
public static class DueText
{
    public static string Relative(DueOccurrence o, DateTime today)
    {
        var income = o.Item.Type == TxType.Income;
        var days = o.DaysFrom(today);
        return o.Status switch
        {
            DueStatus.Paid => income ? "Cobrado" : "Pagado",
            DueStatus.Skipped => "Omitido",
            _ when days == 0 => income ? "Se cobra hoy" : "Vence hoy",
            _ when days == 1 => income ? "Se cobra mañana" : "Vence mañana",
            _ when days > 1 => income ? $"Se cobra en {days} días" : $"Vence en {days} días",
            _ when days == -1 => income ? "Era para ayer" : "Venció ayer",
            _ => income ? $"Era para hace {-days} días" : $"Venció hace {-days} días",
        };
    }

    /// <summary>Monto a mostrar: el pagado, o el estimado ("aprox." si varía).</summary>
    public static string Amount(DueOccurrence o)
    {
        var currency = o.Item.Account?.Currency ?? Currency.ARS;
        if (o.Payment is { } p) return Money.Format(p.Amount, currency);
        if (o.Item.EstimatedAmount == 0) return "A confirmar";
        var text = Money.Format(o.Item.EstimatedAmount, currency, compact: true);
        return o.Item.VariableAmount ? $"≈ {text}" : text;
    }

    public static string Frequency(RecurringItem item)
    {
        var f = Schedule.Label(item.Frequency);
        return item.Frequency == Core.Domain.Frequency.Monthly
            ? $"{f}, día {item.StartDate.Day}"
            : $"{f}, desde {Ui.ShortDate(item.StartDate)}";
    }
}
