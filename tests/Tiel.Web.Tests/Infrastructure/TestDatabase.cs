using Tiel.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tiel.Web.Tests.Infrastructure;

/// <summary>A SQLite database in its own temporary folder, deleted on dispose.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _folder;

    private TestDatabase()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tiel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        // No pooling, so no connection keeps the file open after a test.
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_folder, "tiel.db"),
            Pooling = false,
        }.ToString();
        Factory = new TestDbContextFactory(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
    }

    public string ConnectionString { get; }
    public IDbContextFactory<AppDbContext> Factory { get; }

    public static TestDatabase CreateEmpty() => new();

    public static async Task<TestDatabase> CreateMigratedAsync()
    {
        var database = new TestDatabase();
        await using var db = database.Factory.CreateDbContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return database;
    }

    public AppDbContext CreateContext() => Factory.CreateDbContext();

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_folder, recursive: true);
        return ValueTask.CompletedTask;
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
