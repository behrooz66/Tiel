using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LocalChat.Web.Data;
using LocalChat.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models.Exceptions;
using OllamaOption = OllamaSharp.Models.OllamaOption;

namespace LocalChat.Web.Services;

public interface IGenerationService
{
    Task<TurnStarted> SendAsync(Guid conversationId, string content, Guid? modelId, CancellationToken ct);

    /// <summary>Only when the last message is an assistant reply that failed or was stopped; it is replaced.</summary>
    Task<TurnStarted> RetryAsync(Guid conversationId, CancellationToken ct);

    void Stop(Guid conversationId);

    bool IsGenerating(Guid conversationId);

    /// <summary>Joins the running generation, if any: its snapshot so far, then every later event.</summary>
    GenerationSubscription Subscribe(Guid conversationId, Func<GenerationEvent, Task> onEvent);
}

/// <summary>
/// Generates replies in background tasks that outlive any component, request or tab. Each running
/// generation is registered by conversation id; subscribers get their own channel, so a slow one
/// never blocks generation, and a late one gets the text so far plus exactly the deltas after it.
/// </summary>
public sealed class GenerationService : IGenerationService, IHostedService
{
    public static readonly TimeSpan PartialSaveInterval = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<Guid, ActiveGeneration> _active = new();
    private readonly ConcurrentDictionary<ActiveGeneration, Task> _running = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IOllamaClientProvider _clients;
    private readonly ITitleService _titles;
    private readonly ChangeNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<GenerationService> _logger;

    public GenerationService(
        IDbContextFactory<AppDbContext> dbFactory,
        IOllamaClientProvider clients,
        ITitleService titles,
        ChangeNotifier notifier,
        TimeProvider time,
        IHostApplicationLifetime lifetime,
        ILogger<GenerationService> logger)
    {
        _dbFactory = dbFactory;
        _clients = clients;
        _titles = titles;
        _notifier = notifier;
        _time = time;
        _logger = logger;
        lifetime.ApplicationStopping.Register(CancelAll);
    }

