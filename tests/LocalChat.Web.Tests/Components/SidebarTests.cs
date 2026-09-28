using Bunit;
using LocalChat.Web.Components.Layout;
using LocalChat.Web.Data;
using LocalChat.Web.Services;
using LocalChat.Web.Tests.Infrastructure;

namespace LocalChat.Web.Tests.Components;

public sealed class SidebarTests : AppTestContext
{
    [Fact]
    public async Task Lists_the_current_projects_chats_newest_first_and_updates_live()
    {
        var ct = TestContext.Current.CancellationToken;
        var older = await App.Conversations.CreateAsync(ProjectIds.General, null, ct);
        await App.Conversations.RenameAsync(older.Id, "Older chat", ct);
        var cut = Render<Sidebar>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".conversation")));

        App.Time.Advance(TimeSpan.FromMinutes(5));
        var newer = await App.Conversations.CreateAsync(ProjectIds.General, null, ct);
        await App.Conversations.RenameAsync(newer.Id, "Newer chat", ct);

        cut.WaitForAssertion(() => Assert.Equal(
            ["Newer chat", "Older chat"],
            cut.FindAll(".conversation-title").Select(e => e.TextContent)));
        Assert.Equal(["now", "5m"], cut.FindAll(".conversation-time").Select(e => e.TextContent));

        await App.Conversations.DeleteAsync(newer.Id, ct);
        cut.WaitForAssertion(() => Assert.Equal(["Older chat"], cut.FindAll(".conversation-title").Select(e => e.TextContent)));
    }

    [Fact]
    public async Task Shows_new_projects_in_the_switcher_without_a_reload()
    {
        var cut = Render<Sidebar>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindComponent<ProjectSwitcher>().Instance.Projects));

        await App.Projects.CreateAsync(new ProjectInput("Travel", null, null), TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => Assert.Equal(
            ["General", "Travel"], cut.FindComponent<ProjectSwitcher>().Instance.Projects.Select(p => p.Name)));
    }

    [Fact]
    public async Task Rename_from_the_menu_edits_the_title_inline()
    {
        var ct = TestContext.Current.CancellationToken;
        var conversation = await App.Conversations.CreateAsync(ProjectIds.General, null, ct);
        var cut = Render<Sidebar>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".conversation")));

        cut.Find($"#chat-menu-{conversation.Id}").Click();
        await cut.InvokeAsync(() => cut.FindComponents<Microsoft.FluentUI.AspNetCore.Components.FluentMenuItem>()[0].Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => cut.Find("input.inline-rename"));
        cut.Find("input.inline-rename").Input("Trip ideas");
        cut.Find("input.inline-rename").KeyDown("Enter");

        cut.WaitForAssertion(() => Assert.Equal("Trip ideas", cut.Find(".conversation-title").TextContent));
        Assert.Equal("Trip ideas", (await App.Conversations.GetAsync(conversation.Id, ct)).Title);
    }
}
