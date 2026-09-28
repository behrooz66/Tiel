using Bunit;
using LocalChat.Web.Components.Layout;
using LocalChat.Web.Components.Shared;
using LocalChat.Web.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LocalChat.Web.Tests.Infrastructure;

/// <summary>
/// A bUnit context wired to the real services over a seeded test database, one phi4-mini model,
/// a <see cref="FakeChatClient"/> and a <see cref="FakeHealthService"/>.
/// </summary>
public abstract class AppTestContext : BunitContext, IAsyncLifetime
{
    protected TestDatabase Database { get; private set; } = null!;
    protected TestServices App { get; private set; } = null!;
    protected FakeHealthService Health { get; } = new();

    public virtual async ValueTask InitializeAsync()
    {
        Database = TestDatabase.CreateEmpty();
        App = new TestServices(Database);
        App.Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: ["completion"]));
        await App.Seed.RunAsync(TestContext.Current.CancellationToken);

        Services.AddFluentUIComponents();
        Services.AddSingleton<IHealthService>(Health);
        Services.AddSingleton<ISettingsService>(App.Settings);
        Services.AddSingleton<IModelService>(App.Models);
        Services.AddSingleton<IProjectService>(App.Projects);
        Services.AddSingleton<IConversationService>(App.Conversations);
        Services.AddSingleton<IGenerationService>(App.Generation);
        Services.AddSingleton(App.Notifier);
        Services.AddSingleton<TimeProvider>(App.Time);
        Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<ProjectContext>();
        Services.AddScoped<Interop>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        await App.Generation.StopAsync(CancellationToken.None);
        await Database.DisposeAsync();
    }
}
