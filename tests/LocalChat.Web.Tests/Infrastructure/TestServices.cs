using LocalChat.Web.Data;
using LocalChat.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LocalChat.Web.Tests.Infrastructure;

/// <summary>
/// The real services over a test database, wired by hand instead of by the app's DI container.
/// Ollama calls go through the real <see cref="OllamaClientProvider"/> to a <see cref="FakeOllama"/>.
/// </summary>
public sealed class TestServices
{
    public TestServices(
        TestDatabase database,
        FakeOllama? ollama = null,
        string? ollamaBaseUrl = "http://localhost:11434",
        ILogger<SettingsService>? settingsLogger = null)
    {
        Ollama = ollama ?? new FakeOllama();
        Settings = new SettingsService(
            database.Factory,
            new Lazy<IModelSyncService>(() => ModelSync!), // assigned below, before first use
            Configuration(ollamaBaseUrl),
            Time,
            settingsLogger ?? NullLogger<SettingsService>.Instance);
        Clients = new OllamaClientProvider(Settings, Ollama);
        ModelSync = new ModelSyncService(database.Factory, Clients, Settings, Time, NullLogger<ModelSyncService>.Instance)
        {
            Timeout = TimeSpan.FromMilliseconds(500),
        };
        Models = new ModelService(database.Factory, ModelSync, Time);
        Seed = new Seed(database.Factory, Settings, ModelSync, Time, NullLogger<Seed>.Instance);
        Notifier = new ChangeNotifier(NullLogger<ChangeNotifier>.Instance);
        Projects = new ProjectService(database.Factory, Notifier, Time);
        Conversations = new ConversationService(database.Factory, Settings, Notifier, Time);
    }

    /// <summary>Starts at <see cref="TestData.Now"/> and moves only when a test advances it.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(TestData.Now));
    public FakeOllama Ollama { get; }
    public SettingsService Settings { get; }
    public OllamaClientProvider Clients { get; }
    public ModelSyncService ModelSync { get; }
    public ModelService Models { get; }
    public Seed Seed { get; }
    public ChangeNotifier Notifier { get; }
    public ProjectService Projects { get; }
    public ConversationService Conversations { get; }

    public static IConfiguration Configuration(string? ollamaBaseUrl) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ollama:BaseUrl"] = ollamaBaseUrl })
            .Build();
}
