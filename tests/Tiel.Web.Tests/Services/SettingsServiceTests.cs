using Tiel.Web.Data.Entities;
using Tiel.Web.Services;
using Tiel.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Tiel.Web.Tests.Services;

public sealed class SettingsServiceTests : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Missing_rows_return_the_defaults()
    {
        var settings = new TestServices(_database, ollamaBaseUrl: "http://gpu-box:11434/").Settings;

        var snapshot = await settings.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new AppSettingsSnapshot("http://gpu-box:11434", null), snapshot);
    }

    [Fact]
    public async Task Missing_configuration_falls_back_to_the_local_ollama()
    {
        var settings = new TestServices(_database, ollamaBaseUrl: null).Settings;

        var snapshot = await settings.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal("http://localhost:11434", snapshot.OllamaBaseUrl);
    }

    [Fact]
    public async Task Unparseable_values_return_the_defaults_and_log_warnings()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddRowAsync("Ollama.BaseUrl", "localhost:11434", ct);
        await AddRowAsync("Chat.DefaultModelId", "not-a-guid", ct);
        var logger = new FakeLogger<SettingsService>();
        var settings = new TestServices(_database, settingsLogger: logger).Settings;

        var snapshot = await settings.GetAsync(ct);

        Assert.Equal(new AppSettingsSnapshot("http://localhost:11434", null), snapshot);
        var warnings = logger.Collector.GetSnapshot().Where(r => r.Level == LogLevel.Warning).Select(r => r.Message).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("Ollama.BaseUrl", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("Chat.DefaultModelId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Clearing_the_default_model_deletes_its_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = await AddModelAsync(isAvailable: true, ct);
        var settings = new TestServices(_database).Settings;
        await settings.SetDefaultModelAsync(model.Id, ct);
        Assert.Equal(model.Id.ToString("D"), await ReadRowAsync("Chat.DefaultModelId", ct));

        await settings.SetDefaultModelAsync(null, ct);

        Assert.Null(await ReadRowAsync("Chat.DefaultModelId", ct));
        Assert.Null((await settings.GetAsync(ct)).DefaultModelId);
    }

    [Fact]
    public async Task Values_are_cached_until_a_write_which_clears_the_cache_and_raises_SettingsChanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new TestServices(_database).Settings;
        var changes = 0;
        settings.SettingsChanged += () => changes++;
        Assert.Equal("http://localhost:11434", (await settings.GetAsync(ct)).OllamaBaseUrl);

        // A change made behind the service's back is not seen: the value is cached.
        await AddRowAsync("Ollama.BaseUrl", "http://elsewhere:11434", ct);
        Assert.Equal("http://localhost:11434", (await settings.GetAsync(ct)).OllamaBaseUrl);

        await settings.SetOllamaBaseUrlAsync("http://gpu-box:11434", ct);

        Assert.Equal("http://gpu-box:11434", (await settings.GetAsync(ct)).OllamaBaseUrl);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Saves_the_url_trimmed_and_without_a_trailing_slash()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new TestServices(_database).Settings;

        await settings.SetOllamaBaseUrlAsync("  https://gpu-box:11434/ ", ct);

        Assert.Equal("https://gpu-box:11434", await ReadRowAsync("Ollama.BaseUrl", ct));
    }

    [Fact]
    public async Task Saving_the_url_runs_a_model_sync_against_the_new_url()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new TestServices(_database);
        services.Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: ["completion"]));

        await services.Settings.SetOllamaBaseUrlAsync("http://gpu-box:11434", ct);

        Assert.Contains(services.Ollama.Requests, u => u.ToString() == "http://gpu-box:11434/api/tags");
        var model = Assert.Single(await services.Models.ListAsync(includeUnavailable: true, ct));
        Assert.Equal(model.Id, (await services.Settings.GetAsync(ct)).DefaultModelId);
    }

    [Fact]
    public async Task Saving_an_unreachable_url_still_saves_it_and_logs_the_failed_sync()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = new FakeLogger<SettingsService>();
        var services = new TestServices(_database, settingsLogger: logger);
        services.Ollama.State = FakeOllamaState.Stopped;

        await services.Settings.SetOllamaBaseUrlAsync("http://wrong-box:11434", ct);

        Assert.Equal("http://wrong-box:11434", (await services.Settings.GetAsync(ct)).OllamaBaseUrl);
        Assert.Equal(LogLevel.Warning, logger.LatestRecord.Level);
        Assert.Contains("model sync failed", logger.LatestRecord.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost:11434")]
    [InlineData("gpu-box")]
    [InlineData("/api")]
    [InlineData("ftp://gpu-box:11434")]
    [InlineData("file:///etc/passwd")]
    public async Task Rejects_a_url_that_is_not_absolute_http_or_https(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new TestServices(_database).Settings;
        var changes = 0;
        settings.SettingsChanged += () => changes++;

        var error = await Assert.ThrowsAsync<ValidationException>(() => settings.SetOllamaBaseUrlAsync(url, ct));

        Assert.Contains(nameof(AppSettingsSnapshot.OllamaBaseUrl), error.Errors.Keys);
        Assert.Null(await ReadRowAsync("Ollama.BaseUrl", ct));
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Rejects_an_unknown_or_unavailable_default_model()
    {
        var ct = TestContext.Current.CancellationToken;
        var unavailable = await AddModelAsync(isAvailable: false, ct);
        var settings = new TestServices(_database).Settings;

        var unknown = await Assert.ThrowsAsync<ValidationException>(() => settings.SetDefaultModelAsync(Guid.CreateVersion7(), ct));
        var notInstalled = await Assert.ThrowsAsync<ValidationException>(() => settings.SetDefaultModelAsync(unavailable.Id, ct));

        Assert.Contains(nameof(AppSettingsSnapshot.DefaultModelId), unknown.Errors.Keys);
        Assert.Contains(nameof(AppSettingsSnapshot.DefaultModelId), notInstalled.Errors.Keys);
        Assert.Null(await ReadRowAsync("Chat.DefaultModelId", ct));
    }

    [Fact]
    public async Task Seeding_warns_about_an_invalid_configured_url_and_stores_the_fallback()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = new FakeLogger<SettingsService>();
        var settings = new TestServices(_database, ollamaBaseUrl: "localhost:11434", settingsLogger: logger).Settings;

        await settings.SeedAsync(ct);

        Assert.Equal("http://localhost:11434", await ReadRowAsync("Ollama.BaseUrl", ct));
        Assert.Equal(LogLevel.Warning, logger.LatestRecord.Level);
    }

    private async Task AddRowAsync(string key, string value, CancellationToken ct)
    {
        await using var db = _database.CreateContext();
        db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = TestData.Now });
        await db.SaveChangesAsync(ct);
    }

    private async Task<string?> ReadRowAsync(string key, CancellationToken ct)
    {
        await using var db = _database.CreateContext();
        return await db.AppSettings.Where(s => s.Key == key).Select(s => s.Value).SingleOrDefaultAsync(ct);
    }

    private async Task<Model> AddModelAsync(bool isAvailable, CancellationToken ct)
    {
        await using var db = _database.CreateContext();
        var model = TestData.NewModel(isAvailable: isAvailable);
        db.Models.Add(model);
        await db.SaveChangesAsync(ct);
        return model;
    }
}
