using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace Tiel.Web.Services;

public interface ITitleService
{
    /// <summary>A short title for a conversation that starts with <paramref name="firstUserMessage"/>. Never fails: falls back to the message itself.</summary>
    Task<string> GenerateAsync(string modelTag, int contextLength, string firstUserMessage, CancellationToken ct);
}

public sealed partial class TitleService(IOllamaClientProvider clients, ILogger<TitleService> logger) : ITitleService
{
    public const string SystemPrompt = "You write short titles for chat conversations.";
    public const string Instruction =
        "Write a title of at most 6 words for a conversation that starts with the message below. Reply with the title only.";

    public const int MaxTitleLength = 60;
    public const int FallbackLength = 50;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Shortened by tests.</summary>
    internal TimeSpan Timeout { get; init; } = DefaultTimeout;

    public async Task<string> GenerateAsync(string modelTag, int contextLength, string firstUserMessage, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var client = await clients.GetChatClientAsync(timeout.Token);
            var excerpt = firstUserMessage.Length > 1000 ? firstUserMessage[..1000] : firstUserMessage;
            // The same num_ctx as the chat, so Ollama does not reload the model for a different context size.
            var options = new ChatOptions { ModelId = modelTag }.AddOllamaOption(OllamaOption.NumCtx, contextLength);
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, $"{Instruction}\n\n{excerpt}")],
                options,
                timeout.Token);
            if (Clean(response.Text) is { Length: > 0 } title)
            {
                return title;
            }

            logger.LogWarning("The model returned an empty title; using the first message instead.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Title generation timed out after {Seconds} seconds.", Timeout.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Title generation failed; using the first message instead.");
        }

        return Fallback(firstUserMessage);
    }

    /// <summary>
    /// The first non-empty line, without a leading <c>Title:</c>, quotes, asterisks or trailing punctuation,
    /// with whitespace collapsed, capped at 60 characters.
    /// </summary>
    public static string Clean(string raw)
    {
        var line = raw.Split('\n').Select(l => l.Replace("*", "").Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        if (line.StartsWith("```", StringComparison.Ordinal))
        {
            // Small models sometimes answer the message instead; a code block is not a title.
            return "";
        }

        if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
        {
            line = line["Title:".Length..];
        }

        line = DoubleQuotes().Replace(line, "").Trim().Trim('\'', '‘', '’', '`');
        line = Whitespace().Replace(line, " ").Trim().TrimEnd('.', ',', ';', ':', '!', '?', '…').TrimEnd();
        return line.Length > MaxTitleLength ? line[..MaxTitleLength].TrimEnd() : line;
    }

    /// <summary>The first 50 characters of the message, with <c>…</c> when cut.</summary>
    public static string Fallback(string firstUserMessage)
    {
        var text = Whitespace().Replace(firstUserMessage, " ").Trim();
        return text.Length > FallbackLength ? text[..FallbackLength].TrimEnd() + "…" : text;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("[\"“”]")]
    private static partial Regex DoubleQuotes();
}
