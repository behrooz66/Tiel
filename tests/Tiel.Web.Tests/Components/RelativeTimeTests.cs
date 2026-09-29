using Tiel.Web.Components.Shared;

namespace Tiel.Web.Tests.Components;

public sealed class RelativeTimeTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, "now")]
    [InlineData(59, "now")]
    [InlineData(60, "1m")]
    [InlineData(59 * 60, "59m")]
    [InlineData(3 * 3600, "3h")]
    [InlineData(2 * 86400, "2d")]
    public void Formats_recent_times_compactly(int secondsAgo, string expected)
    {
        Assert.Equal(expected, RelativeTime.Format(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void Older_times_become_dates()
    {
        Assert.DoesNotContain("d", RelativeTime.Format(Now.AddDays(-30), Now).Replace("Aug", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("2025", RelativeTime.Format(Now.AddYears(-1), Now), StringComparison.Ordinal);
    }
}
