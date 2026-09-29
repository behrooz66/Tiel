using Microsoft.Data.Sqlite;

namespace Tiel.Web.Data;

public static class SqliteConnectionStrings
{
    public const string LocalApplicationDataToken = "{LocalApplicationData}";

    /// <summary>
    /// Replaces <see cref="LocalApplicationDataToken"/> in the data source with
    /// <paramref name="localApplicationData"/> and creates the database folder if it is missing.
    /// </summary>
    public static string Resolve(string connectionString, string localApplicationData)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        builder.DataSource = builder.DataSource.Replace(LocalApplicationDataToken, localApplicationData, StringComparison.Ordinal);

        var folder = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        return builder.ToString();
    }
}
