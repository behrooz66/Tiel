using Tiel.Web.Services;
using Microsoft.Extensions.Logging.Testing;

namespace Tiel.Web.Tests.Services;

public sealed class ChangeNotifierTests
{
    [Fact]
    public void A_failing_subscriber_does_not_stop_the_others()
    {
        var logger = new FakeLogger<ChangeNotifier>();
        var notifier = new ChangeNotifier(logger);
        var seen = new List<Guid>();
        notifier.ConversationsChanged += _ => throw new InvalidOperationException("broken component");
        notifier.ConversationsChanged += seen.Add;
        var projectId = Guid.CreateVersion7();

        notifier.NotifyConversationsChanged(projectId);

        Assert.Equal([projectId], seen);
        Assert.IsType<InvalidOperationException>(logger.LatestRecord.Exception);
    }
}
