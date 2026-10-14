using System.Globalization;
using System.Text.Json;
using Cuentitas.Core.Domain;

namespace Cuentitas.Core.Services;

/// <summary>Cotización de un tipo de dólar en pesos (compra/venta, en pesos por dólar).</summary>
public sealed record DollarQuote(DollarType Type, decimal Buy, decimal Sell, DateTime UpdatedAt);

/// <summary>Última descarga de cotizaciones.</summary>
public sealed record QuoteSnapshot(IReadOnlyList<DollarQuote> Quotes, DateTime FetchedAt)
{
    public DollarQuote? Get(DollarType type) => Quotes.FirstOrDefault(q => q.Type == type);
}

/// <summary>
/// Cotizaciones del dólar en Argentina desde dolarapi.com (pública, sin cuenta).
/// Sólo se hace un GET: <b>nunca se envían datos del usuario</b> (ADR-015). La última descarga se guarda
/// en Settings para funcionar sin internet.
/// </summary>
public sealed class QuoteService(SettingsService settings, HttpClient http, DataChanges changes)
{
    public const string Endpoint = "https://dolarapi.com/v1/dolares";

    /// <summary>Cada cuánto vale la pena volver a pedir (la app lo hace sola al abrir).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);

    public static class Keys
    {
        public const string Snapshot = "quotes.snapshot";

        /// <summary>Con qué dólar se valúa el patrimonio total ("Mep", "Blue"…).</summary>
        public const string Valuation = "quotes.valuation";

        /// <summary>"false" = no consultar internet (se usa sólo la última cotización guardada).</summary>
        public const string Enabled = "quotes.enabled";
    }

    public async Task<bool> IsEnabledAsync() => await settings.GetAsync(Keys.Enabled) != "false";

    public async Task SetEnabledAsync(bool enabled)
    {
        await settings.SetAsync(Keys.Enabled, enabled ? "true" : "false");
        changes.Notify();
    }

    public async Task<QuoteSnapshot?> GetCachedAsync()
    {
        var json = await settings.GetAsync(Keys.Snapshot);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<QuoteSnapshot>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Descarga cotizaciones nuevas. Devuelve null (y deja las anteriores) si no hay conexión o falla.</summary>
    public async Task<QuoteSnapshot?> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var json = await http.GetStringAsync(Endpoint, timeout.Token);
            var quotes = Parse(json);
            if (quotes.Count == 0) return null;
            var snapshot = new QuoteSnapshot(quotes, DateTime.Now);
            await settings.SetAsync(Keys.Snapshot, JsonSerializer.Serialize(snapshot));
            changes.Notify();
            return snapshot;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Cotización guardada; si es vieja (o no hay) intenta actualizarla.</summary>
    public async Task<QuoteSnapshot?> GetFreshAsync(CancellationToken ct = default)
    {
        var cached = await GetCachedAsync();
        if (!await IsEnabledAsync()) return cached;
        if (cached is not null && DateTime.Now - cached.FetchedAt < MaxAge) return cached;
        return await RefreshAsync(ct) ?? cached;
    }

    public async Task<DollarType> GetValuationTypeAsync() =>
        Enum.TryParse<DollarType>(await settings.GetAsync(Keys.Valuation), out var t) ? t : DollarType.Mep;

    public async Task SetValuationTypeAsync(DollarType type)
    {
        await settings.SetAsync(Keys.Valuation, type.ToString());
        changes.Notify();
    }

    /// <summary>
    /// Convierte la respuesta de dolarapi.com. Mapea: oficial, blue, bolsa (= MEP), cripto, tarjeta.
    /// Ignora otras casas (CCL, mayorista) y entradas inválidas.
    /// </summary>
    public static List<DollarQuote> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<DollarQuote>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            DollarType? type = (e.TryGetProperty("casa", out var c) ? c.GetString() : null) switch
            {
                "oficial" => DollarType.Oficial,
                "blue" => DollarType.Blue,
                "bolsa" => DollarType.Mep,
                "cripto" => DollarType.Cripto,
                "tarjeta" => DollarType.Tarjeta,
                _ => null,
            };
            if (type is null) continue;
            if (!TryDecimal(e, "compra", out var buy) || !TryDecimal(e, "venta", out var sell) || sell <= 0) continue;
            var updated = e.TryGetProperty("fechaActualizacion", out var f) &&
                          DateTime.TryParse(f.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
                ? d.ToLocalTime()
                : DateTime.Now;
            result.Add(new DollarQuote(type.Value, buy, sell, updated));
        }
        return result;
    }

    private static bool TryDecimal(JsonElement e, string name, out decimal value)
    {
        value = 0;
        return e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out value);
    }
}
