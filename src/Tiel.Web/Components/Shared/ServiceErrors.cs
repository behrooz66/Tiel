using Tiel.Web.Services;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Tiel.Web.Components.Shared;

/// <summary>
/// Components catch the services' typed exceptions (<c>catch (Exception ex) when (ex.IsServiceError())</c>) and show
/// them as toasts. Anything else is a bug: it reaches the layout's error boundary, which logs it.
/// </summary>
public static class ServiceErrors
{
    public static bool IsServiceError(this Exception exception) =>
        exception is ValidationException or NotFoundException or ConflictException or OllamaUnavailableException;

    public static Task ShowErrorAsync(this INotificationService notifications, Exception exception)
    {
        var title = exception switch
        {
            ValidationException => "Check your input",
            NotFoundException => "Not found",
            OllamaUnavailableException => "Ollama is unavailable",
            _ => "That didn't work",
        };
        return notifications.ShowErrorToastAsync(title, exception.Message);
    }
}
