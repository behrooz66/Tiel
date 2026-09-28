using LocalChat.Web.Services;

namespace LocalChat.Web.Tests.Infrastructure;

public sealed class FakeHealthService : IHealthService
{
    public static readonly HealthStatus Reachable = new(true, "0.34.4", "http://localhost:11434");
    public static readonly HealthStatus Unreachable = new(false, null, "http://localhost:11434");

    public HealthStatus Status { get; set; } = Reachable;
    public int Checks { get; private set; }
    public Func<string, ConnectionTest> Test { get; set; } = _ => new ConnectionTest(true, "0.34.4", null);

    public Task<HealthStatus> CheckAsync(CancellationToken ct)
    {
        Checks++;
        return Task.FromResult(Status);
    }

    public Task<ConnectionTest> TestAsync(string baseUrl, CancellationToken ct) => Task.FromResult(Test(baseUrl));
}

public sealed class FakeSettingsService : ISettingsService
{
    public AppSettingsSnapshot Snapshot { get; set; } = new("http://localhost:11434", null);

    public event Action? SettingsChanged;

    public Task<AppSettingsSnapshot> GetAsync(CancellationToken ct) => Task.FromResult(Snapshot);

    public Task SetOllamaBaseUrlAsync(string url, CancellationToken ct)
    {
        Snapshot = Snapshot with { OllamaBaseUrl = url };
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task SetDefaultModelAsync(Guid? modelId, CancellationToken ct)
    {
        Snapshot = Snapshot with { DefaultModelId = modelId };
        RaiseChanged();
        return Task.CompletedTask;
    }

    public void RaiseChanged() => SettingsChanged?.Invoke();
}
