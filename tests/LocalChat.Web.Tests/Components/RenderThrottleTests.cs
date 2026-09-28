using LocalChat.Web.Components.Chat;
using Microsoft.Extensions.Time.Testing;

namespace LocalChat.Web.Tests.Components;

public sealed class RenderThrottleTests
{
    [Fact]
    public async Task Renders_at_once_then_at_most_once_per_interval()
    {
        var time = new FakeTimeProvider();
        var renders = 0;
        var throttle = new RenderThrottle(time, () => { Interlocked.Increment(ref renders); return Task.CompletedTask; });

        throttle.Request();
        Assert.Equal(1, renders);

        for (var i = 0; i < 10; i++)
        {
            throttle.Request();
        }

        await Task.Yield();
        Assert.Equal(1, renders);

        time.Advance(TimeSpan.FromMilliseconds(50));
        await WaitUntilAsync(() => Volatile.Read(ref renders) == 2);
        Assert.Equal(2, renders);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
