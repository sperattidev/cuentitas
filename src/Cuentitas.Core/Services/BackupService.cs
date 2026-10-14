using Cuentitas.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

/// <summary>Qué tiene una copia de seguridad (para mostrar antes de restaurar).</summary>
public sealed record BackupInfo(string Path, int Accounts, int Transactions, DateTime? LastMovement, DateTime FileDate);

/// <summary>Copias de seguridad de la base. Usa <c>VACUUM INTO</c>: copia consistente aunque la app esté abierta.</summary>
public sealed class BackupService(IDbContextFactory<CuentitasDbContext> factory, string? folder = null)
{
    /// <summary>
    /// Carpeta por defecto: Documentos\Cuentitas\Copias de seguridad. En modo desarrollo
    /// (CUENTITAS_DATA_DIR) va dentro de la carpeta de prueba, para no mezclar con las copias reales.
    /// </summary>
    public static string DefaultFolder =>
        Environment.GetEnvironmentVariable("CUENTITAS_DATA_DIR") is { Length: > 0 } dev
            ? Path.Combine(dev, "Copias de seguridad")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Cuentitas", "Copias de seguridad");

    /// <summary>Carpeta donde esta instancia guarda las copias.</summary>
    public string Folder => folder ?? DefaultFolder;

    /// <summary>Crea una copia con fecha y hora en el nombre. Devuelve la ruta del archivo creado.</summary>
    public async Task<string> CreateAsync(string? targetFolder = null, string suffix = "")
    {
        targetFolder ??= Folder;
        Directory.CreateDirectory(targetFolder);
        var path = Path.Combine(targetFolder, $"cuentitas-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{suffix}.db");
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {path}");
        return path;
    }

    /// <summary>
    /// Verifica que el archivo sea una base de Cuentitas y cuenta lo que tiene.
    /// Lanza <see cref="InvalidDataException"/> si no lo es.
    /// </summary>
    public static async Task<BackupInfo> InspectAsync(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException("El archivo no existe.");
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            await using var conn = new SqliteConnection(cs);
            await conn.OpenAsync();

            async Task<long> Scalar(string sql)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
            }

            var tables = await Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Accounts','Transactions','Categories','__EFMigrationsHistory')");
            if (tables < 4) throw new InvalidDataException("El archivo no es una copia de Cuentitas.");

            var accounts = (int)await Scalar("SELECT COUNT(*) FROM Accounts");
            var transactions = (int)await Scalar("SELECT COUNT(*) FROM Transactions");
            await using var last = conn.CreateCommand();
            last.CommandText = "SELECT MAX(Date) FROM Transactions";
            var lastValue = await last.ExecuteScalarAsync();
            DateTime? lastDate = lastValue is string s && DateTime.TryParse(s, out var d) ? d : null;
            return new BackupInfo(path, accounts, transactions, lastDate, File.GetLastWriteTime(path));
        }
        catch (SqliteException)
        {
            throw new InvalidDataException("El archivo no es una base de datos válida.");
        }
    }

    /// <summary>
    /// Reemplaza la base actual por la copia. Antes guarda una copia de seguridad de la actual
    /// (devuelve su ruta). Después hay que reiniciar la app (o reabrir contextos) y migrar si la copia es vieja.
    /// </summary>
    public async Task<string> RestoreAsync(string backupPath)
    {
        await InspectAsync(backupPath);
        var safety = await CreateAsync(suffix: "-antes-de-restaurar");

        string target;
        await using (var db = await factory.CreateDbContextAsync())
            target = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrEmpty(target) || target == ":memory:")
            throw new InvalidOperationException("La base actual no está en un archivo.");
        if (Path.GetFullPath(target).Equals(Path.GetFullPath(backupPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Elegiste la base actual: no hay nada que restaurar.");

        // Soltar los archivos de la base actual y copiar con un temporal, para no dejar una base a medias.
        SqliteConnection.ClearAllPools();
        var temp = target + ".restaurando";
        File.Copy(backupPath, temp, overwrite: true);
        foreach (var extra in new[] { target + "-wal", target + "-shm" })
            if (File.Exists(extra)) File.Delete(extra);
        File.Move(temp, target, overwrite: true);

        await Database.InitializeAsync(factory); // aplica migraciones si la copia es de una versión anterior
        return safety;
    }
}
