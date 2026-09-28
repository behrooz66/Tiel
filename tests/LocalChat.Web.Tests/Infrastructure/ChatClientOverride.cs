using LocalChat.Web.Services;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace LocalChat.Web.Tests.Infrastructure;

/// <summary>Serves <paramref name="chat"/> for chat calls and delegates Ollama-specific calls to <paramref name="inner"/>.</summary>
public sealed class ChatClientOverride(IOllamaClientProvider inner, IChatClient chat) : IOllamaClientProvider
{
    public Task<IChatClient> GetChatClientAsync(CancellationToken ct) => Task.FromResult(chat);

    public Task<IOllamaApiClient> GetApiClientAsync(CancellationToken ct) => inner.GetApiClientAsync(ct);

    public IOllamaApiClient CreateApiClient(string baseUrl) => inner.CreateApiClient(baseUrl);
}
