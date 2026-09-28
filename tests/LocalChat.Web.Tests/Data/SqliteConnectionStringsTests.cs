using LocalChat.Web.Data;
using Microsoft.Data.Sqlite;

namespace LocalChat.Web.Tests.Data;

public sealed class SqliteConnectionStringsTests
{
    [Fact]
    public void Resolves_the_local_application_data_folder_and_creates_the_database_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "localchat-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var resolved = SqliteConnectionStrings.Resolve("Data Source={LocalApplicationData}/LocalChat/localchat.db", root);

            var dataSource = new SqliteConnectionStringBuilder(resolved).DataSource;
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "LocalChat", "localchat.db")), Path.GetFullPath(dataSource));
            Assert.True(Directory.Exists(Path.Combine(root, "LocalChat")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
