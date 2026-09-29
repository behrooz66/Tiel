using Bunit;
using Tiel.Web.Components.Pages;
using Tiel.Web.Data;
using Tiel.Web.Services;
using Tiel.Web.Tests.Infrastructure;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Tiel.Web.Tests.Components;

public sealed class ChatPageTests : AppTestContext
{
    [Fact]
    public async Task Sending_streams_the_reply_then_shows_the_generated_title()
    {
        var conversation = await App.Conversations.CreateAsync(ProjectIds.General, null, TestContext.Current.CancellationToken);
        var cut = Render<Chat>(p => p.Add(x => x.ConversationId, conversation.Id));
        cut.WaitForAssertion(() => Assert.Equal("New chat", cut.Find(".chat-title h1").TextContent));

        cut.Find("textarea.composer-input").Input("Hi there");
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>().Single(b => b.Instance.Title == "Send (Enter)").Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => Assert.Equal("Greeting the assistant", cut.Find(".chat-title h1").TextContent), TimeSpan.FromSeconds(5));
        Assert.Equal("Hi there", cut.Find(".from-user .user-bubble").TextContent);
        Assert.Equal("Hello there!", cut.Find(".from-assistant.status-complete .markdown").TextContent.Trim());
        Assert.Equal("", cut.Find("textarea.composer-input").GetAttribute("value") ?? "");
        cut.WaitForAssertion(() => Assert.Equal(["Send (Enter)"], ComposerButtons(cut)));
    }

    [Fact]
    public async Task After_each_reply_the_composer_offers_Send_again()
    {
        var conversation = await App.Conversations.CreateAsync(ProjectIds.General, null, TestContext.Current.CancellationToken);
        var cut = Render<Chat>(p => p.Add(x => x.ConversationId, conversation.Id));
        cut.WaitForAssertion(() => Assert.Equal("New chat", cut.Find(".chat-title h1").TextContent));

        for (var turn = 1; turn <= 3; turn++)
        {
            cut.Find("textarea.composer-input").Input($"Message {turn}");
            await cut.InvokeAsync(() => cut.FindComponents<FluentButton>().Single(b => b.Instance.Title == "Send (Enter)").Instance.OnClick.InvokeAsync());
            cut.WaitForAssertion(() =>
            {
                Assert.Equal(turn * 2, cut.FindAll(".message.status-complete").Count);
                Assert.Equal(["Send (Enter)"], ComposerButtons(cut));
            }, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Opening_a_chat_mid_reply_shows_the_text_so_far_and_follows_it_live()
    {
        var ct = TestContext.Current.CancellationToken;
        var live = App.Chat.GoLive();
        var conversation = await App.Conversations.CreateAsync(ProjectIds.General, null, ct);
        await App.Generation.SendAsync(conversation.Id, "Tell me a story", null, ct);
        await live.Writer.WriteAsync("Once upon", ct);
        await WaitForDeltaAsync(conversation.Id, "Once upon");

        var cut = Render<Chat>(p => p.Add(x => x.ConversationId, conversation.Id));

        cut.WaitForAssertion(() => Assert.Equal("Once upon", cut.Find(".status-streaming .markdown").TextContent.Trim()));
        Assert.NotNull(cut.FindComponents<FluentButton>().SingleOrDefault(b => b.Instance.Title == "Stop generating"));

        await live.Writer.WriteAsync(" a time", ct);
        cut.WaitForAssertion(() => Assert.Equal("Once upon a time", cut.Find(".status-streaming .markdown").TextContent.Trim()));

        live.Writer.Complete();
        cut.WaitForAssertion(() => Assert.Equal("Once upon a time", cut.Find(".status-complete .markdown").TextContent.Trim()));
    }

    [Fact]
    public async Task The_trimmed_note_counts_the_dropped_messages()
    {
        Assert.Equal("1 earlier message is outside this model's context window.", Chat.TrimmedNote(1));
        Assert.Equal("3 earlier messages are outside this model's context window.", Chat.TrimmedNote(3));
        await Task.CompletedTask;
    }

    private static List<string?> ComposerButtons(IRenderedComponent<Chat> cut) =>
        cut.FindComponents<FluentButton>()
            .Select(b => b.Instance.Title)
            .Where(t => t is "Send (Enter)" or "Stop generating")
            .ToList();

    private async Task WaitForDeltaAsync(Guid conversationId, string text)
    {
        var recorder = new EventRecorder();
        using var subscription = App.Generation.Subscribe(conversationId, recorder.Record);
        for (var i = 0; i < 100 && subscription.Snapshot?.Text != text && recorder.DeltaText.Length == 0; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
