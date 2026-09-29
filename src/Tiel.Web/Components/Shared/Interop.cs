using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tiel.Web.Components.Shared;

/// <summary>
/// The app's JavaScript module (<c>wwwroot/js/interop.js</c>), imported once per circuit. Calls made after the
/// browser went away are ignored.
/// </summary>
public sealed class Interop(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    public ValueTask HighlightCodeBlocksAsync(ElementReference container) => InvokeVoidAsync("highlightCodeBlocks", container);

    public ValueTask FollowIfPinnedAsync(ElementReference element) => InvokeVoidAsync("followIfPinned", element);

    public ValueTask ScrollToBottomAsync(ElementReference element) => InvokeVoidAsync("scrollToBottom", element);

    /// <summary>Starts reporting whether <paramref name="element"/> is scrolled to the bottom; dispose the result to stop.</summary>
    public ValueTask<IJSObjectReference?> AttachScrollAsync<T>(ElementReference element, DotNetObjectReference<T> target) where T : class =>
        InvokeAsync("attachScroll", element, target);

    /// <summary>Makes Enter call <c>SendFromKeyboard</c> on <paramref name="target"/>; dispose the result to stop.</summary>
    public ValueTask<IJSObjectReference?> AttachComposerAsync<T>(ElementReference textarea, DotNetObjectReference<T> target) where T : class =>
        InvokeAsync("attachComposer", textarea, target);

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await (await _module).DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/interop.js").AsTask();

    private async ValueTask InvokeVoidAsync(string identifier, params object?[] args)
    {
        try
        {
            await (await Module).InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException)
        {
        }
    }

    private async ValueTask<IJSObjectReference?> InvokeAsync(string identifier, params object?[] args)
    {
        try
        {
            return await (await Module).InvokeAsync<IJSObjectReference>(identifier, args);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException)
        {
            return null;
        }
    }
}
