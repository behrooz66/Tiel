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
    [InlineData(8192, 6144)]
    [InlineData(32768, 24576)]
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
    public void The_summary_closes_the_system_message_and_replaces_the_messages_it_covers()
    {
        MessageDto[] conversation = [User("Old question"), Assistant("Old answer"), User("Recent question"), Assistant("Recent answer"), User("Latest")];

        var prompt = PromptBuilder.Build("Rules", "Be brief.", conversation, 4096, new RollingSummary(" They discussed X. ", 2));

        Assert.Equal(
            ["system: Rules\n\nBe brief.\n\nSummary of the earlier conversation:\nThey discussed X.", "user: Recent question", "assistant: Recent answer", "user: Latest"],
            prompt.Messages.Select(m => $"{m.Role}: {m.Text}"));
        Assert.Equal(0, prompt.TrimmedMessages);
    }

    [Fact]
    public void A_summary_alone_makes_a_system_message()
    {
        MessageDto[] conversation = [User("Old"), Assistant("Reply"), User("Latest")];

        var prompt = PromptBuilder.Build(null, " ", conversation, 4096, new RollingSummary("Earlier: hello.", 2));

        Assert.Equal(["system: Summary of the earlier conversation:\nEarlier: hello.", "user: Latest"], prompt.Messages.Select(m => $"{m.Role}: {m.Text}"));
    }

    [Fact]
    public void Only_unsummarized_history_counts_as_trimmed()
    {
        // Budget for 512 is 384. The summary costs 20 and each long message 292: only the newer one could fit,
        // and not with the older one, which is dropped. The summarized messages are not counted.
        var text = new string('x', 1000);
        MessageDto[] conversation = [User("a"), Assistant("b"), User("older " + text), Assistant("newer " + text), User("Latest")];

        var prompt = PromptBuilder.Build(null, null, conversation, 512, new RollingSummary("They said a and b.", 2));

        Assert.Equal(1, prompt.TrimmedMessages);
        Assert.Equal([ChatRole.System, ChatRole.Assistant, ChatRole.User], prompt.Messages.Select(m => m.Role));
    }

    [Fact]
    public void No_summary_is_planned_while_the_history_fits_in_three_quarters_of_the_budget()
    {
        // Budget for 4096 is 3072; three quarters is 2304. Four 2000-char messages cost 576 each: 2304 in all.
        var text = new string('x', 2000);
        MessageDto[] conversation = [User(text), Assistant(text), User(text), Assistant(text)];

        Assert.Null(PromptBuilder.PlanSummary(null, null, conversation, 4096, null));
    }

    [Fact]
    public void The_plan_folds_the_oldest_messages_until_the_rest_fits_in_half_the_budget()
    {
        // Budget 3072: folding starts above 2304 and stops at 1536 or less. Six messages of 576: 3456.
        // Folding three leaves 1728, still over; folding four leaves 1152.
        var text = new string('x', 2000);
        MessageDto[] conversation = [User(text), Assistant(text), User(text), Assistant(text), User(text), Assistant(text)];

        var plan = PromptBuilder.PlanSummary(null, null, conversation, 4096, null);

        Assert.NotNull(plan);
        Assert.Equal([1, 2, 3, 4], plan.Messages.Select(m => m.Sequence));
        Assert.Equal(4, plan.ThroughSequence);
        Assert.Equal(768, plan.MaxTokens);
    }

    [Fact]
    public void The_plan_never_folds_the_last_exchange()
    {
        // Two huge messages: over budget, but they are the last exchange, so there is nothing to fold.
        var text = new string('x', 10_000);

        Assert.Null(PromptBuilder.PlanSummary(null, null, [User(text), Assistant(text)], 4096, null));
    }

    [Fact]
    public void The_plan_starts_after_the_current_summary_and_covers_skipped_messages()
    {
        var text = new string('x', 2000);
        MessageDto[] conversation =
        [
            User("old"), Assistant("old reply"),
            User(text), Assistant("", MessageStatus.Error), Assistant(text), User(text), Assistant(text), User(text), Assistant(text),
        ];

        var plan = PromptBuilder.PlanSummary("Rules", null, conversation, 4096, new RollingSummary("Earlier: old.", 2));

        // The failed reply (4) is not sent to the summarizer, but the new summary covers it.
        Assert.NotNull(plan);
        Assert.Equal([3, 5, 6, 7], plan.Messages.Select(m => m.Sequence));
        Assert.Equal(7, plan.ThroughSequence);
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
