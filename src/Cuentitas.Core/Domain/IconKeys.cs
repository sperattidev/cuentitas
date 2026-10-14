namespace Cuentitas.Core.Domain;

/// <summary>
/// Claves de ícono válidas (ADR-007). La app las traduce a glifos en Cuentitas.App/Helpers/AppIcons.cs,
/// que debe tener exactamente estas claves (lo verifica <c>AppIcons</c> al iniciar en Debug).
/// Agregar claves acá y allá a la vez. Nunca borrar ni renombrar una clave en uso.
/// </summary>
public static class IconKeys
{
    public static readonly IReadOnlyList<string> All =
    [
        "cash", "dollar", "bank", "wallet", "card", "growth", "chart", "transfer", "tag", "work", "laptop",
        "calendar", "undo", "add", "home", "building", "bolt", "flame", "drop", "wifi", "phone", "receipt",
        "document", "tools", "cart", "leaf", "bag", "shirt", "dining", "package", "coffee", "bus", "train", "car",
        "parking", "plane", "health", "medicine", "education", "book", "people", "tv", "music", "game", "gift",
        "heart", "sport", "star", "other",
    ];
}
