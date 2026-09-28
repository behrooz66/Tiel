using LocalChat.Web.Data.Entities;
using LocalChat.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace LocalChat.Web.Data;

/// <summary>Prepares the database at every startup. Idempotent.</summary>
public sealed class Seed(
    IDbContextFactory<AppDbContext> dbFactory,
    SettingsService settings,
    IModelSyncService modelSync,
    TimeProvider time,
    ILogger<Seed> logger)
{
    public const string GeneralProjectName = "General";
    public const string InterruptedErrorMessage = "Interrupted: the app stopped during generation.";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);

        if (!await db.Projects.AnyAsync(p => p.Id == ProjectIds.General, ct))
        {
            var now = time.GetUtcNow().UtcDateTime;
            db.Projects.Add(new Project { Id = ProjectIds.General, Name = GeneralProjectName, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(ct);
        }

        await settings.SeedAsync(ct);

        // A reply still streaming at startup was cut off when the app last stopped.
        var interrupted = await db.Messages
            .Where(m => m.Status == MessageStatus.Streaming)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.Error)
                .SetProperty(m => m.ErrorMessage, InterruptedErrorMessage), ct);
        if (interrupted > 0)
        {
            logger.LogWarning("Marked {Count} interrupted message(s) as failed.", interrupted);
        }

        // The app must start without Ollama; the Settings page can sync later.
        try
        {
            await modelSync.SyncAsync(ct);
        }
        catch (OllamaUnavailableException ex)
        {
            logger.LogWarning("Models were not synced at startup: {Error}", ex.Message);
        }
    }
}
