using System.Security.Cryptography;
using Tiel.Web.Data;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace Tiel.Web.Components.Layout;

/// <summary>
/// The project the sidebar shows, for one browser tab (scoped to its circuit). The last one is remembered
/// in the browser's local storage, so <c>/</c> reopens it.
/// </summary>
public sealed class ProjectContext(ProtectedLocalStorage storage, ILogger<ProjectContext> logger)
{
    private const string StorageKey = "tiel.lastProject";

    public Guid ProjectId { get; private set; } = ProjectIds.General;

    public event Action? Changed;

    public async Task SetAsync(Guid projectId)
    {
        if (projectId != ProjectId)
        {
            ProjectId = projectId;
            Changed?.Invoke();
        }

        try
        {
            await storage.SetAsync(StorageKey, projectId);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Could not remember the last project in the browser.");
        }
    }

    /// <summary>The last project this browser used, or null when there is none (or it cannot be read).</summary>
    public async Task<Guid?> GetRememberedAsync()
    {
        try
        {
            var stored = await storage.GetAsync<Guid>(StorageKey);
            return stored.Success ? stored.Value : null;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException
            or InvalidOperationException or CryptographicException)
        {
            logger.LogDebug(ex, "Could not read the last project from the browser.");
            return null;
        }
    }
}
