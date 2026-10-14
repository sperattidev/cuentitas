using System.Globalization;
using System.Text;

namespace Cuentitas.Core;

/// <summary>Monedas soportadas. El código ISO es lo que se guarda en la base.</summary>
public enum Currency
{
    ARS,
    USD,
}

public static class CurrencyInfo
{
    public static string Symbol(this Currency c) => c switch
    {
        Currency.USD => "US$",
        _ => "$",
    };

    public static string Label(this Currency c) => c switch
    {
        Currency.USD => "Dólares",
        _ => "Pesos",
    };
}

/// <summary>
/// Conversión entre centavos (<see cref="long"/>) y texto, formato argentino: <c>$ 1.234,56</c>.
/// Regla del proyecto: todo monto se guarda y opera como centavos enteros (ver docs/DECISIONES.md, ADR-003).
/// Este es el único lugar que convierte montos a/desde texto.
/// </summary>
public static class Money
{
    /// <summary>Formatea centavos: <c>$ 1.234,56</c>.</summary>
    /// <param name="showSign">Agrega "+" a los positivos (ingresos).</param>
    /// <param name="compact">Omite ",00" si no hay decimales.</param>
    public static string Format(long cents, Currency currency = Currency.ARS,
        bool showSign = false, bool compact = false, bool withSymbol = true)
    {
        var negative = cents < 0;
        var abs = Math.Abs(cents);
        var whole = abs / 100;
        var frac = abs % 100;

        var sb = new StringBuilder();
        if (negative) sb.Append('-');
        else if (showSign && cents > 0) sb.Append('+');
        if (withSymbol) sb.Append(currency.Symbol()).Append(' ');
        sb.Append(GroupThousands(whole));
        if (!(compact && frac == 0)) sb.Append(',').Append(frac.ToString("00", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string GroupThousands(long value)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder(digits.Length + digits.Length / 3);
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append('.');
            sb.Append(digits[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Convierte texto del usuario a centavos. Acepta <c>1234</c>, <c>1234,5</c>, <c>1.234,56</c>, <c>$ 1.234,56</c>.
    /// La coma es el separador decimal; los puntos son separadores de miles.
    /// Como comodidad, si hay un único punto seguido de 1 o 2 dígitos (<c>12.5</c>), se toma como decimal.
    /// </summary>
    /// <returns><c>null</c> si el texto no es un número válido.</returns>
    public static long? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var sb = new StringBuilder();
        foreach (var ch in input.Trim())
            if (char.IsDigit(ch) || ch is ',' or '.' or '-') sb.Append(ch);
        var s = sb.ToString();
        if (s.Length == 0 || s == "-") return null;

        var negative = s.StartsWith('-');
        s = s.Replace("-", "");

        // "12.5" o "12.50" sin comas → punto decimal.
        if (!s.Contains(',') && s.Count(c => c == '.') == 1)
        {
            var afterDot = s.Length - s.IndexOf('.') - 1;
            if (afterDot is 1 or 2) s = s.Replace('.', ',');
        }

        var parts = s.Replace(".", "").Split(',');
        if (parts.Length > 2) return null;
        var wholePart = parts[0].Length == 0 ? "0" : parts[0];
        var fracPart = parts.Length == 2 ? parts[1] : "";
        if (fracPart.Length > 2) return null;
        fracPart = fracPart.PadRight(2, '0');

        if (!long.TryParse(wholePart, NumberStyles.None, CultureInfo.InvariantCulture, out var whole)) return null;
        if (!long.TryParse(fracPart, NumberStyles.None, CultureInfo.InvariantCulture, out var frac)) return null;
        if (whole > long.MaxValue / 100 - 1) return null;

        var cents = whole * 100 + frac;
        return negative ? -cents : cents;
    }

    /// <summary>
    /// Tipo de cambio implícito de una compra: cuántos centavos de origen se pagaron por cada unidad de destino.
    /// Ej.: pagaste $ 120.000 por US$ 100 → 1200.
    /// </summary>
    public static decimal ImpliedRate(long paidCents, long receivedCents) =>
        receivedCents == 0 ? 0 : (decimal)paidCents / receivedCents;

    public static string FormatRate(decimal rate) => Format((long)Math.Round(rate * 100, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Monto redondeado a la unidad, sin decimales: para estimaciones y promedios ("$ 80.157"),
    /// donde los centavos no aportan.
    /// </summary>
    public static string FormatWhole(long cents, Currency currency = Currency.ARS, bool showSign = false) =>
        Format((long)Math.Round(cents / 100.0, MidpointRounding.AwayFromZero) * 100, currency, showSign, compact: true);

    /// <summary>Texto de un monto para editar en un campo (sin símbolo, ej. "1.234,56").</summary>
    public static string ToEditable(long cents) => Format(cents, withSymbol: false, compact: true);
}
