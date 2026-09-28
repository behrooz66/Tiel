using LocalChat.Web.Services;
using LocalChat.Web.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalChat.Web.Tests.Services;

public sealed class HealthServiceTests : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private FakeOllama _ollama = null!;
    private HealthService _health = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateMigratedAsync();
        var services = new TestServices(_database);
        _ollama = services.Ollama;
        _health = new HealthService(services.Clients, services.Settings, NullLogger<HealthService>.Instance)
        {
            Timeout = TimeSpan.FromMilliseconds(200),
        };
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Reports_the_version_when_ollama_is_reachable()
    {
        var status = await _health.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new HealthStatus(true, "0.34.4", "http://localhost:11434"), status);
    }

    [Theory]
    [InlineData(FakeOllamaState.Stopped)]
    [InlineData(FakeOllamaState.Hanging)]
    [InlineData(FakeOllamaState.NotOllama)]
    public async Task Reports_unreachable_when_ollama_does_not_answer(FakeOllamaState state)
    {
        _ollama.State = state;

        var status = await _health.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new HealthStatus(false, null, "http://localhost:11434"), status);
    }

    [Fact]
    public void The_default_timeout_is_five_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), HealthService.DefaultTimeout);
    }

    [Fact]
    public async Task Tests_an_unsaved_url()
    {
        var test = await _health.TestAsync(" http://gpu-box:11434/ ", TestContext.Current.CancellationToken);

        Assert.Equal(new ConnectionTest(true, "0.34.4", null), test);
        Assert.Equal("http://gpu-box:11434/api/version", Assert.Single(_ollama.Requests).ToString());
    }

    [Theory]
    [InlineData(FakeOllamaState.Stopped, "Connection refused (gpu-box:11434)")]
    [InlineData(FakeOllamaState.Hanging, "Ollama did not respond within")]
    [InlineData(FakeOllamaState.NotOllama, "The server at this URL did not answer like Ollama.")]
    public async Task A_failed_test_explains_why(FakeOllamaState state, string error)
    {
        _ollama.State = state;

        var test = await _health.TestAsync("http://gpu-box:11434", TestContext.Current.CancellationToken);

        Assert.False(test.Ok);
        Assert.Null(test.Version);
        Assert.StartsWith(error, test.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_url_fails_the_test_without_a_request()
    {
        var test = await _health.TestAsync("gpu-box:11434", TestContext.Current.CancellationToken);

        Assert.False(test.Ok);
        Assert.StartsWith("Enter an absolute http or https URL", test.Error, StringComparison.Ordinal);
        Assert.Empty(_ollama.Requests);
    }
}
