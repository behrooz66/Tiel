using Microsoft.FluentUI.AspNetCore.Components;

namespace Tiel.Web.Components.Shared;

public static class FieldMessages
{
    /// <summary>
    /// Shows a Fluent field's message whenever one is set. Pass it as <c>MessageCondition</c>: the default condition of
    /// Fluent's text inputs replaces the message with its own "required" text and hides ours.
    /// </summary>
    public static readonly Func<IFluentField, bool> WhenSet = field => field.Message is not null;
}
