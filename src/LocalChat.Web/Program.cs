using LocalChat.Web.Components;
using LocalChat.Web.Data;
using LocalChat.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContextFactory<AppDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("LocalChat")
        ?? throw new InvalidOperationException("The connection string 'LocalChat' is missing.");
    var localApplicationData = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
    options.UseSqlite(SqliteConnectionStrings.Resolve(connectionString, localApplicationData));
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
