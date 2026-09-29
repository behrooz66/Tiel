using Tiel.Web.Data;
using Tiel.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tiel.Web.Tests.Infrastructure;

/// <summary>
/// The real services over a test database, wired by hand instead of by the app's DI container.
/// Ollama calls go through the real <see cref="OllamaClientProvider"/> to a <see cref="FakeOllama"/>;
/// chat calls go to <see cref="Chat"/> unless <c>fakeChat</c> is false.
/// </summary>
public sealed class TestServices
{
    public TestServices(
        TestDatabase database,
        FakeOllama? ollama = null,
        string? ollamaBaseUrl = "http://localhost:11434",
        ILogger<SettingsService>? settingsLogger = null,
        bool fakeChat = true)
    {
        Ollama = ollama ?? new FakeOllama();
        Settings = new SettingsService(
            database.Factory,
            new Lazy<IModelSyncService>(() => ModelSync!), // assigned below, before first use
            Configuration(ollamaBaseUrl),
            Time,
            settingsLogger ?? NullLogger<SettingsService>.Instance);
        var ollamaClients = new OllamaClientProvider(Settings, Ollama);
        Clients = fakeChat ? new ChatClientOverride(ollamaClients, Chat) : ollamaClients;
        ModelSync = new ModelSyncService(database.Factory, Clients, Settings, Time, NullLogger<ModelSyncService>.Instance)
        {
            Timeout = TimeSpan.FromMilliseconds(500),
        };
        Models = new ModelService(database.Factory, ModelSync, Time);
        Seed = new Seed(database.Factory, Settings, ModelSync, Time, NullLogger<Seed>.Instance);
        Notifier = new ChangeNotifier(NullLogger<ChangeNotifier>.Instance);
        Titles = new TitleService(Clients, NullLogger<TitleService>.Instance) { Timeout = TimeSpan.FromSeconds(2) };
        Generation = new GenerationService(
            database.Factory, Clients, Titles, Notifier, Time, Lifetime, NullLogger<GenerationService>.Instance);
        Projects = new ProjectService(database.Factory, Generation, Notifier, Time);
        Conversations = new ConversationService(database.Factory, Settings, Generation, Notifier, Time);
    }

    /// <summary>Starts at <see cref="TestData.Now"/> and moves only when a test advances it.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(TestData.Now));
    public FakeOllama Ollama { get; }
    public SettingsService Settings { get; }
    public FakeChatClient Chat { get; } = new();
    public IOllamaClientProvider Clients { get; }
    public ModelSyncService ModelSync { get; }
    public ModelService Models { get; }
    public Seed Seed { get; }
    public ChangeNotifier Notifier { get; }
    public ProjectService Projects { get; }
    public ConversationService Conversations { get; }
    public TitleService Titles { get; }
    public GenerationService Generation { get; }
    public ApplicationLifetime Lifetime { get; } = new(NullLogger<ApplicationLifetime>.Instance);

    public static IConfiguration Configuration(string? ollamaBaseUrl) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ollama:BaseUrl"] = ollamaBaseUrl })
            .Build();
}
