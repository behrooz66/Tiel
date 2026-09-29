using Tiel.Web.Data.Entities;
using Tiel.Web.Services;
using Tiel.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tiel.Web.Tests.Services;

public sealed class ModelSyncServiceTests : IAsyncLifetime
{
    private static readonly string[] Chat = ["completion", "tools"];

    private TestDatabase _database = null!;
    private TestServices _services = null!;

    private FakeOllama Ollama => _services.Ollama;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateMigratedAsync();
        _services = new TestServices(_database);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Adds_a_new_model_with_defaults()
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", MaxContextLength: 131072, Chat));

        var result = await _services.ModelSync.SyncAsync(ct);

        Assert.Equal(new SyncResult(Added: 1, Updated: 0, MarkedUnavailable: 0), result);
        var model = Assert.Single(await ReadModelsAsync(ct));
        Assert.Equal("phi4-mini:latest", model.Tag);
        Assert.Equal("phi4-mini:latest", model.DisplayName);
        Assert.Equal(4096, model.ContextLength);
        // The fake also reports "phi3.rope.scaling.original_context_length" = 4096, which must not be taken.
        Assert.Equal(131072, model.MaxContextLength);
        Assert.True(model.IsAvailable);
    }

    [Theory]
    [InlineData(2048, 2048)]
    [InlineData(null, 4096)]
    public async Task The_default_context_length_is_the_smaller_of_the_max_and_4096(int? max, int expected)
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("tiny:latest", MaxContextLength: max, Chat));

        await _services.ModelSync.SyncAsync(ct);

        var model = Assert.Single(await ReadModelsAsync(ct));
        Assert.Equal(expected, model.ContextLength);
        Assert.Equal(max, model.MaxContextLength);
    }

    [Fact]
    public async Task Resync_keeps_the_users_display_name_and_context_length_and_refreshes_the_max()
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", MaxContextLength: 131072, Chat));
        await _services.ModelSync.SyncAsync(ct);
        var id = Assert.Single(await ReadModelsAsync(ct)).Id;
        await _services.Models.UpdateAsync(id, "Phi 4 mini", 8192, ct);
        Ollama.Models[0] = Ollama.Models[0] with { MaxContextLength = 65536 };

        var result = await _services.ModelSync.SyncAsync(ct);

        Assert.Equal(new SyncResult(Added: 0, Updated: 1, MarkedUnavailable: 0), result);
        var model = Assert.Single(await ReadModelsAsync(ct));
        Assert.Equal("Phi 4 mini", model.DisplayName);
        Assert.Equal(8192, model.ContextLength);
        Assert.Equal(65536, model.MaxContextLength);
    }

    [Fact]
    public async Task Resync_with_nothing_new_changes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: Chat));
        await _services.ModelSync.SyncAsync(ct);

        var result = await _services.ModelSync.SyncAsync(ct);

        Assert.Equal(new SyncResult(0, 0, 0), result);
    }

    [Fact]
    public async Task A_model_missing_from_ollama_is_marked_unavailable_not_deleted_and_comes_back()
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: Chat));
        Ollama.Models.Add(new FakeOllamaModel("llama3.2:3b", Capabilities: Chat, Architecture: "llama"));
        await _services.ModelSync.SyncAsync(ct);
        Ollama.Models.RemoveAll(m => m.Tag == "llama3.2:3b");

        var removed = await _services.ModelSync.SyncAsync(ct);

        Assert.Equal(new SyncResult(Added: 0, Updated: 0, MarkedUnavailable: 1), removed);
        var llama = (await ReadModelsAsync(ct)).Single(m => m.Tag == "llama3.2:3b");
        Assert.False(llama.IsAvailable);

        Ollama.Models.Add(new FakeOllamaModel("llama3.2:3b", Capabilities: Chat, Architecture: "llama"));
        var restored = await _services.ModelSync.SyncAsync(ct);

        Assert.Equal(new SyncResult(Added: 0, Updated: 1, MarkedUnavailable: 0), restored);
        Assert.Equal(llama.Id, (await ReadModelsAsync(ct)).Single(m => m.Tag == "llama3.2:3b" && m.IsAvailable).Id);
    }

    [Fact]
    public async Task Skips_embedding_models_but_keeps_models_that_report_no_capabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("nomic-embed-text:latest", Capabilities: ["embedding"], Architecture: "nomic-bert"));
        Ollama.Models.Add(new FakeOllamaModel("old-ollama-model:latest", Capabilities: null));

        var result = await _services.ModelSync.SyncAsync(ct);

        Assert.Equal(1, result.Added);
        Assert.Equal("old-ollama-model:latest", Assert.Single(await ReadModelsAsync(ct)).Tag);
    }

    [Fact]
    public async Task Sets_the_default_model_to_the_first_available_by_display_name_when_unset()
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: Chat));
        Ollama.Models.Add(new FakeOllamaModel("Llama3.2:3b", Capabilities: Chat, Architecture: "llama"));

        await _services.ModelSync.SyncAsync(ct);

        var llama = (await ReadModelsAsync(ct)).Single(m => m.Tag == "Llama3.2:3b");
        Assert.Equal(llama.Id, (await _services.Settings.GetAsync(ct)).DefaultModelId);
    }

    [Fact]
    public async Task Replaces_a_default_model_that_became_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("llama3.2:3b", Capabilities: Chat, Architecture: "llama"));
        Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: Chat));
        await _services.ModelSync.SyncAsync(ct);
        Ollama.Models.RemoveAll(m => m.Tag == "llama3.2:3b");

        await _services.ModelSync.SyncAsync(ct);

        var phi = (await ReadModelsAsync(ct)).Single(m => m.Tag == "phi4-mini:latest");
        Assert.Equal(phi.Id, (await _services.Settings.GetAsync(ct)).DefaultModelId);
    }

    [Fact]
    public async Task Keeps_a_default_model_that_is_still_available()
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("llama3.2:3b", Capabilities: Chat, Architecture: "llama"));
        Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: Chat));
        await _services.ModelSync.SyncAsync(ct);
        var phi = (await ReadModelsAsync(ct)).Single(m => m.Tag == "phi4-mini:latest");
        await _services.Settings.SetDefaultModelAsync(phi.Id, ct);

        await _services.ModelSync.SyncAsync(ct);

        Assert.Equal(phi.Id, (await _services.Settings.GetAsync(ct)).DefaultModelId);
    }

    [Fact]
    public async Task Leaves_the_default_unset_when_no_model_is_available()
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("nomic-embed-text:latest", Capabilities: ["embedding"]));

        await _services.ModelSync.SyncAsync(ct);

        Assert.Null((await _services.Settings.GetAsync(ct)).DefaultModelId);
    }

    [Theory]
    [InlineData(FakeOllamaState.Stopped, "Ollama is unreachable: Connection refused (localhost:11434)")]
    [InlineData(FakeOllamaState.Hanging, "Ollama did not respond within")]
    [InlineData(FakeOllamaState.NotOllama, "Ollama is unreachable: The server at this URL did not answer like Ollama.")]
    public async Task Throws_OllamaUnavailable_and_changes_nothing_when_ollama_does_not_answer(FakeOllamaState state, string error)
    {
        var ct = TestContext.Current.CancellationToken;
        Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: Chat));
        await _services.ModelSync.SyncAsync(ct);
        Ollama.State = state;

        var thrown = await Assert.ThrowsAsync<OllamaUnavailableException>(() => _services.ModelSync.SyncAsync(ct));

        Assert.StartsWith(error, thrown.Message, StringComparison.Ordinal);
        Assert.True(Assert.Single(await ReadModelsAsync(ct)).IsAvailable);
    }

    private async Task<List<Model>> ReadModelsAsync(CancellationToken ct)
    {
        await using var db = _database.CreateContext();
        return await db.Models.AsNoTracking().OrderBy(m => m.Tag).ToListAsync(ct);
    }
}
