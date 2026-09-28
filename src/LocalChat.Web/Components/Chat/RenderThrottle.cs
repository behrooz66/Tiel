namespace LocalChat.Web.Components.Chat;

/// <summary>
/// Coalesces render requests so a streaming reply re-renders at most once per interval (20 times a second by default).
/// <see cref="Request"/> may be called from any thread; <paramref name="render"/> should marshal with <c>InvokeAsync</c>.
/// </summary>
public sealed class RenderThrottle(TimeProvider time, Func<Task> render, TimeSpan? interval = null)
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    private readonly TimeSpan _interval = interval ?? DefaultInterval;
    private long _lastRender = long.MinValue;
    private int _scheduled;

    public void Request()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 1)
        {
            return;
        }

        var last = Interlocked.Read(ref _lastRender);
        var wait = last == long.MinValue ? TimeSpan.Zero : _interval - time.GetElapsedTime(last);
        _ = RenderAfterAsync(wait);
    }

    private async Task RenderAfterAsync(TimeSpan wait)
    {
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, time);
        }

        Interlocked.Exchange(ref _lastRender, time.GetTimestamp());
        Volatile.Write(ref _scheduled, 0);
        await render();
    }
}
