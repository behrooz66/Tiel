using LocalChat.Web.Data;
using LocalChat.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LocalChat.Web.Services;

public interface IModelService
{
    /// <summary>Ordered by display name.</summary>
    Task<IReadOnlyList<ModelDto>> ListAsync(bool includeUnavailable, CancellationToken ct);

    Task<SyncResult> SyncAsync(CancellationToken ct);

    /// <summary>A null argument leaves that field unchanged.</summary>
    Task<ModelDto> UpdateAsync(Guid id, string? displayName, int? contextLength, CancellationToken ct);
}

public sealed class ModelService(
    IDbContextFactory<AppDbContext> dbFactory,
    IModelSyncService modelSync,
    TimeProvider time) : IModelService
{
    public const int MaxDisplayNameLength = 100;
    public const int MinContextLength = 512;

    /// <summary>The upper bound for the context length when Ollama does not report the model's maximum.</summary>
    public const int MaxContextLengthWhenUnknown = 131072;

    public async Task<IReadOnlyList<ModelDto>> ListAsync(bool includeUnavailable, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Models
            .Where(m => includeUnavailable || m.IsAvailable)
            .OrderByDisplayName()
            .Select(m => new ModelDto(m.Id, m.Tag, m.DisplayName, m.ContextLength, m.MaxContextLength, m.IsAvailable))
            .ToListAsync(ct);
    }

    public Task<SyncResult> SyncAsync(CancellationToken ct) => modelSync.SyncAsync(ct);

    public async Task<ModelDto> UpdateAsync(Guid id, string? displayName, int? contextLength, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var model = await db.Models.SingleOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("The model does not exist.");

        var errors = new Dictionary<string, string>();
        if (displayName is not null)
        {
            var trimmed = displayName.Trim();
            if (trimmed.Length == 0)
            {
                errors[nameof(ModelDto.DisplayName)] = "Enter a display name.";
            }
            else if (trimmed.Length > MaxDisplayNameLength)
            {
                errors[nameof(ModelDto.DisplayName)] = $"Use at most {MaxDisplayNameLength} characters.";
            }
            else
            {
                model.DisplayName = trimmed;
            }
        }

        if (contextLength is { } length)
        {
            var max = model.MaxContextLength ?? MaxContextLengthWhenUnknown;
            if (length < MinContextLength || length > max)
            {
                errors[nameof(ModelDto.ContextLength)] = $"Enter a context length from {MinContextLength} to {max}.";
            }
            else
            {
                model.ContextLength = length;
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (db.ChangeTracker.HasChanges())
        {
            model.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }

        return new ModelDto(model.Id, model.Tag, model.DisplayName, model.ContextLength, model.MaxContextLength, model.IsAvailable);
    }
}

internal static class ModelQueries
{
    /// <summary>By display name, ignoring case, then by tag so the order is stable.</summary>
    public static IOrderedQueryable<Model> OrderByDisplayName(this IQueryable<Model> models) =>
        models.OrderBy(m => EF.Functions.Collate(m.DisplayName, "NOCASE")).ThenBy(m => m.Tag);

    /// <summary>
    /// The model with this id, which must exist and be available: the rule for creating a conversation
    /// with a model, switching to one, or sending to one.
    /// </summary>
    public static async Task<Model> GetAvailableAsync(this IQueryable<Model> models, Guid id, CancellationToken ct)
    {
        var model = await models.SingleOrDefaultAsync(m => m.Id == id, ct);
        return model switch
        {
            null => throw new ValidationException("ModelId", "The model does not exist."),
            { IsAvailable: false } => throw new ValidationException(
                "ModelId", $"{model.DisplayName} is no longer installed in Ollama. Pick another model."),
            _ => model,
        };
    }
}
