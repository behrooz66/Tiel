using LocalChat.Web.Data.Entities;
using LocalChat.Web.Services;
using LocalChat.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LocalChat.Web.Tests.Services;

public sealed class SettingsServiceTests : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Missing_rows_return_the_defaults()
    {
        var settings = TestServices.Settings(_database, ollamaBaseUrl: "http://gpu-box:11434/");

        var snapshot = await settings.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new AppSettingsSnapshot("http://gpu-box:11434", null), snapshot);
    }

    [Fact]
    public async Task Missing_configuration_falls_back_to_the_local_ollama()
    {
        var settings = TestServices.Settings(_database, ollamaBaseUrl: null);

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
        var settings = TestServices.Settings(_database, logger);

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
        var settings = TestServices.Settings(_database);
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
        var settings = TestServices.Settings(_database);
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
        var settings = TestServices.Settings(_database);

        await settings.SetOllamaBaseUrlAsync("  https://gpu-box:11434/ ", ct);

        Assert.Equal("https://gpu-box:11434", await ReadRowAsync("Ollama.BaseUrl", ct));
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
        var settings = TestServices.Settings(_database);
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
        var settings = TestServices.Settings(_database);

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
        var settings = TestServices.Settings(_database, logger, ollamaBaseUrl: "localhost:11434");

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
