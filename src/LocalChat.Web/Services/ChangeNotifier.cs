namespace LocalChat.Web.Services;

/// <summary>
/// Raised by services after every write, so the sidebar and any other open tab refresh.
/// Handlers run on the writer's thread: components marshal with <c>InvokeAsync</c> and unsubscribe on dispose.
/// </summary>
public sealed class ChangeNotifier(ILogger<ChangeNotifier> logger)
{
    public event Action? ProjectsChanged;

    /// <summary>The conversations of the project with this id changed: added, renamed, reordered or deleted.</summary>
    public event Action<Guid>? ConversationsChanged;

    public void NotifyProjectsChanged() => Raise(ProjectsChanged, handler => ((Action)handler)());

    public void NotifyConversationsChanged(Guid projectId) =>
        Raise(ConversationsChanged, handler => ((Action<Guid>)handler)(projectId));

    // A failing subscriber must not fail the write that raised the event, or stop the other subscribers.
    private void Raise(Delegate? handlers, Action<Delegate> invoke)
    {
        foreach (var handler in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                invoke(handler);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A change subscriber failed.");
            }
        }
    }
}
