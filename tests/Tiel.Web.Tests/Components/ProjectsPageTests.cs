using Bunit;
using Tiel.Web.Components.Pages;
using Tiel.Web.Data;
using Tiel.Web.Services;
using Tiel.Web.Tests.Infrastructure;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Tiel.Web.Tests.Components;

public sealed class ProjectsPageTests : AppTestContext
{
    [Fact]
    public async Task General_has_a_Default_badge_and_no_Delete_action()
    {
        await App.Projects.CreateAsync(new ProjectInput("Work", "Day job", null), TestContext.Current.CancellationToken);

        var cut = Render<Projects>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".project-row").Count));
        var general = cut.FindAll(".project-row")[0];
        var work = cut.FindAll(".project-row")[1];
        Assert.Contains("Default", general.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Delete", general.TextContent, StringComparison.Ordinal);
        Assert.Contains("Delete", work.TextContent, StringComparison.Ordinal);
        Assert.Contains("Day job", work.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_asks_for_confirmation_stating_how_many_conversations_go()
    {
        var ct = TestContext.Current.CancellationToken;
        var work = await App.Projects.CreateAsync(new ProjectInput("Work", null, null), ct);
        await App.Conversations.CreateAsync(work.Id, null, ct);
        await App.Conversations.CreateAsync(work.Id, null, ct);
        var provider = Render<FluentDialogProvider>();
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".project-row").Count));

        var delete = cut.FindComponents<FluentButton>().Single(b => b.Markup.Contains("Delete", StringComparison.Ordinal));
        _ = cut.InvokeAsync(() => delete.Instance.OnClick.InvokeAsync());

        provider.WaitForAssertion(() => Assert.Contains(
            "This permanently deletes the project and its 2 conversations.", provider.Markup, StringComparison.Ordinal));
        Assert.Equal("This permanently deletes the project and its 1 conversation.", Projects.DeleteMessage(1));
    }

    [Fact]
    public async Task The_list_refreshes_when_projects_change_elsewhere()
    {
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".project-row")));

        await App.Projects.CreateAsync(new ProjectInput("Home", null, null), TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".project-row").Count));
    }
}
