using Microsoft.Extensions.AI;
using OllamaSharp;

namespace Tiel.Web.Services;

public interface IOllamaClientProvider
{
    /// <summary>The chat client for the current <c>Ollama.BaseUrl</c>.</summary>
    Task<IChatClient> GetChatClientAsync(CancellationToken ct);

    /// <summary>The Ollama API client for the current <c>Ollama.BaseUrl</c>, for Ollama-specific calls.</summary>
    Task<IOllamaApiClient> GetApiClientAsync(CancellationToken ct);

    /// <summary>An Ollama API client for another URL, such as one typed into Settings but not saved.</summary>
    IOllamaApiClient CreateApiClient(string baseUrl);
}

/// <summary>
/// Hands out one <see cref="OllamaApiClient"/> (both <see cref="IChatClient"/> and <see cref="IOllamaApiClient"/>)
/// per URL. A settings change drops the cached client, so the next call uses the new URL without a restart.
/// </summary>
public sealed class OllamaClientProvider : IOllamaClientProvider, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly HttpMessageHandler _handler;
    private CachedClient? _cached;

    public OllamaClientProvider(ISettingsService settings)
        : this(settings, new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) })
    {
    }

    /// <summary>For tests: sends every request through <paramref name="handler"/>.</summary>
    internal OllamaClientProvider(ISettingsService settings, HttpMessageHandler handler)
    {
        _settings = settings;
        _handler = handler;
        _settings.SettingsChanged += Reset;
    }

    public async Task<IChatClient> GetChatClientAsync(CancellationToken ct) => await GetCurrentAsync(ct);

    public async Task<IOllamaApiClient> GetApiClientAsync(CancellationToken ct) => await GetCurrentAsync(ct);

    public IOllamaApiClient CreateApiClient(string baseUrl) => Create(baseUrl);

    public void Dispose()
    {
        _settings.SettingsChanged -= Reset;
        _handler.Dispose();
    }

    private async Task<OllamaApiClient> GetCurrentAsync(CancellationToken ct)
    {
        var url = (await _settings.GetAsync(ct)).OllamaBaseUrl;
        if (Volatile.Read(ref _cached) is { } cached && cached.Url == url)
        {
            return cached.Client;
        }

        var created = new CachedClient(url, Create(url));
        Volatile.Write(ref _cached, created);
        return created.Client;
    }

    private void Reset() => Volatile.Write(ref _cached, null);

    private OllamaApiClient Create(string baseUrl)
    {
        // The clients share one handler and its connection pool, so a dropped client holds nothing open.
        // No overall timeout: a streamed reply can run for minutes. Callers bound other calls with their token.
        var http = new HttpClient(_handler, disposeHandler: false)
        {
            // OllamaSharp requests relative paths ("api/tags"), which need a trailing slash to keep any path prefix.
            BaseAddress = new Uri(SettingDefinitions.NormalizeUrl(baseUrl) + "/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new OllamaApiClient(http);
    }

    private sealed record CachedClient(string Url, OllamaApiClient Client);
}
