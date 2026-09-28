using LocalChat.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalChat.Web.Tests.Infrastructure;

/// <summary>Real services over a test database, built without the app's DI container.</summary>
public static class TestServices
{
    public static IConfiguration Configuration(string? ollamaBaseUrl = "http://localhost:11434") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ollama:BaseUrl"] = ollamaBaseUrl })
            .Build();

    public static SettingsService Settings(
        TestDatabase database,
        ILogger<SettingsService>? logger = null,
        string? ollamaBaseUrl = "http://localhost:11434") =>
        new(database.Factory, Configuration(ollamaBaseUrl), TimeProvider.System, logger ?? NullLogger<SettingsService>.Instance);
}
