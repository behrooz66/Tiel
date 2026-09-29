using Tiel.Web.Data.Entities;
using Tiel.Web.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tiel.Web.Tests.Data;

public sealed class DataLayerTests : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Migrations_apply_to_an_empty_database()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var empty = TestDatabase.CreateEmpty();
        await using var db = empty.CreateContext();

        await db.Database.MigrateAsync(ct);

        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(ct), m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
        Assert.False(db.Database.HasPendingModelChanges(), "The model has changes that no migration captures.");
    }

    [Fact]
    public async Task Project_names_are_unique_regardless_of_case()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _database.CreateContext())
        {
            db.Projects.Add(TestData.NewProject("Work"));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _database.CreateContext())
        {
            db.Projects.Add(TestData.NewProject("WORK"));
            await AssertUniqueViolationAsync(db, ct);
        }
    }

    [Fact]
    public async Task Message_sequence_is_unique_within_a_conversation()
    {
        var ct = TestContext.Current.CancellationToken;
        var (first, second) = await AddTwoConversationsAsync(ct);
        await using (var db = _database.CreateContext())
        {
            db.Messages.Add(TestData.NewMessage(first.Id, sequence: 1));
            db.Messages.Add(TestData.NewMessage(second.Id, sequence: 1));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _database.CreateContext())
        {
            db.Messages.Add(TestData.NewMessage(first.Id, sequence: 1));
            await AssertUniqueViolationAsync(db, ct);
        }
    }

    [Fact]
    public async Task Setting_keys_are_unique()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _database.CreateContext())
        {
            db.AppSettings.Add(new AppSetting { Key = "Ollama.BaseUrl", Value = "http://a", UpdatedAt = TestData.Now });
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _database.CreateContext())
        {
            db.AppSettings.Add(new AppSetting { Key = "Ollama.BaseUrl", Value = "http://b", UpdatedAt = TestData.Now });
            await AssertUniqueViolationAsync(db, ct);
        }
    }

    [Fact]
    public async Task Model_tags_are_unique()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _database.CreateContext())
        {
            db.Models.Add(TestData.NewModel("phi4-mini:latest"));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _database.CreateContext())
        {
            db.Models.Add(TestData.NewModel("phi4-mini:latest"));
            await AssertUniqueViolationAsync(db, ct);
        }
    }

    [Fact]
    public async Task Deleting_a_project_deletes_its_conversations_and_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doomed, kept) = await AddTwoConversationsAsync(ct);
        await using (var db = _database.CreateContext())
        {
            db.Messages.AddRange(
                TestData.NewMessage(doomed.Id, 1),
                TestData.NewMessage(doomed.Id, 2, MessageRole.Assistant),
                TestData.NewMessage(kept.Id, 1));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _database.CreateContext())
        {
            // A set-based delete leaves the cascade entirely to the database.
            await db.Projects.Where(p => p.Id == doomed.ProjectId).ExecuteDeleteAsync(ct);
        }

        await using (var db = _database.CreateContext())
        {
            Assert.Equal([kept.Id], await db.Conversations.Select(c => c.Id).ToListAsync(ct));
            Assert.Equal([kept.Id], await db.Messages.Select(m => m.ConversationId).ToListAsync(ct));
        }
    }

    [Fact]
    public async Task A_model_in_use_cannot_be_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (conversation, _) = await AddTwoConversationsAsync(ct);
        await using var db = _database.CreateContext();

        var error = await Assert.ThrowsAsync<SqliteException>(
            () => db.Models.Where(m => m.Id == conversation.ModelId).ExecuteDeleteAsync(ct));

        Assert.Contains("FOREIGN KEY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timestamps_read_back_as_utc()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = TestData.NewModel();
        await using (var db = _database.CreateContext())
        {
            db.Models.Add(model);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _database.CreateContext())
        {
            var stored = await db.Models.SingleAsync(m => m.Id == model.Id, ct);
            Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
            Assert.Equal(TestData.Now, stored.CreatedAt);
        }
    }

    [Fact]
    public async Task Enums_are_stored_as_strings()
    {
        var ct = TestContext.Current.CancellationToken;
        var (conversation, _) = await AddTwoConversationsAsync(ct);
        await using var db = _database.CreateContext();
        db.Messages.Add(TestData.NewMessage(conversation.Id, 1, MessageRole.Assistant, MessageStatus.Cancelled));
        await db.SaveChangesAsync(ct);

        var stored = await db.Database
            .SqlQuery<string>($"SELECT Role || '/' || Status AS Value FROM Messages")
            .SingleAsync(ct);

        Assert.Equal("Assistant/Cancelled", stored);
    }

    private async Task<(Conversation First, Conversation Second)> AddTwoConversationsAsync(CancellationToken ct)
    {
        await using var db = _database.CreateContext();
        var model = TestData.NewModel();
        var firstProject = TestData.NewProject("First");
        var secondProject = TestData.NewProject("Second");
        var first = TestData.NewConversation(firstProject.Id, model.Id);
        var second = TestData.NewConversation(secondProject.Id, model.Id);
        db.AddRange(model, firstProject, secondProject, first, second);
        await db.SaveChangesAsync(ct);
        return (first, second);
    }

    private static async Task AssertUniqueViolationAsync(DbContext db, CancellationToken ct)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        Assert.Contains("UNIQUE constraint failed", error.InnerException?.Message, StringComparison.Ordinal);
    }
}
