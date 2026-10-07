using Tiel.Web.Data.Entities;
using Microsoft.Extensions.AI;

namespace Tiel.Web.Services;

/// <param name="Messages">What to send: an optional system message, the kept history, then the latest user message.</param>
/// <param name="TrimmedMessages">History messages dropped to fit the context window; summarized messages are not counted.</param>
/// <param name="EstimatedTokens">The estimate for <paramref name="Messages"/>.</param>
/// <param name="Budget">The tokens available for the prompt, after reserving room for the answer.</param>
public sealed record Prompt(IReadOnlyList<ChatMessage> Messages, int TrimmedMessages, int EstimatedTokens, int Budget)
{
    /// <summary>True when even the system message and the latest user message alone do not fit.</summary>
    public bool ExceedsBudget => EstimatedTokens > Budget;
}

/// <summary>A conversation's rolling summary: <paramref name="Text"/> stands in for every message up to <paramref name="ThroughSequence"/>.</summary>
public sealed record RollingSummary(string Text, int ThroughSequence);

/// <param name="Messages">The replayable messages to fold into the summary, oldest first.</param>
/// <param name="ThroughSequence">The new summary covers every message up to this sequence, including skipped ones.</param>
/// <param name="MaxTokens">The most the new summary may cost.</param>
public sealed record SummaryPlan(IReadOnlyList<MessageDto> Messages, int ThroughSequence, int MaxTokens);

/// <summary>Assembles the request for one turn from the stored conversation, and plans its rolling summary. Pure: no I/O, no clock.</summary>
public static class PromptBuilder
{
    public const string SummaryHeading = "Summary of the earlier conversation:";

    /// <summary>The latest messages that are never folded into the summary, so the last exchange is always sent word for word.</summary>
    public const int KeepRecent = 2;

    /// <summary>
    /// The tokens available for the prompt: the context length minus a quarter of it for the answer.
    /// The reserve grows with the window, so a thinking model still has room to reason in a long chat.
    /// </summary>
    public static int Budget(int contextLength) => contextLength - contextLength / 4;

    /// <param name="messages">The conversation in sequence order. Its last user message is the one being answered; anything after it is ignored.</param>
    /// <param name="summary">Sent in the system message instead of the messages it covers.</param>
    public static Prompt Build(
        string? projectInstructions,
        string? systemPrompt,
        IReadOnlyList<MessageDto> messages,
        int contextLength,
        RollingSummary? summary = null)
    {
        var latestIndex = LastUserMessageIndex(messages);
        var latest = messages[latestIndex].Content;
        var history = Unsummarized(messages.Take(latestIndex), summary).Where(IsReplayable).ToList();
        var system = SystemText(projectInstructions, systemPrompt, summary);

        // Drop the oldest history until the estimate fits. The system and latest user messages always stay.
        var budget = Budget(contextLength);
        var keptCost = (system is null ? 0 : TokenEstimator.Estimate(system)) + TokenEstimator.Estimate(latest);
        var historyCost = history.Sum(m => TokenEstimator.Estimate(m.Content));
        var dropped = 0;
        while (dropped < history.Count && keptCost + historyCost > budget)
        {
            historyCost -= TokenEstimator.Estimate(history[dropped].Content);
            dropped++;
        }

        var result = new List<ChatMessage>();
        if (system is not null)
        {
            result.Add(new ChatMessage(ChatRole.System, system));
        }

        result.AddRange(history.Skip(dropped).Select(m =>
            new ChatMessage(m.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)));
        result.Add(new ChatMessage(ChatRole.User, latest));
        return new Prompt(result, dropped, keptCost + historyCost, budget);
    }

    /// <summary>
    /// After a reply: which messages to fold into the summary, or null when the next prompt still fits comfortably.
    /// Folding starts once the system message and unsummarized history pass three quarters of the budget, and takes
    /// the oldest messages until the rest fits in half of it. The summary itself may use a quarter.
    /// </summary>
    /// <param name="messages">The whole conversation in sequence order, ending with the completed reply.</param>
    public static SummaryPlan? PlanSummary(
        string? projectInstructions,
        string? systemPrompt,
        IReadOnlyList<MessageDto> messages,
        int contextLength,
        RollingSummary? summary)
    {
        var budget = Budget(contextLength);
        var system = SystemText(projectInstructions, systemPrompt, summary);
        var candidates = Unsummarized(messages, summary).ToList();
        var historyCost = candidates.Where(IsReplayable).Sum(m => TokenEstimator.Estimate(m.Content));
        var systemCost = system is null ? 0 : TokenEstimator.Estimate(system);
        if (systemCost + historyCost <= budget * 3 / 4)
        {
            return null;
        }

        var folded = new List<MessageDto>();
        var through = 0;
        for (var i = 0; i < candidates.Count - KeepRecent && historyCost > budget / 2; i++)
        {
            var message = candidates[i];
            through = message.Sequence;
            if (IsReplayable(message))
            {
                folded.Add(message);
                historyCost -= TokenEstimator.Estimate(message.Content);
            }
        }

        return folded.Count == 0 ? null : new SummaryPlan(folded, through, budget / 4);
    }

    /// <summary>The project instructions, the system prompt and the summary, whichever are non-empty, as one system message.</summary>
    private static string? SystemText(string? projectInstructions, string? systemPrompt, RollingSummary? summary)
    {
        // Small models follow one system message more reliably than two.
        string[] parts = [.. new[]
            {
                projectInstructions,
                systemPrompt,
                string.IsNullOrWhiteSpace(summary?.Text) ? null : $"{SummaryHeading}\n{summary.Text.Trim()}",
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())];
        return parts.Length > 0 ? string.Join("\n\n", parts) : null;
    }

    private static IEnumerable<MessageDto> Unsummarized(IEnumerable<MessageDto> messages, RollingSummary? summary) =>
        summary is null ? messages : messages.Where(m => m.Sequence > summary.ThroughSequence);

    /// <summary>Complete messages, and stopped replies that produced text. Failed and empty messages are skipped.</summary>
    private static bool IsReplayable(MessageDto message) =>
        !string.IsNullOrWhiteSpace(message.Content)
        && message.Status is MessageStatus.Complete or MessageStatus.Cancelled;

    private static int LastUserMessageIndex(IReadOnlyList<MessageDto> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == MessageRole.User)
            {
                return i;
            }
        }

        throw new ArgumentException("The conversation has no user message to answer.", nameof(messages));
    }
}
