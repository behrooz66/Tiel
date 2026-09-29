namespace Tiel.Web.Services;

/// <summary>A live event of one reply being generated. See <see cref="IGenerationService.Subscribe"/>.</summary>
public abstract record GenerationEvent;

/// <summary>Once, first. <paramref name="UserMessage"/> is null for a retry.</summary>
public sealed record GenerationStarted(MessageDto? UserMessage, MessageDto AssistantMessage, int TrimmedMessages) : GenerationEvent;

/// <summary>A chunk of generated text.</summary>
public sealed record GenerationDelta(string Text) : GenerationEvent;

/// <summary>The reply ended normally (<c>Complete</c>) or was stopped (<c>Cancelled</c>).</summary>
public sealed record GenerationCompleted(MessageDto Message) : GenerationEvent;

/// <summary>After completion, only when a title was generated.</summary>
public sealed record TitleGenerated(string Title) : GenerationEvent;

/// <summary>The reply failed (<c>Error</c>). Final event.</summary>
public sealed record GenerationFailed(MessageDto Message) : GenerationEvent;

/// <summary>The reply so far, when a subscriber joins a running generation.</summary>
public sealed record GenerationSnapshot(Guid AssistantMessageId, string Text, int TrimmedMessages);

/// <summary>
/// One subscriber's view of a running generation: the snapshot at the moment it joined, then every
/// event after it. Dispose it to stop receiving events; that never stops the generation.
/// </summary>
public sealed class GenerationSubscription : IDisposable
{
    private Action? _unsubscribe;

    internal GenerationSubscription(GenerationSnapshot? snapshot, Action? unsubscribe)
    {
        Snapshot = snapshot;
        _unsubscribe = unsubscribe;
    }

    /// <summary>Null when nothing was running, in which case no events follow.</summary>
    public GenerationSnapshot? Snapshot { get; }

    internal static GenerationSubscription None { get; } = new(null, null);

    public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
}
