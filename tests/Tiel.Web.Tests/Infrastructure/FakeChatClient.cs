using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace Tiel.Web.Tests.Infrastructure;

/// <summary>
/// An <see cref="IChatClient"/> that streams canned chunks. By default it streams <see cref="Chunks"/> at once;
/// with <see cref="Live"/> set it streams whatever the test writes, until the test completes the channel.
/// It can be told to fail (<see cref="FailWith"/>) or hang until cancelled (<see cref="Hang"/>) after the chunks.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    public List<string> Chunks { get; } = ["Hello", " there", "!"];
    public Channel<string>? Live { get; private set; }
    public Exception? FailWith { get; set; }
    public bool Hang { get; set; }
    public long? OutputTokenCount { get; set; } = 3;

    /// <summary>End the stream quietly when cancelled, as OllamaSharp does, instead of throwing.</summary>
    public bool EndQuietlyOnCancel { get; set; }

    /// <summary>The reply to non-streaming calls (title generation).</summary>
    public string TitleReply { get; set; } = "Title: \"Greeting the assistant.\"";
    public Exception? TitleFailWith { get; set; }

    public ConcurrentQueue<(List<ChatMessage> Messages, ChatOptions? Options)> StreamingRequests { get; } = new();
    public ConcurrentQueue<(List<ChatMessage> Messages, ChatOptions? Options)> TitleRequests { get; } = new();

    /// <summary>Switches to live mode: every streaming call reads its chunks from the returned channel.</summary>
    public Channel<string> GoLive() => Live = Channel.CreateUnbounded<string>();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamingRequests.Enqueue((messages.ToList(), options));
        if (Live is { } live)
        {
            while (true)
            {
                string chunk;
                try
                {
                    if (!await live.Reader.WaitToReadAsync(cancellationToken) || !live.Reader.TryRead(out chunk!))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (EndQuietlyOnCancel)
                {
                    yield break;
                }

                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
        }
        else
        {
            foreach (var chunk in Chunks)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
        }

        if (FailWith is not null)
        {
            throw FailWith;
        }

        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (OutputTokenCount is { } tokens)
        {
            yield return new ChatResponseUpdate { Contents = [new UsageContent(new UsageDetails { OutputTokenCount = tokens })] };
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        TitleRequests.Enqueue((messages.ToList(), options));
        return TitleFailWith is not null
            ? Task.FromException<ChatResponse>(TitleFailWith)
            : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, TitleReply)));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
