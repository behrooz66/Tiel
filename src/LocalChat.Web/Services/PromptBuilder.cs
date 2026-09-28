using LocalChat.Web.Data.Entities;
using Microsoft.Extensions.AI;

namespace LocalChat.Web.Services;

/// <param name="Messages">What to send: an optional system message, the kept history, then the latest user message.</param>
/// <param name="TrimmedMessages">History messages dropped to fit the context window.</param>
/// <param name="EstimatedTokens">The estimate for <paramref name="Messages"/>.</param>
/// <param name="Budget">The tokens available for the prompt, after reserving room for the answer.</param>
public sealed record Prompt(IReadOnlyList<ChatMessage> Messages, int TrimmedMessages, int EstimatedTokens, int Budget)
{
    /// <summary>True when even the system message and the latest user message alone do not fit.</summary>
    public bool ExceedsBudget => EstimatedTokens > Budget;
}

/// <summary>Assembles the request for one turn from the stored conversation. Pure: no I/O, no clock.</summary>
public static class PromptBuilder
{
    /// <summary>The tokens available for the prompt: the context length minus room for the answer.</summary>
    public static int Budget(int contextLength) => contextLength - Math.Min(1024, contextLength / 4);

    /// <param name="messages">The conversation in sequence order. Its last user message is the one being answered; anything after it is ignored.</param>
    public static Prompt Build(
        string? projectInstructions,
        string? systemPrompt,
        IReadOnlyList<MessageDto> messages,
        int contextLength)
    {
        var latestIndex = LastUserMessageIndex(messages);
        var latest = messages[latestIndex].Content;
        var history = messages.Take(latestIndex).Where(IsReplayable).ToList();

        // Small models follow one system message more reliably than two.
        string[] systemParts = [.. new[] { projectInstructions, systemPrompt }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())];
        var system = systemParts.Length > 0 ? string.Join("\n\n", systemParts) : null;

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
