using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LocalChat.Web.Tests.Infrastructure;

/// <summary>Runs the real app in memory against a temporary database.</summary>
public sealed class LocalChatWebFactory : WebApplicationFactory<Program>
{
    private readonly bool _ownsDatabase;

    public LocalChatWebFactory()
        : this(TestDatabase.CreateEmpty(), ownsDatabase: true)
    {
    }

    private LocalChatWebFactory(TestDatabase database, bool ownsDatabase)
    {
        Database = database;
        _ownsDatabase = ownsDatabase;
    }

    public TestDatabase Database { get; }

    /// <summary>A factory over a database the caller owns, for tests that start the app more than once.</summary>
    public static LocalChatWebFactory ForDatabase(TestDatabase database) => new(database, ownsDatabase: false);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:LocalChat", Database.ConnectionString);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_ownsDatabase)
        {
            await Database.DisposeAsync();
        }
    }
}
