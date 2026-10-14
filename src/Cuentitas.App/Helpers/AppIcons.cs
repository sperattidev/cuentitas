using Microsoft.UI.Xaml.Media;

namespace Cuentitas.App.Helpers;

/// <summary>Un ícono del catálogo: glifo + fuente.</summary>
public sealed record AppIcon(string Key, string Glyph, FontFamily Font, string Label);

/// <summary>
/// Catálogo de íconos (ADR-007). La base guarda la CLAVE; acá se traduce a un glifo de
/// Segoe Fluent Icons (incluida en Windows 11). Códigos verificados contra la fuente instalada.
/// Para agregar uno: sumar una entrada. Nunca borrar ni renombrar claves en uso.
/// </summary>
public static class AppIcons
{
    private static readonly FontFamily Fluent = new("Segoe Fluent Icons,Segoe MDL2 Assets");
    private static readonly FontFamily Text = new("Segoe UI Variable Display,Segoe UI");

    private static AppIcon G(string key, char glyph, string label) => new(key, glyph.ToString(), Fluent, label);
    private static AppIcon T(string key, string text, string label) => new(key, text, Text, label);

    public static readonly IReadOnlyList<AppIcon> All =
    [
        // Cuentas y finanzas
        T("cash", "$", "Efectivo"),
        T("dollar", "US$", "Dólares"),
        G("bank", '', "Banco"),
        G("wallet", '', "Billetera virtual"),
        G("card", '', "Tarjeta"),
        G("growth", '', "Inversión"),
        G("chart", '', "Gráfico"),
        G("transfer", '', "Transferencia"),
        G("tag", '', "Etiqueta"),
        G("work", '', "Trabajo"),
        G("laptop", '', "Computadora"),
        G("calendar", '', "Calendario"),
        G("undo", '', "Devolución"),
        G("add", '', "Otros"),
        // Hogar y servicios
        G("home", '', "Casa"),
        G("building", '', "Edificio"),
        G("bolt", '', "Luz"),
        G("flame", '', "Gas"),
        G("drop", '', "Agua"),
        G("wifi", '', "Internet"),
        G("phone", '', "Teléfono"),
        G("receipt", '', "Boleta"),
        G("document", '', "Documento"),
        G("tools", '', "Herramientas"),
        // Comida y compras
        G("cart", '', "Supermercado"),
        G("leaf", '', "Verdulería"),
        G("bag", '', "Compras"),
        G("shirt", '', "Ropa"),
        G("dining", '', "Comida"),
        G("package", '', "Paquete"),
        G("coffee", '', "Café"),
        // Movilidad
        G("bus", '', "Colectivo"),
        G("train", '', "Tren"),
        G("car", '', "Auto"),
        G("parking", '', "Peajes"),
        G("plane", '', "Viajes"),
        // Salud, educación, personales
        G("health", '', "Salud"),
        G("medicine", '', "Farmacia"),
        G("education", '', "Educación"),
        G("book", '', "Libros"),
        G("people", '', "Salidas"),
        G("tv", '', "Streaming"),
        G("music", '', "Música"),
        G("game", '', "Juegos"),
        G("gift", '', "Regalos"),
        G("heart", '', "Cuidado"),
        G("sport", '', "Deporte"),
        G("star", '', "Favorito"),
        G("other", '', "Otros"),
    ];

    private static readonly Dictionary<string, AppIcon> ByKey = BuildIndex();

    private static Dictionary<string, AppIcon> BuildIndex()
    {
        var index = All.ToDictionary(i => i.Key);
        // El catálogo debe coincidir con las claves del Core (las que se guardan en la base).
        System.Diagnostics.Debug.Assert(
            index.Keys.ToHashSet().SetEquals(Core.Domain.IconKeys.All),
            "AppIcons y Core.Domain.IconKeys no coinciden");
        return index;
    }

    public static AppIcon Get(string? key) => key is not null && ByKey.TryGetValue(key, out var i) ? i : ByKey["other"];

    public static readonly string[] AccountKeys =
        ["cash", "dollar", "bank", "wallet", "card", "growth", "chart", "home", "work", "star"];

    public static readonly IReadOnlyList<AppIcon> ForAccounts = AccountKeys.Select(Get).ToList();

    public static readonly IReadOnlyList<AppIcon> ForCategories =
        All.Where(i => i.Key is not ("cash" or "dollar" or "transfer")).ToList();

    /// <summary>Paleta sobria (tonos medios que funcionan en claro y oscuro).</summary>
    public static readonly uint[] Colors =
    [
        0xFF1F8A7A, 0xFF2F6FB5, 0xFF6B5BB5, 0xFFB04A6E, 0xFFC2603E, 0xFFD07A2E,
        0xFFC9962B, 0xFF3F8F5A, 0xFF2B8FA8, 0xFF8A6A55, 0xFF6E7781, 0xFF56657A,
    ];
}
