using System.Linq.Expressions;
using Tiel.Web.Data;
using Tiel.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Tiel.Web.Services;

public interface IConversationService
{
    /// <summary>Newest <c>UpdatedAt</c> first.</summary>
    Task<IReadOnlyList<ConversationSummary>> ListAsync(Guid projectId, CancellationToken ct);

    /// <summary>Messages by sequence.</summary>
    Task<ConversationDetail> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Uses the given model, else the default, else the first available one.</summary>
    Task<ConversationDetail> CreateAsync(Guid projectId, Guid? modelId, CancellationToken ct);

    Task RenameAsync(Guid id, string title, CancellationToken ct);

    Task SetModelAsync(Guid id, Guid modelId, CancellationToken ct);

    Task SetSystemPromptAsync(Guid id, string? systemPrompt, CancellationToken ct);

    /// <summary>Stops an active generation first.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class ConversationService(
    IDbContextFactory<AppDbContext> dbFactory,
    ISettingsService settings,
    IGenerationService generation,
    ChangeNotifier notifier,
    TimeProvider time) : IConversationService
{
    public const string DefaultTitle = "New chat";
    public const int MaxTitleLength = 200;

    internal static readonly Expression<Func<Message, MessageDto>> ToMessageDto = m => new MessageDto(
        m.Id, m.Sequence, m.Role, m.Content, m.Status, m.ErrorMessage, m.ModelId, m.TokenCount, m.CreatedAt);

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new NotFoundException("The project does not exist.");
        }

        return await db.Conversations
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new ConversationSummary(c.Id, c.ProjectId, c.Title, c.ModelId, c.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<ConversationDetail> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw NotFound();
        var messages = await db.Messages
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.Sequence)
            .Select(ToMessageDto)
            .ToListAsync(ct);
        return ToDetail(conversation, messages);
    }

    public async Task<ConversationDetail> CreateAsync(Guid projectId, Guid? modelId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new NotFoundException("The project does not exist.");
        }

        var model = modelId is { } id
            ? await db.Models.GetAvailableAsync(id, ct)
            : await GetDefaultOrFirstAvailableAsync(db, ct);

        var now = time.GetUtcNow().UtcDateTime;
        var conversation = new Conversation
        {
            ProjectId = projectId,
            ModelId = model.Id,
            Title = DefaultTitle,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);

        notifier.NotifyConversationsChanged(projectId);
        return ToDetail(conversation, []);
    }

    public async Task RenameAsync(Guid id, string title, CancellationToken ct)
    {
        var trimmed = title.Trim();
        var error = trimmed.Length switch
        {
            0 => "Enter a title.",
            > MaxTitleLength => $"Use at most {MaxTitleLength} characters.",
            _ => null,
        };
        if (error is not null)
        {
            throw new ValidationException(nameof(ConversationDetail.Title), error);
        }

        await UpdateAsync(id, c => c.Title = trimmed, ct);
    }

    public async Task SetModelAsync(Guid id, Guid modelId, CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            if (!await db.Conversations.AnyAsync(c => c.Id == id, ct))
            {
                throw NotFound();
            }

            await db.Models.GetAvailableAsync(modelId, ct);
        }

        await UpdateAsync(id, c => c.ModelId = modelId, ct);
    }

    public Task SetSystemPromptAsync(Guid id, string? systemPrompt, CancellationToken ct) =>
        UpdateAsync(id, c => c.SystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt.Trim(), ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var projectId = await db.Conversations.Where(c => c.Id == id).Select(c => (Guid?)c.ProjectId).SingleOrDefaultAsync(ct)
            ?? throw NotFound();

        generation.Stop(id);
        // The database cascades the delete to the messages. A stopped reply then saves into nothing.
        await db.Conversations.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        notifier.NotifyConversationsChanged(projectId);
    }

    /// <summary>Applies <paramref name="change"/>; when it changed anything, bumps <c>UpdatedAt</c>, saves and notifies.</summary>
    private async Task UpdateAsync(Guid id, Action<Conversation> change, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
        change(conversation);
        if (!db.ChangeTracker.HasChanges())
        {
            return;
        }

        conversation.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        notifier.NotifyConversationsChanged(conversation.ProjectId);
    }

    private async Task<Model> GetDefaultOrFirstAvailableAsync(AppDbContext db, CancellationToken ct)
    {
        // A default that names a missing or unavailable model counts as unset.
        if ((await settings.GetAsync(ct)).DefaultModelId is { } defaultId
            && await db.Models.SingleOrDefaultAsync(m => m.Id == defaultId && m.IsAvailable, ct) is { } preferred)
        {
            return preferred;
        }

        return await db.Models.Where(m => m.IsAvailable).OrderByDisplayName().FirstOrDefaultAsync(ct)
            ?? throw new ValidationException(
                "ModelId", "No model is available. Install one in Ollama, then sync models in Settings.");
    }

    private ConversationDetail ToDetail(Conversation c, IReadOnlyList<MessageDto> messages) =>
        new(c.Id, c.ProjectId, c.Title, c.ModelId, c.SystemPrompt, c.Summary, c.SummarizedThroughSequence,
            c.CreatedAt, c.UpdatedAt, generation.IsGenerating(c.Id), generation.IsSummarizing(c.Id), messages);

    private static NotFoundException NotFound() => new("The conversation does not exist.");
}
