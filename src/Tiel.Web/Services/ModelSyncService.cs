using System.Globalization;
using System.Text.Json;
using Tiel.Web.Data;
using Tiel.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using OllamaSharp;
using ShowModelResponse = OllamaSharp.Models.ShowModelResponse;

namespace Tiel.Web.Services;

public interface IModelSyncService
{
    /// <summary>Brings the Models table in line with Ollama's installed models, then repairs the default model.</summary>
    Task<SyncResult> SyncAsync(CancellationToken ct);
}

/// <summary>The model sync algorithm from the spec's Settings section. Syncs run one at a time.</summary>
public sealed class ModelSyncService(
    IDbContextFactory<AppDbContext> dbFactory,
    IOllamaClientProvider clients,
    ISettingsService settings,
    TimeProvider time,
    ILogger<ModelSyncService> logger) : IModelSyncService
{
    public const int DefaultContextLength = 4096;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Bounds the Ollama calls of one sync. Shortened by tests.</summary>
    internal TimeSpan Timeout { get; init; } = DefaultTimeout;

    public async Task<SyncResult> SyncAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var installed = await ListInstalledChatModelsAsync(ct);
            var result = await UpsertAsync(installed, ct);
            await EnsureDefaultModelAsync(ct);
            logger.LogInformation(
                "Synced models from Ollama: {Added} added, {Updated} updated, {MarkedUnavailable} marked unavailable.",
                result.Added, result.Updated, result.MarkedUnavailable);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Steps 1 and 2: installed models that can chat, with their maximum context when known.</summary>
    private async Task<List<InstalledModel>> ListInstalledChatModelsAsync(CancellationToken ct)
    {
        var client = await clients.GetApiClientAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var installed = new List<InstalledModel>();
            foreach (var model in await client.ListLocalModelsAsync(timeout.Token))
            {
                var details = await client.ShowModelAsync(model.Name, timeout.Token);
                if (details.Capabilities is { } capabilities && !capabilities.Contains("completion"))
                {
                    logger.LogDebug("Skipping {Tag}: it cannot chat (capabilities: {Capabilities}).", model.Name, capabilities);
                    continue;
                }

                installed.Add(new InstalledModel(model.Name, ReadMaxContextLength(details)));
            }

            return installed;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OllamaUnavailableException($"Ollama did not respond within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OllamaUnavailableException($"Ollama is unreachable: {OllamaUnavailableException.Describe(ex)}", ex);
        }
    }

    /// <summary>Steps 3 and 4: upsert by tag, keeping the user's display name and context length.</summary>
    private async Task<SyncResult> UpsertAsync(List<InstalledModel> installed, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stored = await db.Models.ToDictionaryAsync(m => m.Tag, ct);
        int added = 0, updated = 0, markedUnavailable = 0;

        foreach (var model in installed)
        {
            if (!stored.Remove(model.Tag, out var row))
            {
                db.Models.Add(new Model
                {
                    Tag = model.Tag,
                    DisplayName = model.Tag.Length > ModelService.MaxDisplayNameLength
                        ? model.Tag[..ModelService.MaxDisplayNameLength]
                        : model.Tag,
                    ContextLength = Math.Min(model.MaxContextLength ?? DefaultContextLength, DefaultContextLength),
                    MaxContextLength = model.MaxContextLength,
                    IsAvailable = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                added++;
            }
            else if (!row.IsAvailable || row.MaxContextLength != model.MaxContextLength)
            {
                row.IsAvailable = true;
                row.MaxContextLength = model.MaxContextLength;
                row.UpdatedAt = now;
                updated++;
            }
        }

        // Whatever is left was not in Ollama's list. Models are never deleted, so old messages keep their reference.
        foreach (var row in stored.Values.Where(m => m.IsAvailable))
        {
            row.IsAvailable = false;
            row.UpdatedAt = now;
            markedUnavailable++;
        }

        await db.SaveChangesAsync(ct);
        return new SyncResult(added, updated, markedUnavailable);
    }

    /// <summary>Step 5: an unset, missing or unavailable default becomes the first available model by display name.</summary>
    private async Task EnsureDefaultModelAsync(CancellationToken ct)
    {
        var defaultModelId = (await settings.GetAsync(ct)).DefaultModelId;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (defaultModelId is { } id && await db.Models.AnyAsync(m => m.Id == id && m.IsAvailable, ct))
        {
            return;
        }

        var first = await db.Models
            .Where(m => m.IsAvailable)
            .OrderByDisplayName()
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (first is not null)
        {
            await settings.SetDefaultModelAsync(first, ct);
        }
    }

    /// <summary>
    /// The model-info entry whose key ends in <c>.context_length</c>, preferring the one for the model's
    /// architecture. Keys such as <c>phi3.rope.scaling.original_context_length</c> do not count.
    /// </summary>
    private static int? ReadMaxContextLength(ShowModelResponse details)
    {
        if (details.Info?.ExtraInfo is not { } entries)
        {
            return null;
        }

        var value = details.Info.Architecture is { } architecture
            && entries.TryGetValue($"{architecture}.context_length", out var exact)
                ? exact
                : entries.FirstOrDefault(e => e.Key.EndsWith(".context_length", StringComparison.Ordinal)).Value;
        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out var length) => length,
            IConvertible convertible => convertible.ToInt32(CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    private sealed record InstalledModel(string Tag, int? MaxContextLength);
}
