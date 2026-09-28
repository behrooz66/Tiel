using LocalChat.Web.Data;
using LocalChat.Web.Data.Entities;
using LocalChat.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LocalChat.Web.Tests.Data;

public sealed class SeedTests
{
    [Fact]
    public async Task Starting_twice_leaves_one_general_project_and_one_base_url_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = TestDatabase.CreateEmpty();

        for (var start = 0; start < 2; start++)
        {
            await using var app = LocalChatWebFactory.ForDatabase(database);
            using var client = app.CreateClient();
        }

        await using var db = database.CreateContext();
        var project = await db.Projects.SingleAsync(ct);
        Assert.Equal(ProjectIds.General, project.Id);
        Assert.Equal("General", project.Name);
        var setting = await db.AppSettings.SingleAsync(ct);
        Assert.Equal("Ollama.BaseUrl", setting.Key);
        Assert.Equal("http://localhost:11434", setting.Value);
    }

    [Fact]
    public async Task Startup_syncs_the_models_from_ollama()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new LocalChatWebFactory();
        app.Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: ["completion"]));
        app.Ollama.Models.Add(new FakeOllamaModel("llama3.2:3b", Capabilities: ["completion"], Architecture: "llama"));
        using var client = app.CreateClient();

        await using var db = app.Database.CreateContext();
        Assert.Equal(["llama3.2:3b", "phi4-mini:latest"], await db.Models.OrderBy(m => m.Tag).Select(m => m.Tag).ToListAsync(ct));
    }

    [Fact]
    public async Task Startup_continues_when_ollama_is_unreachable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = TestDatabase.CreateEmpty();
        var services = new TestServices(database);
        services.Ollama.State = FakeOllamaState.Stopped;

        await services.Seed.RunAsync(ct);

        await using var db = database.CreateContext();
        Assert.Equal(ProjectIds.General, (await db.Projects.SingleAsync(ct)).Id);
        Assert.Empty(await db.Models.ToListAsync(ct));
    }

    [Fact]
    public async Task Seeds_the_base_url_from_configuration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = TestDatabase.CreateEmpty();

        await CreateSeed(database, ollamaBaseUrl: " http://gpu-box:11434/ ").RunAsync(ct);

        await using var db = database.CreateContext();
        Assert.Equal("http://gpu-box:11434", (await db.AppSettings.SingleAsync(ct)).Value);
    }

    [Fact]
    public async Task Keeps_an_existing_base_url()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = TestDatabase.CreateEmpty();
        await CreateSeed(database, ollamaBaseUrl: "http://first:11434").RunAsync(ct);

        await CreateSeed(database, ollamaBaseUrl: "http://second:11434").RunAsync(ct);

        await using var db = database.CreateContext();
        Assert.Equal("http://first:11434", (await db.AppSettings.SingleAsync(ct)).Value);
    }

    [Fact]
    public async Task Marks_messages_left_streaming_as_interrupted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await TestDatabase.CreateMigratedAsync();
        var model = TestData.NewModel();
        var project = TestData.NewProject("Work");
        var conversation = TestData.NewConversation(project.Id, model.Id);
        var user = TestData.NewMessage(conversation.Id, 1);
        var assistant = TestData.NewMessage(conversation.Id, 2, MessageRole.Assistant, MessageStatus.Streaming, "Partial ans");
        await using (var db = database.CreateContext())
        {
            db.AddRange(model, project, conversation, user, assistant);
            await db.SaveChangesAsync(ct);
        }

        await CreateSeed(database).RunAsync(ct);

        await using (var db = database.CreateContext())
        {
            var interrupted = await db.Messages.SingleAsync(m => m.Id == assistant.Id, ct);
            Assert.Equal(MessageStatus.Error, interrupted.Status);
            Assert.Equal("Interrupted: the app stopped during generation.", interrupted.ErrorMessage);
            Assert.Equal("Partial ans", interrupted.Content);
            Assert.Equal(MessageStatus.Complete, (await db.Messages.SingleAsync(m => m.Id == user.Id, ct)).Status);
        }
    }

    private static Seed CreateSeed(TestDatabase database, string ollamaBaseUrl = "http://localhost:11434") =>
        new TestServices(database, ollamaBaseUrl: ollamaBaseUrl).Seed;
}
