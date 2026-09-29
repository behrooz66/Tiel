using Bunit;
using Tiel.Web.Components.Chat;
using Tiel.Web.Data.Entities;
using Tiel.Web.Services;
using Tiel.Web.Tests.Infrastructure;

namespace Tiel.Web.Tests.Components;

public sealed class MessageViewTests : AppTestContext
{
    [Fact]
    public void A_user_message_is_a_plain_text_bubble()
    {
        var cut = Render(Message(MessageRole.User, MessageStatus.Complete, "Hi **there** <b>bold</b>"));

        Assert.Equal("Hi **there** <b>bold</b>", cut.Find(".from-user .user-bubble").TextContent);
        Assert.Empty(cut.FindAll(".markdown"));
    }

    [Fact]
    public void A_streaming_reply_shows_the_text_so_far_and_a_typing_indicator_but_no_footer()
    {
        var cut = Render(Message(MessageRole.Assistant, MessageStatus.Streaming, ""), content: "Partial **answer**", isLast: true);

        Assert.Equal("answer", cut.Find(".markdown strong").TextContent);
        Assert.NotNull(cut.Find(".typing[role='status']"));
        Assert.Empty(cut.FindAll(".message-footer"));
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "highlightCodeBlocks");
    }

    [Fact]
    public void A_complete_reply_renders_markdown_safely_names_its_model_and_gets_highlighted()
    {
        var cut = Render(
            Message(MessageRole.Assistant, MessageStatus.Complete, "See [docs](https://example.com)\n\n```csharp\nvar x = 1;\n```\n\n<script>alert(1)</script>"),
            isLast: true, modelName: "phi4-mini:latest");

        var link = cut.Find(".markdown a");
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Equal("language-csharp", cut.Find(".markdown pre code").ClassName);
        Assert.Empty(cut.FindAll(".markdown script"));
        Assert.Contains("&lt;script&gt;", cut.Find(".markdown").InnerHtml, StringComparison.Ordinal);
        Assert.Equal("phi4-mini:latest", cut.Find(".model-name").TextContent);
        Assert.DoesNotContain("Retry", cut.Markup, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations, i => i.Identifier == "highlightCodeBlocks"));
    }

    [Fact]
    public void A_stopped_reply_keeps_its_text_with_a_Stopped_label_and_Retry_when_last()
    {
        var last = Render(Message(MessageRole.Assistant, MessageStatus.Cancelled, "Half an ans"), isLast: true);
        var earlier = Render(Message(MessageRole.Assistant, MessageStatus.Cancelled, "Half an ans"), isLast: false);

        Assert.Equal("Half an ans", last.Find(".markdown").TextContent.Trim());
        Assert.Contains("Stopped", last.Find(".message-footer").TextContent, StringComparison.Ordinal);
        Assert.Contains("Retry", last.Find(".message-footer").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Retry", earlier.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_reply_shows_its_error_in_a_red_bubble_with_Retry_when_last()
    {
        var retried = 0;
        var message = Message(MessageRole.Assistant, MessageStatus.Error, "partial") with
        {
            ErrorMessage = "Ollama is unreachable: Connection refused (localhost:11434)",
        };
        var cut = Render<MessageView>(p => p
            .Add(x => x.Message, message)
            .Add(x => x.IsLast, true)
            .Add(x => x.OnRetry, () => retried++));

        Assert.Contains("Ollama is unreachable: Connection refused (localhost:11434)", cut.Find(".error-bubble[role='alert']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".markdown"));
        await cut.InvokeAsync(() => cut.FindComponent<Microsoft.FluentUI.AspNetCore.Components.FluentButton>().Instance.OnClick.InvokeAsync());
        Assert.Equal(1, retried);
    }

    private IRenderedComponent<MessageView> Render(MessageDto message, string? content = null, bool isLast = false, string? modelName = null) =>
        Render<MessageView>(p => p
            .Add(x => x.Message, message)
            .Add(x => x.Content, content)
            .Add(x => x.IsLast, isLast)
            .Add(x => x.ModelName, modelName));

    private static MessageDto Message(MessageRole role, MessageStatus status, string content) =>
        new(Guid.CreateVersion7(), 1, role, content, status, null, role == MessageRole.Assistant ? Guid.CreateVersion7() : null, null, TestData.Now);
}
