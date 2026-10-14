using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cuentitas.Core.Data;

/// <summary>Ubicación del archivo de base y arranque (migraciones + datos iniciales).</summary>
public static class Database
{
    /// <summary>
    /// %LOCALAPPDATA%\Cuentitas — una carpeta por usuario de Windows (cada persona, sus datos).
    /// Para desarrollo/pruebas se puede usar otra con la variable de entorno CUENTITAS_DATA_DIR.
    /// </summary>
    public static string DataFolder =>
        Environment.GetEnvironmentVariable("CUENTITAS_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cuentitas");

    public static string DefaultPath => Path.Combine(DataFolder, "cuentitas.db");

    public static string ConnectionString(string path) => $"Data Source={path};Foreign Keys=True";

    public static void Configure(DbContextOptionsBuilder options, string path) =>
        options.UseSqlite(ConnectionString(path));

    /// <summary>Aplica migraciones pendientes y carga los datos iniciales. Llamar al iniciar la app.</summary>
    public static async Task InitializeAsync(IDbContextFactory<CuentitasDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await Seed.EnsureSeededAsync(db);
    }
}

/// <summary>Usado sólo por <c>dotnet ef</c> para generar migraciones.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CuentitasDbContext>
{
    public CuentitasDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CuentitasDbContext>();
        Database.Configure(options, "design-time.db");
        return new CuentitasDbContext(options.Options);
    }
}
