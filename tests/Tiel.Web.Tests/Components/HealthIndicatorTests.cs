using Bunit;
using Tiel.Web.Components.Layout;
using Tiel.Web.Services;
using Tiel.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tiel.Web.Tests.Components;

public sealed class HealthIndicatorTests : BunitContext
{
    private readonly FakeHealthService _health = new();
    private readonly FakeSettingsService _settings = new();
    private readonly FakeTimeProvider _time = new();

    public HealthIndicatorTests()
    {
        Services.AddSingleton<IHealthService>(_health);
        Services.AddSingleton<ISettingsService>(_settings);
        Services.AddSingleton<TimeProvider>(_time);
    }

    [Fact]
    public void Turns_red_within_30_seconds_of_ollama_stopping_and_green_after_it_starts()
    {
        var cut = Render<HealthIndicator>();
        cut.WaitForAssertion(() => Assert.Contains("up", cut.Find(".health").ClassList));

        _health.Status = FakeHealthService.Unreachable;
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.Contains("up", cut.Find(".health").ClassList);
        _time.Advance(TimeSpan.FromSeconds(1));
        cut.WaitForAssertion(() => Assert.Contains("down", cut.Find(".health").ClassList));
        Assert.Equal("Ollama is unreachable at http://localhost:11434", cut.Find(".health").GetAttribute("aria-label"));

        _health.Status = FakeHealthService.Reachable;
        _time.Advance(TimeSpan.FromSeconds(30));
        cut.WaitForAssertion(() => Assert.Contains("up", cut.Find(".health").ClassList));
    }

    [Fact]
    public void Rechecks_at_once_when_the_settings_change()
    {
        var cut = Render<HealthIndicator>();
        cut.WaitForAssertion(() => Assert.Equal(1, _health.Checks));

        _health.Status = FakeHealthService.Unreachable;
        _settings.RaiseChanged();

        cut.WaitForAssertion(() => Assert.Contains("down", cut.Find(".health").ClassList));
        Assert.Equal(2, _health.Checks);
    }
}
