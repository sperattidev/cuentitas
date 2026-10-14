using System.Security.Cryptography;
using System.Text;

namespace Cuentitas.Core.Services;

/// <summary>
/// Bloqueo con PIN de la app (v1.0, ADR-017). Guarda sólo un hash PBKDF2-SHA256 con sal; nunca el PIN.
/// Protege la <b>pantalla</b> (que otra persona abra la app en tu sesión de Windows); no cifra la base.
/// </summary>
public sealed class PinService(SettingsService settings)
{
    public const int Iterations = 200_000;
    public const int MinLength = 4;
    public const int MaxLength = 8;

    /// <summary>Intentos fallidos seguidos antes de esperar.</summary>
    public const int MaxAttempts = 5;

    public static class Keys
    {
        /// <summary>"v1:{iteraciones}:{sal base64}:{hash base64}".</summary>
        public const string Hash = "lock.pinHash";

        /// <summary>"true" = también se puede desbloquear con Windows Hello.</summary>
        public const string Hello = "lock.hello";
    }

    public async Task<bool> IsEnabledAsync() => !string.IsNullOrEmpty(await settings.GetAsync(Keys.Hash));

    public async Task<bool> IsHelloEnabledAsync() => await settings.GetAsync(Keys.Hello) == "true";

    public Task SetHelloEnabledAsync(bool enabled) => settings.SetAsync(Keys.Hello, enabled ? "true" : "false");

    public static string? Validate(string pin)
    {
        if (pin.Length is < MinLength or > MaxLength) return $"El PIN tiene que tener entre {MinLength} y {MaxLength} números.";
        if (!pin.All(char.IsAsciiDigit)) return "El PIN sólo puede tener números.";
        if (pin.Distinct().Count() == 1) return "Elegí un PIN menos obvio (no todos los números iguales).";
        if ("0123456789".Contains(pin) || "9876543210".Contains(pin)) return "Elegí un PIN menos obvio (no una escalera).";
        return null;
    }

    public async Task SetPinAsync(string pin)
    {
        if (Validate(pin) is { } error) throw new ArgumentException(error);
        await settings.SetAsync(Keys.Hash, Hash(pin, RandomNumberGenerator.GetBytes(16), Iterations));
    }

    public async Task DisableAsync()
    {
        await settings.SetAsync(Keys.Hash, "");
        await settings.SetAsync(Keys.Hello, "false");
    }

    /// <summary>Comprueba el PIN (comparación en tiempo constante). Sin PIN configurado devuelve true.</summary>
    public async Task<bool> VerifyAsync(string pin)
    {
        var stored = await settings.GetAsync(Keys.Hash);
        if (string.IsNullOrEmpty(stored)) return true;
        var parts = stored.Split(':');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Derive(pin, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    internal static string Hash(string pin, byte[] salt, int iterations) =>
        $"v1:{iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(Derive(pin, salt, iterations))}";

    private static byte[] Derive(string pin, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, 32);
}
