using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cuentitas.App.Helpers;

/// <summary>Tono semántico de un monto. Se resuelve a un pincel del tema actual.</summary>
public enum Tone
{
    Neutral,
    Income,
    Expense,
    Optional,
}

/// <summary>Helpers de presentación: fechas en español, colores, pinceles del tema.</summary>
public static class Ui
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>"Octubre 2026".</summary>
    public static string MonthTitle(DateTime month) => Capitalize(month.ToString("MMMM yyyy", Culture));

    /// <summary>"Hoy", "Ayer" o "Martes 14 de octubre" (con año si no es el actual).</summary>
    public static string DayHeader(DateTime date)
    {
        var today = DateTime.Today;
        if (date.Date == today) return "Hoy";
        if (date.Date == today.AddDays(-1)) return "Ayer";
        var format = date.Year == today.Year ? "dddd d 'de' MMMM" : "dddd d 'de' MMMM 'de' yyyy";
        return Capitalize(date.ToString(format, Culture));
    }

    /// <summary>"14 oct" o "14 oct 2025".</summary>
    public static string ShortDate(DateTime date) =>
        date.ToString(date.Year == DateTime.Today.Year ? "d MMM" : "d MMM yyyy", Culture).Replace(".", "");

    /// <summary>Monto corto para ejes de gráficos: "$ 1,2 M", "$ 350 mil", "$ 900".</summary>
    public static string ShortMoney(long cents)
    {
        var v = cents / 100.0;
        var sign = v < 0 ? "-" : "";
        v = Math.Abs(v);
        return v switch
        {
            >= 1_000_000 => $"{sign}$ {(v / 1_000_000).ToString("0.#", Culture)} M",
            >= 1_000 => $"{sign}$ {(v / 1_000).ToString("0", Culture)} mil",
            _ => $"{sign}$ {v.ToString("0", Culture)}",
        };
    }

    /// <summary>"oct", "nov"… (3 letras, sin punto).</summary>
    public static string MonthShort(DateTime month) => month.ToString("MMM", Culture).Replace(".", "");

    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Culture) + s[1..];

    public static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static SolidColorBrush Brush(uint argb) => new(ToColor(argb));

    /// <summary>El mismo color con transparencia, para fondos suaves detrás de un ícono.</summary>
    public static SolidColorBrush SoftBrush(uint argb, byte alpha = 0x2E)
    {
        var c = ToColor(argb);
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    /// <summary>
    /// Pincel de un tono según el tema efectivo de la ventana (no el del sistema).
    /// Las páginas se recargan al cambiar el tema (ver ThemeService).
    /// </summary>
    public static Brush ToneBrush(Tone tone) => tone switch
    {
        Tone.Income => ThemeResource("IncomeBrush"),
        Tone.Expense => ThemeResource("ExpenseBrush"),
        Tone.Optional => ThemeResource("OptionalBrush"),
        // Mismo valor que TextFillColorPrimary de Fluent.
        _ => new SolidColorBrush(App.Theme.ActualTheme == ElementTheme.Dark
            ? Colors.White
            : Color.FromArgb(0xE4, 0, 0, 0)),
    };

    public static Brush ThemeResource(string key)
    {
        // Se busca explícitamente en el diccionario del tema de la ventana: un TryGetValue
        // directo sobre Application.Resources resolvería con el tema del sistema.
        var theme = App.Theme.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        var dicts = Application.Current.Resources.MergedDictionaries;
        foreach (var dict in dicts)
        {
            if (dict.ThemeDictionaries.TryGetValue(theme, out var td) && td is ResourceDictionary rd &&
                rd.TryGetValue(key, out var value) && value is Brush b)
                return b;
        }
        return new SolidColorBrush(Colors.Gray);
    }
}
