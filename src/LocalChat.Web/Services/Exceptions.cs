using System.Text.Json;

namespace LocalChat.Web.Services;

/// <summary>Input broke a rule. <see cref="Errors"/> maps each field to its message, for inline display.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string field, string error)
        : this(new Dictionary<string, string> { [field] = error })
    {
    }

    public ValidationException(IReadOnlyDictionary<string, string> errors)
        : base(string.Join(" ", errors.Values))
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string> Errors { get; }
}

/// <summary>An unknown project, conversation or model id.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Ollama could not be reached, timed out, or did not answer like Ollama.</summary>
public sealed class OllamaUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>A short, human-readable reason for a failed Ollama call.</summary>
    public static string Describe(Exception exception) =>
        exception is JsonException ? "The server at this URL did not answer like Ollama." : exception.Message;
}

/// <summary>The request conflicts with the current state: a duplicate name, a protected record, or work in progress.</summary>
public sealed class ConflictException(string message) : Exception(message);
