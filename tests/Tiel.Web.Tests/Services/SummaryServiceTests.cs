using Tiel.Web.Data.Entities;
using Tiel.Web.Services;

namespace Tiel.Web.Tests.Services;

public sealed class SummaryServiceTests
{
    [Theory]
    [InlineData("They planned a trip.", "They planned a trip.")]
    [InlineData("Summary: They planned a trip.", "They planned a trip.")]
    [InlineData("<think>\nLet me see.\n</think>\n\nThey planned a trip.", "They planned a trip.")]
    [InlineData("  \n ", "")]
    public void Cleans_the_model_output(string raw, string expected)
    {
        Assert.Equal(expected, SummaryService.Clean(raw));
    }

    [Fact]
    public void The_instruction_carries_the_summary_so_far_the_transcript_and_a_word_limit()
    {
        MessageDto[] messages = [Message(3, MessageRole.User, " Where to? "), Message(4, MessageRole.Assistant, "Lisbon.")];

        var instruction = SummaryService.Instruction("They planned a trip.", messages, 768);

        Assert.StartsWith("Summary so far:\nThey planned a trip.\n\nNew messages:\nUser: Where to?\n\nAssistant: Lisbon.\n\n", instruction, StringComparison.Ordinal);
        Assert.Contains("covers the summary so far and the new messages, in at most 384 words.", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_summary_so_far_the_instruction_covers_only_the_messages()
    {
        var instruction = SummaryService.Instruction(null, [Message(1, MessageRole.User, "Hi")], 40);

        Assert.StartsWith("New messages:\nUser: Hi\n\n", instruction, StringComparison.Ordinal);
        Assert.Contains("covers these messages, in at most 50 words.", instruction, StringComparison.Ordinal);
    }

    private static MessageDto Message(int sequence, MessageRole role, string content) =>
        new(Guid.CreateVersion7(), sequence, role, content, MessageStatus.Complete, null, null, null, DateTime.UtcNow);
}
