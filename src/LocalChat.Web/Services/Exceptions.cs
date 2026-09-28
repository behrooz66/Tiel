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
