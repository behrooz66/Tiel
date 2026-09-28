using LocalChat.Web.Data;
using LocalChat.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LocalChat.Web.Services;

/// <summary>The only reader and writer of the AppSettings table.</summary>
public sealed class SettingsService(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<SettingsService> logger)
{
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
}
