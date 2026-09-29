using Tiel.Web.Components;
using Tiel.Web.Components.Layout;
using Tiel.Web.Components.Shared;
using Tiel.Web.Data;
using Tiel.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.FluentUI.AspNetCore.Components;

// The app's own folder, not the working directory, so a published build finds appsettings.json and
// wwwroot wherever it is started from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContextFactory<AppDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Tiel")
        ?? throw new InvalidOperationException("The connection string 'Tiel' is missing.");
    var localApplicationData = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
    var logger = services.GetRequiredService<ILogger<AppDbContext>>();
    options.UseSqlite(SqliteConnectionStrings.Resolve(connectionString, localApplicationData, logger));
});
builder.Services.AddSingleton<Seed>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ISettingsService>(services => services.GetRequiredService<SettingsService>());
builder.Services.AddSingleton<IOllamaClientProvider, OllamaClientProvider>();
builder.Services.AddSingleton<IHealthService, HealthService>();
builder.Services.AddSingleton<IModelSyncService, ModelSyncService>();
builder.Services.AddSingleton(services => new Lazy<IModelSyncService>(services.GetRequiredService<IModelSyncService>));
builder.Services.AddSingleton<IModelService, ModelService>();
builder.Services.AddSingleton<ChangeNotifier>();
builder.Services.AddSingleton<IProjectService, ProjectService>();
builder.Services.AddSingleton<IConversationService, ConversationService>();
builder.Services.AddSingleton<ITitleService, TitleService>();
builder.Services.AddSingleton<GenerationService>();
builder.Services.AddSingleton<IGenerationService>(services => services.GetRequiredService<GenerationService>());
builder.Services.AddHostedService(services => services.GetRequiredService<GenerationService>());
builder.Services.AddScoped<ProjectContext>();
builder.Services.AddScoped<Interop>();

var app = builder.Build();

await app.Services.GetRequiredService<Seed>().RunAsync(app.Lifetime.ApplicationStopping);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();

app.MapGet("/api/health", (IHealthService health, CancellationToken ct) => health.CheckAsync(ct));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
