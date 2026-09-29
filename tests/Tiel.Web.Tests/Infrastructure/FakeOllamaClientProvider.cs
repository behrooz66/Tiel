using Tiel.Web.Services;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace Tiel.Web.Tests.Infrastructure;

/// <summary>Hands out real OllamaSharp clients that talk to a <see cref="FakeOllama"/>.</summary>
public sealed class FakeOllamaClientProvider(FakeOllama ollama) : IOllamaClientProvider
{
    public const string BaseUrl = "http://localhost:11434";

    public Task<IChatClient> GetChatClientAsync(CancellationToken ct) => Task.FromResult<IChatClient>(Create(BaseUrl));

    public Task<IOllamaApiClient> GetApiClientAsync(CancellationToken ct) => Task.FromResult<IOllamaApiClient>(Create(BaseUrl));

    public IOllamaApiClient CreateApiClient(string baseUrl) => Create(baseUrl);

    private OllamaApiClient Create(string baseUrl) =>
        new(new HttpClient(ollama, disposeHandler: false) { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") });
}
