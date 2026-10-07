using System.Text;
using System.Text.RegularExpressions;
using Tiel.Web.Data.Entities;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace Tiel.Web.Services;

public interface ISummaryService
{
    /// <summary>
    /// Folds <paramref name="messages"/> into <paramref name="previousSummary"/> and returns the new summary.
    /// Null when the call fails, times out or returns nothing; the caller then keeps the old summary.
    /// </summary>
    Task<string?> SummarizeAsync(
        string modelTag, int contextLength, string? previousSummary, IReadOnlyList<MessageDto> messages, int maxTokens, CancellationToken ct);
}

public sealed partial class SummaryService(IOllamaClientProvider clients, ILogger<SummaryService> logger) : ISummaryService
{
    public const string SystemPrompt =
        "You keep a running summary of a conversation between a user and an assistant, so it can continue without the full transcript.";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Shortened by tests.</summary>
    internal TimeSpan Timeout { get; init; } = DefaultTimeout;

    public async Task<string?> SummarizeAsync(
        string modelTag, int contextLength, string? previousSummary, IReadOnlyList<MessageDto> messages, int maxTokens, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var client = await clients.GetChatClientAsync(timeout.Token);
            // The same num_ctx as the chat, so Ollama does not reload the model for a different context size.
            var options = new ChatOptions { ModelId = modelTag }.AddOllamaOption(OllamaOption.NumCtx, contextLength);
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, Instruction(previousSummary, messages, maxTokens))],
                options,
                timeout.Token);
            if (Clean(response.Text) is { Length: > 0 } summary)
            {
                return summary;
            }

            logger.LogWarning("The model returned an empty summary; keeping the previous one.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Summarizing timed out after {Seconds} seconds.", Timeout.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Summarizing failed; keeping the previous summary.");
        }

        return null;
    }

    /// <summary>The request: the summary so far, the new messages as a transcript, and a word limit within <paramref name="maxTokens"/>.</summary>
    public static string Instruction(string? previousSummary, IReadOnlyList<MessageDto> messages, int maxTokens)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(previousSummary))
        {
            text.Append("Summary so far:\n").Append(previousSummary.Trim()).Append("\n\n");
        }

        text.Append("New messages:\n");
        foreach (var message in messages)
        {
            text.Append(message.Role == MessageRole.User ? "User: " : "Assistant: ").Append(message.Content.Trim()).Append("\n\n");
        }

        // About two words per three tokens; one word per two tokens leaves room for code and numbers.
        text.Append($"Write an updated summary that covers {(string.IsNullOrWhiteSpace(previousSummary) ? "these messages" : "the summary so far and the new messages")}, ")
            .Append($"in at most {Math.Max(50, maxTokens / 2)} words. ")
            .Append("Keep facts, decisions, names, numbers, code identifiers, the user's preferences, and open questions or tasks. ")
            .Append("Drop greetings and repetition. Write plain notes, not a dialogue. Reply with the summary only.");
        return text.ToString();
    }

    /// <summary>Without a reasoning block or a leading <c>Summary:</c>, trimmed.</summary>
    public static string Clean(string raw)
    {
        var text = ThinkBlock().Replace(raw, "").Trim();
        if (text.StartsWith("Summary:", StringComparison.OrdinalIgnoreCase))
        {
            text = text["Summary:".Length..].Trim();
        }

        return text;
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();
}
