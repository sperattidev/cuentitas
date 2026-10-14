using Cuentitas.Core.Domain;

namespace Cuentitas.App.Helpers;

/// <summary>Nombres de los tipos de dólar como se dicen en Argentina (ADR-009).</summary>
public static class DollarTypes
{
    public static readonly DollarType[] All = Enum.GetValues<DollarType>();

    public static string Label(DollarType t) => t switch
    {
        DollarType.Oficial => "oficial",
        DollarType.Mep => "MEP",
        DollarType.Blue => "blue",
        DollarType.Tarjeta => "tarjeta",
        DollarType.Cripto => "cripto",
        _ => "(otro)",
    };

    public static string Description(DollarType t) => t switch
    {
        DollarType.Oficial => "Oficial (banco)",
        DollarType.Mep => "MEP (bolsa)",
        DollarType.Blue => "Blue (informal)",
        DollarType.Tarjeta => "Tarjeta",
        DollarType.Cripto => "Cripto (USDT)",
        _ => "Otro",
    };
}