    public async Task<TurnStarted> SendAsync(Guid conversationId, string content, Guid? modelId, CancellationToken ct)
    {
        var text = content.Trim();
        if (text.Length == 0)
        {
            throw new ValidationException("Content", "Type a message first.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw ConversationNotFound();
        var model = await db.Models.GetAvailableAsync(modelId ?? conversation.ModelId, ct);

        var generation = Register(conversationId);
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var last = await LastSequenceAsync(db, conversationId, ct);
            var user = NewMessage(conversationId, last + 1, MessageRole.User, text, MessageStatus.Complete, modelId: null, now);
            var assistant = NewMessage(conversationId, last + 2, MessageRole.Assistant, "", MessageStatus.Streaming, model.Id, now);
            db.Messages.AddRange(user, assistant);
            conversation.ModelId = model.Id;
            conversation.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Start(generation, conversation.ProjectId, ToDto(user), ToDto(assistant));
        }
        catch
        {
            Release(generation);
            throw;
        }
    }

    public async Task<TurnStarted> RetryAsync(Guid conversationId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw ConversationNotFound();
        var failed = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Sequence)
            .FirstOrDefaultAsync(ct);
        if (failed is not { Role: MessageRole.Assistant, Status: MessageStatus.Error or MessageStatus.Cancelled })
        {
            throw new ConflictException("Only a reply that failed or was stopped can be retried.");
        }

        var model = await db.Models.GetAvailableAsync(conversation.ModelId, ct);
        var generation = Register(conversationId);
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Messages.Remove(failed);
            await db.SaveChangesAsync(ct);
            var assistant = NewMessage(
                conversationId, await LastSequenceAsync(db, conversationId, ct) + 1, MessageRole.Assistant, "",
                MessageStatus.Streaming, model.Id, now);
            db.Messages.Add(assistant);
            conversation.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Start(generation, conversation.ProjectId, userMessage: null, ToDto(assistant));
        }
        catch
        {
            Release(generation);
            throw;
        }
    }

    public void Stop(Guid conversationId)
    {
        if (_active.TryGetValue(conversationId, out var generation))
        {
            generation.Cancellation.Cancel();
        }
    }

    public bool IsGenerating(Guid conversationId) => _active.ContainsKey(conversationId);

    public GenerationSubscription Subscribe(Guid conversationId, Func<GenerationEvent, Task> onEvent)
    {
        if (!_active.TryGetValue(conversationId, out var generation))
        {
            return GenerationSubscription.None;
        }

        var channel = Channel.CreateUnbounded<GenerationEvent>(new UnboundedChannelOptions { SingleReader = true });
        GenerationSnapshot snapshot;
        lock (generation.Gate)
        {
            if (generation.Finished)
            {
                return GenerationSubscription.None;
            }

            // Under the same lock as every publish: the snapshot and the channel see exactly complementary text.
            generation.Subscribers.Add(channel);
            snapshot = new GenerationSnapshot(generation.AssistantMessageId, generation.Text.ToString(), generation.TrimmedMessages);
        }

        var stopped = new CancellationTokenSource();
        _ = Task.Run(() => DeliverAsync(channel.Reader, onEvent, stopped.Token));
        return new GenerationSubscription(snapshot, () =>
        {
            lock (generation.Gate)
            {
                generation.Subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
            stopped.Cancel();
        });
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Runs at shutdown, after <c>ApplicationStopping</c> cancelled every generation: waits for them to save.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancelAll();
        try
        {
            await Task.WhenAll(_running.Values).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Shutdown did not wait for every generation to save.");
        }
    }

    /// <summary>For tests: completes when every background task, including title generation, has finished.</summary>
    internal Task WhenIdleAsync() => Task.WhenAll(_running.Values);

    private ActiveGeneration Register(Guid conversationId)
    {
        var generation = new ActiveGeneration(conversationId);
        return _active.TryAdd(conversationId, generation)
            ? generation
            : throw new ConflictException("A reply is already being generated in this conversation.");
    }

    /// <summary>Unregisters this generation only; a newer one for the same conversation stays.</summary>
    private void Release(ActiveGeneration generation) =>
        _active.TryRemove(new KeyValuePair<Guid, ActiveGeneration>(generation.ConversationId, generation));

    private TurnStarted Start(ActiveGeneration generation, Guid projectId, MessageDto? userMessage, MessageDto assistant)
    {
        lock (generation.Gate)
        {
            generation.AssistantMessageId = assistant.Id;
        }

        _notifier.NotifyConversationsChanged(projectId);
        var task = Task.Run(() => RunAsync(generation, projectId, userMessage, assistant));
        _running.TryAdd(generation, task);
        _ = task.ContinueWith(_ => _running.TryRemove(generation, out var _), TaskScheduler.Default);
        return new TurnStarted(userMessage, assistant);
    }

    private async Task RunAsync(ActiveGeneration generation, Guid projectId, MessageDto? userMessage, MessageDto assistant)
    {
        var token = generation.Cancellation.Token;
        Turn? turn = null;
        MessageDto? completed = null;
        try
        {
            try
            {
                turn = await LoadTurnAsync(generation.ConversationId, assistant, token);
                var prompt = PromptBuilder.Build(turn.ProjectInstructions, turn.SystemPrompt, turn.Messages, turn.ContextLength);
                if (prompt.ExceedsBudget)
                {
                    _logger.LogWarning(
                        "The prompt for conversation {ConversationId} needs about {Tokens} tokens, over the budget of {Budget}; sending it anyway.",
                        generation.ConversationId, prompt.EstimatedTokens, prompt.Budget);
                }

                Publish(generation, new GenerationStarted(userMessage, assistant, prompt.TrimmedMessages),
                    () => generation.TrimmedMessages = prompt.TrimmedMessages);

                var client = await _clients.GetChatClientAsync(token);
                var options = new ChatOptions { ModelId = turn.ModelTag }.AddOllamaOption(OllamaOption.NumCtx, turn.ContextLength);
                long? outputTokens = null;
                var lastSave = _time.GetTimestamp();
                await foreach (var update in client.GetStreamingResponseAsync(prompt.Messages, options, token))
                {
                    foreach (var usage in update.Contents.OfType<UsageContent>())
                    {
                        outputTokens = usage.Details.OutputTokenCount ?? outputTokens;
                    }

                    if (update.Text is not { Length: > 0 } text)
                    {
                        continue;
                    }

                    Publish(generation, new GenerationDelta(text), () => generation.Text.Append(text));
                    if (_time.GetElapsedTime(lastSave) >= PartialSaveInterval)
                    {
                        await SavePartialAsync(assistant.Id, TextOf(generation), token);
                        lastSave = _time.GetTimestamp();
                    }
                }

                // OllamaSharp ends the stream quietly when cancelled, instead of throwing: that is still a stop.
                token.ThrowIfCancellationRequested();
                completed = await SaveFinalAsync(assistant, TextOf(generation), MessageStatus.Complete, null, (int?)outputTokens);
                Publish(generation, new GenerationCompleted(completed));
                _notifier.NotifyConversationsChanged(projectId);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                // Stopped: whatever the stream threw on the way out (a cancellation or a cut connection) is the stop.
                var cancelled = await SaveFinalAsync(assistant, TextOf(generation), MessageStatus.Cancelled, null, null);
                Publish(generation, new GenerationCompleted(cancelled));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generating a reply in conversation {ConversationId} failed.", generation.ConversationId);
                var failed = await SaveFinalAsync(assistant, TextOf(generation), MessageStatus.Error, Describe(ex), null);
                Publish(generation, new GenerationFailed(failed));
            }
            finally
            {
                // The reply is settled: a new turn may start while the title is generated.
                Release(generation);
            }

            if (completed is not null && turn is { IsFirstReply: true })
            {
                await GenerateTitleAsync(generation, projectId, turn);
            }
        }
        finally
        {
            lock (generation.Gate)
            {
                generation.Finished = true;
                foreach (var subscriber in generation.Subscribers)
                {
                    subscriber.Writer.TryComplete();
                }

                generation.Subscribers.Clear();
            }
        }
    }

    private async Task GenerateTitleAsync(ActiveGeneration generation, Guid projectId, Turn turn)
    {
        try
        {
            var title = await _titles.GenerateAsync(turn.ModelTag, turn.ContextLength, turn.FirstUserMessage, _shutdown.Token);
            await using var db = await _dbFactory.CreateDbContextAsync(_shutdown.Token);
            // Only if nobody renamed the chat meanwhile.
            var renamed = await db.Conversations
                .Where(c => c.Id == generation.ConversationId && c.Title == ConversationService.DefaultTitle)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Title, title)
                    .SetProperty(c => c.UpdatedAt, _time.GetUtcNow().UtcDateTime), _shutdown.Token);
            if (renamed > 0)
            {
                Publish(generation, new TitleGenerated(title));
                _notifier.NotifyConversationsChanged(projectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Saving a title for conversation {ConversationId} failed.", generation.ConversationId);
        }
    }

    private async Task<Turn> LoadTurnAsync(Guid conversationId, MessageDto assistant, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.Conversations
            .Where(c => c.Id == conversationId)
            .Select(c => new { c.Title, c.SystemPrompt, c.Project.Instructions })
            .SingleAsync(ct);
        var messages = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Sequence)
            .Select(ConversationService.ToMessageDto)
            .ToListAsync(ct);
        var model = await db.Models.Where(m => m.Id == assistant.ModelId).Select(m => new { m.Tag, m.ContextLength }).SingleAsync(ct);

        var isFirstReply = conversation.Title == ConversationService.DefaultTitle
            && !messages.Any(m => m.Role == MessageRole.Assistant && m.Status == MessageStatus.Complete);
        return new Turn(
            conversation.Instructions, conversation.SystemPrompt, messages, model.Tag, model.ContextLength,
            isFirstReply, messages.First(m => m.Role == MessageRole.User).Content);
    }

    private async Task SavePartialAsync(Guid messageId, string content, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.Messages.Where(m => m.Id == messageId).ExecuteUpdateAsync(s => s.SetProperty(m => m.Content, content), ct);
    }

    /// <summary>
    /// Saves how the reply ended, even while shutting down. A deleted conversation just updates nothing.
    /// Returns the final message either way, so subscribers always get a last event.
    /// </summary>
    private async Task<MessageDto> SaveFinalAsync(MessageDto assistant, string content, MessageStatus status, string? error, int? tokenCount)
    {
        var final = assistant with { Content = content, Status = status, ErrorMessage = error, TokenCount = tokenCount };
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
            await db.Messages.Where(m => m.Id == assistant.Id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Content, content)
                .SetProperty(m => m.Status, status)
                .SetProperty(m => m.ErrorMessage, error)
                .SetProperty(m => m.TokenCount, tokenCount), CancellationToken.None);
            if (status == MessageStatus.Complete)
            {
                var conversationId = await db.Messages.Where(m => m.Id == assistant.Id).Select(m => m.ConversationId).SingleOrDefaultAsync(CancellationToken.None);
                await db.Conversations.Where(c => c.Id == conversationId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, _time.GetUtcNow().UtcDateTime), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving reply {MessageId} as {Status} failed.", assistant.Id, status);
        }

        return final;
    }

    private void CancelAll()
    {
        _shutdown.Cancel();
        foreach (var generation in _active.Values)
        {
            generation.Cancellation.Cancel();
        }
    }

    private async Task DeliverAsync(ChannelReader<GenerationEvent> reader, Func<GenerationEvent, Task> onEvent, CancellationToken stopped)
    {
        try
        {
            await foreach (var generationEvent in reader.ReadAllAsync(stopped))
            {
                try
                {
                    await onEvent(generationEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "A generation subscriber failed on {Event}.", generationEvent.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            // Unsubscribed: drop whatever was still queued.
        }
    }

    /// <summary>Applies <paramref name="change"/> and writes the event to every subscriber, atomically with <see cref="Subscribe"/>.</summary>
    private static void Publish(ActiveGeneration generation, GenerationEvent generationEvent, Action? change = null)
    {
        lock (generation.Gate)
        {
            change?.Invoke();
            foreach (var subscriber in generation.Subscribers)
            {
                subscriber.Writer.TryWrite(generationEvent);
            }
        }
    }

    private static string TextOf(ActiveGeneration generation)
    {
        lock (generation.Gate)
        {
            return generation.Text.ToString();
        }
    }

    /// <summary>A short, human-readable reason for the message bubble. The full exception goes to the log.</summary>
    private static string Describe(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
            "Ollama does not have this model. Install it in Ollama, or pick another model.",
        HttpRequestException { StatusCode: { } status } => $"Ollama returned an error ({(int)status} {status}).",
        HttpRequestException => $"Ollama is unreachable: {exception.Message}",
        TaskCanceledException { InnerException: TimeoutException } => "Ollama did not respond in time.",
        OllamaException => $"Ollama returned an error: {exception.Message}",
        JsonException => "The server at the Ollama URL did not answer like Ollama.",
        _ => "The reply failed because of an unexpected error. The app log has the details.",
    };

    private static async Task<int> LastSequenceAsync(AppDbContext db, Guid conversationId, CancellationToken ct) =>
        await db.Messages.Where(m => m.ConversationId == conversationId).MaxAsync(m => (int?)m.Sequence, ct) ?? 0;

    private static Message NewMessage(
        Guid conversationId, int sequence, MessageRole role, string content, MessageStatus status, Guid? modelId, DateTime now) => new()
    {
        ConversationId = conversationId,
        Sequence = sequence,
        Role = role,
        Content = content,
        Status = status,
        ModelId = modelId,
        CreatedAt = now,
    };

    private static MessageDto ToDto(Message m) =>
        new(m.Id, m.Sequence, m.Role, m.Content, m.Status, m.ErrorMessage, m.ModelId, m.TokenCount, m.CreatedAt);

    private static NotFoundException ConversationNotFound() => new("The conversation does not exist.");

    private sealed record Turn(
        string? ProjectInstructions,
        string? SystemPrompt,
        IReadOnlyList<MessageDto> Messages,
        string ModelTag,
        int ContextLength,
        bool IsFirstReply,
        string FirstUserMessage);

    private sealed class ActiveGeneration(Guid conversationId)
    {
        public Guid ConversationId { get; } = conversationId;
        public CancellationTokenSource Cancellation { get; } = new();
        public Lock Gate { get; } = new();
        public StringBuilder Text { get; } = new();
        public List<Channel<GenerationEvent>> Subscribers { get; } = [];
        public Guid AssistantMessageId { get; set; }
        public int TrimmedMessages { get; set; }
        public bool Finished { get; set; }
    }
}
