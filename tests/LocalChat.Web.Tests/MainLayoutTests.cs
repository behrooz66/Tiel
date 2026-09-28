using Bunit;
using LocalChat.Web.Components.Layout;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LocalChat.Web.Tests;

public sealed class MainLayoutTests : BunitContext
{
    public MainLayoutTests()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Renders_the_body_inside_main()
    {
        var cut = Render<MainLayout>(parameters => parameters
            .Add(p => p.Body, "<p id=\"page\">Page content</p>"));

        Assert.Equal("Page content", cut.Find("main > #page").TextContent);
    }
}
