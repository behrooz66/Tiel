using Microsoft.AspNetCore.Components;

namespace LocalChat.Web.Components.Shared;

/// <summary>A component whose service calls stop when it goes away: pass <see cref="DisposeToken"/> to them.</summary>
public abstract class CancellableComponent : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _disposed = new();

    /// <summary>Cancelled when the component is disposed.</summary>
    protected CancellationToken DisposeToken => _disposed.Token;

    protected bool IsDisposed => _disposed.IsCancellationRequested;

    public void Dispose()
    {
        if (!_disposed.IsCancellationRequested)
        {
            OnDispose();
            _disposed.Cancel();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Unsubscribe from events here.</summary>
    protected virtual void OnDispose()
    {
    }
}
