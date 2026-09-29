using Microsoft.Data.Sqlite;

namespace Tiel.Web.Data;

public static class SqliteConnectionStrings
{
    public const string LocalApplicationDataToken = "{LocalApplicationData}";

    /// <summary>The default database, under the local application data folder.</summary>
    public static readonly string DefaultRelativePath = Path.Combine("Tiel", "tiel.db");

    /// <summary>Where the default database lived before the app was renamed from LocalChat to Tiel.</summary>
    public static readonly string LegacyRelativePath = Path.Combine("LocalChat", "localchat.db");

    // SQLite keeps uncommitted and not-yet-checkpointed pages next to the database, under the same name.
    private static readonly string[] CompanionSuffixes = ["-wal", "-shm", "-journal"];

    /// <summary>
    /// Replaces <see cref="LocalApplicationDataToken"/> in the data source with <paramref name="localApplicationData"/>,
    /// creates the database folder if it is missing, and moves a LocalChat-era database to the default location
    /// (see <see cref="MoveLegacyDatabase"/>).
    /// </summary>
    public static string Resolve(string connectionString, string localApplicationData, ILogger? logger = null)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        builder.DataSource = builder.DataSource.Replace(LocalApplicationDataToken, localApplicationData, StringComparison.Ordinal);

        var dataSource = Path.GetFullPath(builder.DataSource);
        var folder = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        if (MoveLegacyDatabase(dataSource, localApplicationData))
        {
            logger?.LogInformation(
                "Moved the database from {Legacy} to {DataSource}, since the app was renamed from LocalChat to Tiel.",
                Path.Combine(localApplicationData, LegacyRelativePath), dataSource);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Moves the database (with its WAL and shared-memory files) from the LocalChat location to the default Tiel
    /// location, when <paramref name="dataSource"/> is that default location, it has no database yet, and the old one
    /// exists. A database configured anywhere else is left alone. Returns whether anything moved.
    /// </summary>
    public static bool MoveLegacyDatabase(string dataSource, string localApplicationData)
    {
        var target = Path.GetFullPath(Path.Combine(localApplicationData, DefaultRelativePath));
        var legacy = Path.GetFullPath(Path.Combine(localApplicationData, LegacyRelativePath));
        if (!string.Equals(Path.GetFullPath(dataSource), target, StringComparison.Ordinal)
            || File.Exists(target)
            || !File.Exists(legacy))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Companion files first and the database last: if this is interrupted, the database is still at the old
        // location, so the next start finishes the move.
        foreach (var suffix in CompanionSuffixes)
        {
            if (File.Exists(legacy + suffix))
            {
                File.Move(legacy + suffix, target + suffix, overwrite: true);
            }
        }

        File.Move(legacy, target);

        var legacyFolder = Path.GetDirectoryName(legacy)!;
        if (!Directory.EnumerateFileSystemEntries(legacyFolder).Any())
        {
            Directory.Delete(legacyFolder);
        }

        return true;
    }
}
