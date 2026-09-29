using OllamaSharp;

namespace Tiel.Web.Services;

public interface IHealthService
{
    /// <summary>Checks the current URL, with a 5-second timeout.</summary>
    Task<HealthStatus> CheckAsync(CancellationToken ct);

    /// <summary>Checks an unsaved URL, with a 5-second timeout.</summary>
    Task<ConnectionTest> TestAsync(string baseUrl, CancellationToken ct);
}

public sealed class HealthService(
    IOllamaClientProvider clients,
    ISettingsService settings,
    ILogger<HealthService> logger) : IHealthService
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Shortened by tests.</summary>
    internal TimeSpan Timeout { get; init; } = DefaultTimeout;

    public async Task<HealthStatus> CheckAsync(CancellationToken ct)
    {
        var url = (await settings.GetAsync(ct)).OllamaBaseUrl;
        var (version, _) = await GetVersionAsync(await clients.GetApiClientAsync(ct), ct);
        return new HealthStatus(version is not null, version, url);
    }

    public async Task<ConnectionTest> TestAsync(string baseUrl, CancellationToken ct)
    {
        var url = SettingDefinitions.NormalizeUrl(baseUrl);
        if (SettingDefinitions.OllamaBaseUrl.Validate(url) is { } invalid)
        {
            return new ConnectionTest(false, null, invalid);
        }

        var (version, error) = await GetVersionAsync(clients.CreateApiClient(url), ct);
        return new ConnectionTest(version is not null, version, error);
    }

    /// <summary>Returns Ollama's version, or a short error message when it cannot be reached.</summary>
    private async Task<(string? Version, string? Error)> GetVersionAsync(IOllamaApiClient client, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return (await client.GetVersionAsync(timeout.Token), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, $"Ollama did not respond within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Checked every 30 seconds per open tab, so failures stay out of the normal log.
            logger.LogDebug(ex, "Ollama at {Url} is unreachable.", client.Uri);
            return (null, OllamaUnavailableException.Describe(ex));
        }
    }
}
