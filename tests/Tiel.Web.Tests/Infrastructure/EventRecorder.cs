using System.Collections.Concurrent;
using Tiel.Web.Services;

namespace Tiel.Web.Tests.Infrastructure;

/// <summary>Records a subscription's events and lets a test wait for one.</summary>
public sealed class EventRecorder
{
    private readonly ConcurrentQueue<GenerationEvent> _events = new();
    private readonly List<(Func<GenerationEvent, bool> Match, TaskCompletionSource Seen)> _waiters = [];

    public IReadOnlyList<GenerationEvent> Events => [.. _events];

    public Task Record(GenerationEvent generationEvent)
    {
        _events.Enqueue(generationEvent);
        lock (_waiters)
        {
            foreach (var waiter in _waiters.Where(w => w.Match(generationEvent)).ToList())
            {
                waiter.Seen.TrySetResult();
                _waiters.Remove(waiter);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Completes when an event matching <paramref name="match"/> has arrived (or already had).</summary>
    public Task WaitForAsync<T>(Func<T, bool>? match = null) where T : GenerationEvent
    {
        bool Matches(GenerationEvent e) => e is T typed && (match?.Invoke(typed) ?? true);
        lock (_waiters)
        {
            if (_events.Any(Matches))
            {
                return Task.CompletedTask;
            }

            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((Matches, seen));
            return seen.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>The concatenated text of all deltas so far.</summary>
    public string DeltaText => string.Concat(Events.OfType<GenerationDelta>().Select(d => d.Text));
}
