using LocalChat.Web.Data;
using LocalChat.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LocalChat.Web.Services;

public interface ISettingsService
{
    /// <summary>A typed view of all known keys. <c>DefaultModelId</c> may name a missing or unavailable model; callers treat that as unset.</summary>
    Task<AppSettingsSnapshot> GetAsync(CancellationToken ct);

    /// <summary>Validates, saves, raises <see cref="SettingsChanged"/>, then runs a model sync.</summary>
    Task SetOllamaBaseUrlAsync(string url, CancellationToken ct);

    /// <summary>Null clears it; otherwise the model must exist and be available.</summary>
    Task SetDefaultModelAsync(Guid? modelId, CancellationToken ct);

    event Action? SettingsChanged;
}

/// <summary>The only reader and writer of the AppSettings table. Values are cached until the next write.</summary>
public sealed class SettingsService(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<SettingsService> logger) : ISettingsService
{
    // Loads and writes take the gate, so a load can never cache a value that a concurrent write replaced.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettingsSnapshot? _cache;

    public event Action? SettingsChanged;

    public async Task<AppSettingsSnapshot> GetAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _cache) is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            return _cache ??= await LoadAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetOllamaBaseUrlAsync(string url, CancellationToken ct)
    {
        var definition = SettingDefinitions.OllamaBaseUrl;
        var normalized = SettingDefinitions.NormalizeUrl(url);
        if (definition.Validate(normalized) is { } error)
        {
            throw new ValidationException(nameof(AppSettingsSnapshot.OllamaBaseUrl), error);
        }

        await WriteAsync(definition.Key, definition.Format(normalized), ct);
    }

    public async Task SetDefaultModelAsync(Guid? modelId, CancellationToken ct)
    {
        var definition = SettingDefinitions.DefaultModelId;
        if (modelId is { } id)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var isAvailable = await db.Models
                .Where(m => m.Id == id)
                .Select(m => (bool?)m.IsAvailable)
                .SingleOrDefaultAsync(ct);
            var error = isAvailable switch
            {
                null => "The model does not exist.",
                false => "The model is no longer installed in Ollama.",
                true => null,
            };
            if (error is not null)
            {
                throw new ValidationException(nameof(AppSettingsSnapshot.DefaultModelId), error);
            }
        }

        await WriteAsync(definition.Key, modelId is null ? null : definition.Format(modelId), ct);
    }

    /// <summary>Runs at startup: inserts the <c>Ollama.BaseUrl</c> row from configuration when it is missing.</summary>
    public async Task SeedAsync(CancellationToken ct)
    {
        var definition = SettingDefinitions.OllamaBaseUrl;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.AppSettings.AnyAsync(s => s.Key == definition.Key, ct))
        {
            return;
        }

        var configured = configuration[SettingDefinitions.OllamaBaseUrlConfigurationKey];
        if (configured is not null && !definition.TryParse(configured, out _))
        {
            logger.LogWarning(
                "Configuration value {ConfigurationKey} '{Value}' is not an absolute http or https URL; using {Fallback}.",
                SettingDefinitions.OllamaBaseUrlConfigurationKey, configured, SettingDefinitions.FallbackOllamaBaseUrl);
        }

        db.AppSettings.Add(new AppSetting
        {
            Key = definition.Key,
            Value = definition.Format(definition.GetDefault(configuration)),
            UpdatedAt = time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>; null deletes the row.</summary>
    private async Task WriteAsync(string key, string? value, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var row = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == key, ct);
            if (value is null)
            {
                if (row is not null)
                {
                    db.AppSettings.Remove(row);
                }
            }
            else if (row is null)
            {
                db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = time.GetUtcNow().UtcDateTime });
            }
            else
            {
                row.Value = value;
                row.UpdatedAt = time.GetUtcNow().UtcDateTime;
            }

            await db.SaveChangesAsync(ct);
            _cache = null;
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke();
    }

    private async Task<AppSettingsSnapshot> LoadAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return new AppSettingsSnapshot(
            Read(rows, SettingDefinitions.OllamaBaseUrl),
            Read(rows, SettingDefinitions.DefaultModelId));
    }

    private T Read<T>(Dictionary<string, string> rows, SettingDefinition<T> definition)
    {
        if (!rows.TryGetValue(definition.Key, out var text))
        {
            return definition.GetDefault(configuration);
        }

        if (definition.TryParse(text, out var value))
        {
            return value;
        }

        logger.LogWarning("Setting {Key} has an invalid value '{Value}'; using the default.", definition.Key, text);
        return definition.GetDefault(configuration);
    }
}
