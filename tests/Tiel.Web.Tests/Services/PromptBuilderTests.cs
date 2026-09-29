using Tiel.Web.Data.Entities;
using Tiel.Web.Services;
using Microsoft.Extensions.AI;

namespace Tiel.Web.Tests.Services;

public sealed class PromptBuilderTests
{
    private int _sequence;

    [Theory]
    [InlineData("Answer in French.", "Be brief.", "Answer in French.\n\nBe brief.")]
    [InlineData("  Answer in French.  ", null, "Answer in French.")]
    [InlineData(null, "Be brief.", "Be brief.")]
    [InlineData("", "  Be brief. ", "Be brief.")]
    public void Instructions_and_system_prompt_merge_into_one_system_message(string? instructions, string? systemPrompt, string expected)
    {
        var prompt = PromptBuilder.Build(instructions, systemPrompt, [User("Hi")], 4096);

        Assert.Equal([ChatRole.System, ChatRole.User], prompt.Messages.Select(m => m.Role));
        Assert.Equal(expected, prompt.Messages[0].Text);
    }

    [Fact]
    public void Without_instructions_or_system_prompt_there_is_no_system_message()
    {
        var prompt = PromptBuilder.Build(" ", null, [User("Hi")], 4096);

        Assert.Equal(ChatRole.User, Assert.Single(prompt.Messages).Role);
    }

    [Fact]
    public void Replays_complete_and_non_empty_cancelled_messages_and_skips_failed_and_empty_ones()
    {
        MessageDto[] conversation =
        [
            User("First question"),
            Assistant("First answer"),
            User("Second question"),
            Assistant("", MessageStatus.Error),
            Assistant("Partial ans", MessageStatus.Cancelled),
            Assistant("", MessageStatus.Cancelled),
            Assistant("   ", MessageStatus.Complete),
            User("Third question"),
            Assistant("", MessageStatus.Streaming),
        ];

        var prompt = PromptBuilder.Build(null, null, conversation, 4096);

        Assert.Equal(
            ["user: First question", "assistant: First answer", "user: Second question", "assistant: Partial ans", "user: Third question"],
            prompt.Messages.Select(m => $"{m.Role}: {m.Text}"));
        Assert.Equal(0, prompt.TrimmedMessages);
    }

    [Fact]
    public void The_latest_user_message_is_always_last()
    {
        var prompt = PromptBuilder.Build("Rules", null, [User("Old"), Assistant("Reply"), User("Latest"), Assistant("", MessageStatus.Streaming)], 4096);

        Assert.Equal("Latest", prompt.Messages[^1].Text);
        Assert.Equal(ChatRole.User, prompt.Messages[^1].Role);
    }

    [Fact]
    public void Trimming_drops_the_oldest_history_first_and_reports_the_count()
    {
        // Budget for 2048 is 2048 - 512 = 1536. System (700 chars) and latest cost 204 + 7; each long message
        // about 578. All of it is 1953 tokens; without the oldest message it is 1375, which fits.
        var text = new string('x', 2000);
        MessageDto[] conversation = [User("oldest " + text), Assistant("older " + text), User("recent " + text), Assistant("latest reply"), User("question")];

        var prompt = PromptBuilder.Build(new string('s', 700), null, conversation, 2048);

        Assert.Equal(1536, prompt.Budget);
        Assert.Equal(1, prompt.TrimmedMessages);
        Assert.Equal(
            [ChatRole.System, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant, ChatRole.User],
            prompt.Messages.Select(m => m.Role));
        Assert.StartsWith("older", prompt.Messages[1].Text, StringComparison.Ordinal);
        Assert.True(prompt.EstimatedTokens <= prompt.Budget);
        Assert.False(prompt.ExceedsBudget);
    }

    [Fact]
    public void When_the_kept_messages_alone_exceed_the_budget_all_history_goes_and_they_are_sent_anyway()
    {
        MessageDto[] conversation = [User("hello"), Assistant("hi"), User(new string('q', 4000))];

        var prompt = PromptBuilder.Build("Rules", null, conversation, 1024);

        Assert.Equal(2, prompt.TrimmedMessages);
        Assert.Equal([ChatRole.System, ChatRole.User], prompt.Messages.Select(m => m.Role));
        Assert.True(prompt.ExceedsBudget);
    }

    [Theory]
    [InlineData(4096, 3072)]
    [InlineData(8192, 7168)]
    [InlineData(2048, 1536)]
    [InlineData(1000, 750)]
    public void The_budget_reserves_room_for_the_answer(int contextLength, int budget)
    {
        Assert.Equal(budget, PromptBuilder.Budget(contextLength));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(7, 6)]
    [InlineData(8, 7)]
    [InlineData(3500, 1004)]
    public void Estimates_ceil_chars_over_three_and_a_half_plus_four(int chars, int tokens)
    {
        Assert.Equal(tokens, TokenEstimator.Estimate(new string('a', chars)));
    }

    [Fact]
    public void A_conversation_without_a_user_message_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PromptBuilder.Build(null, null, [Assistant("orphan")], 4096));
    }

    private MessageDto User(string content) => Message(MessageRole.User, content, MessageStatus.Complete);

    private MessageDto Assistant(string content, MessageStatus status = MessageStatus.Complete) =>
        Message(MessageRole.Assistant, content, status);

    private MessageDto Message(MessageRole role, string content, MessageStatus status) =>
        new(Guid.CreateVersion7(), ++_sequence, role, content, status, null, null, null, DateTime.UtcNow);
}
