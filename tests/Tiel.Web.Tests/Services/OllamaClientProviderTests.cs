using Tiel.Web.Services;
using Tiel.Web.Tests.Infrastructure;

namespace Tiel.Web.Tests.Services;

public sealed class OllamaClientProviderTests : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task The_next_call_after_a_url_change_uses_the_new_url()
    {
        var ct = TestContext.Current.CancellationToken;
        var ollama = new FakeOllama();
        var settings = new TestServices(_database, ollamaBaseUrl: "http://first-box:11434").Settings;
        using var provider = new OllamaClientProvider(settings, ollama);
        await (await provider.GetApiClientAsync(ct)).GetVersionAsync(ct);

        await settings.SetOllamaBaseUrlAsync("http://second-box:11434", ct);
        await (await provider.GetApiClientAsync(ct)).GetVersionAsync(ct);

        Assert.Equal(
            ["http://first-box:11434/api/version", "http://second-box:11434/api/version"],
            ollama.Requests.Select(u => u.ToString()));
    }

    [Fact]
    public async Task Reuses_one_client_per_url_for_chat_and_api_calls()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new TestServices(_database).Settings;
        using var provider = new OllamaClientProvider(settings, new FakeOllama());

        var api = await provider.GetApiClientAsync(ct);
        var chat = await provider.GetChatClientAsync(ct);

        Assert.Same(api, chat);
        Assert.Same(api, await provider.GetApiClientAsync(ct));
    }

    [Fact]
    public async Task Keeps_a_path_prefix_in_the_url()
    {
        var ct = TestContext.Current.CancellationToken;
        var ollama = new FakeOllama();
        var settings = new TestServices(_database, ollamaBaseUrl: "http://proxy/ollama").Settings;
        using var provider = new OllamaClientProvider(settings, ollama);

        await (await provider.GetApiClientAsync(ct)).GetVersionAsync(ct);

        Assert.Equal("http://proxy/ollama/api/version", Assert.Single(ollama.Requests).ToString());
    }
}
