using Cuentitas.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

/// <summary>Preferencias de la app guardadas en la tabla Settings (clave/valor).</summary>
public sealed class SettingsService(IDbContextFactory<CuentitasDbContext> factory)
{
    /// <summary>Claves conocidas. Documentadas en docs/MODELO_DE_DATOS.md.</summary>
    public static class Keys
    {
        /// <summary>"System" | "Light" | "Dark".</summary>
        public const string Theme = "ui.theme";
    }

    public async Task<string?> GetAsync(string key)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value;
    }

    public async Task SetAsync(string key, string value)
    {
        await using var db = await factory.CreateDbContextAsync();
        var existing = await db.Settings.FindAsync(key);
        if (existing is null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else existing.Value = value;
        await db.SaveChangesAsync();
    }
}
