using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tiel.Web.Data;
using Tiel.Web.Tests.Infrastructure;

namespace Tiel.Web.Tests.Data;

public sealed class SqliteConnectionStringsTests : IDisposable
{
    private const string DefaultConnectionString = "Data Source={LocalApplicationData}/Tiel/tiel.db";

    // Stands in for the local application data folder, so no test touches the real one.
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiel-tests", Guid.NewGuid().ToString("N"));

    private string Legacy => Path.Combine(_root, "LocalChat", "localchat.db");
    private string Target => Path.Combine(_root, "Tiel", "tiel.db");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Resolves_the_local_application_data_folder_and_creates_the_database_folder()
    {
        var resolved = SqliteConnectionStrings.Resolve(DefaultConnectionString, _root);

        Assert.Equal(Path.GetFullPath(Target), Path.GetFullPath(new SqliteConnectionStringBuilder(resolved).DataSource));
        Assert.True(Directory.Exists(Path.Combine(_root, "Tiel")));
    }

    [Fact]
    public void Moves_the_LocalChat_database_and_its_companion_files_to_the_default_location()
    {
        WriteFile(Legacy, "database");
        WriteFile(Legacy + "-wal", "write-ahead log");
        WriteFile(Legacy + "-shm", "shared memory");

        SqliteConnectionStrings.Resolve(DefaultConnectionString, _root);

        Assert.Equal("database", File.ReadAllText(Target));
        Assert.Equal("write-ahead log", File.ReadAllText(Target + "-wal"));
        Assert.Equal("shared memory", File.ReadAllText(Target + "-shm"));
        Assert.False(Directory.Exists(Path.Combine(_root, "LocalChat")), "The emptied LocalChat folder should be removed.");
    }

    [Fact]
    public async Task A_moved_database_keeps_its_data()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.GetDirectoryName(Legacy)!);
        await using (var db = Context(Legacy))
        {
            await db.Database.MigrateAsync(ct);
            db.Projects.Add(TestData.NewProject("Travel"));
            await db.SaveChangesAsync(ct);
        }

        SqliteConnectionStrings.Resolve(DefaultConnectionString, _root);

        await using var moved = Context(Target);
        Assert.Equal(["Travel"], await moved.Projects.Select(p => p.Name).ToListAsync(ct));
        Assert.False(File.Exists(Legacy));
    }

    [Fact]
    public void Leaves_both_alone_when_a_Tiel_database_already_exists()
    {
        WriteFile(Legacy, "old");
        WriteFile(Target, "new");

        SqliteConnectionStrings.Resolve(DefaultConnectionString, _root);

        Assert.Equal("old", File.ReadAllText(Legacy));
        Assert.Equal("new", File.ReadAllText(Target));
    }

    [Fact]
    public void Never_moves_into_a_database_configured_elsewhere()
    {
        WriteFile(Legacy, "old");
        var custom = Path.Combine(_root, "custom", "chat.db");

        SqliteConnectionStrings.Resolve($"Data Source={custom}", _root);

        Assert.Equal("old", File.ReadAllText(Legacy));
        Assert.False(File.Exists(custom));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public void Keeps_the_LocalChat_folder_when_other_files_are_left_in_it()
    {
        WriteFile(Legacy, "database");
        WriteFile(Path.Combine(_root, "LocalChat", "backup.db"), "backup");

        SqliteConnectionStrings.Resolve(DefaultConnectionString, _root);

        Assert.True(File.Exists(Target));
        Assert.True(File.Exists(Path.Combine(_root, "LocalChat", "backup.db")));
    }

    [Fact]
    public void Does_nothing_when_there_is_no_old_database()
    {
        Assert.False(SqliteConnectionStrings.MoveLegacyDatabase(Target, _root));
        Assert.False(File.Exists(Target));
    }

    private static AppDbContext Context(string path) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
        .Options);

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
