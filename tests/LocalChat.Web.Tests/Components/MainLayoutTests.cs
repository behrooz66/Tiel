using Bunit;
using LocalChat.Web.Components.Layout;
using LocalChat.Web.Services;
using LocalChat.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LocalChat.Web.Tests.Components;

public sealed class MainLayoutTests : BunitContext
{
    public MainLayoutTests()
    {
        Services.AddFluentUIComponents();
        Services.AddSingleton<IHealthService>(new FakeHealthService());
        Services.AddSingleton<ISettingsService>(new FakeSettingsService());
        Services.AddSingleton(TimeProvider.System);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Renders_the_body_inside_main_next_to_the_sidebar()
    {
        var cut = Render<MainLayout>(parameters => parameters
            .Add(p => p.Body, "<p id=\"page\">Page content</p>"));

        Assert.Equal("Page content", cut.Find("main #page").TextContent);
        Assert.NotNull(cut.Find("aside.sidebar a[href='settings']"));
    }

    [Fact]
    public void The_menu_button_opens_the_drawer_and_the_scrim_closes_it()
    {
        var cut = Render<MainLayout>(parameters => parameters.Add(p => p.Body, "<p>Page</p>"));

        cut.Find("button[aria-label='Open the sidebar']").Click();
        Assert.Contains("drawer-open", cut.Find(".shell").ClassList);

        cut.Find(".scrim").Click();
        Assert.DoesNotContain("drawer-open", cut.Find(".shell").ClassList);
    }
}
