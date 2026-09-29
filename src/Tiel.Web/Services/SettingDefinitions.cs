namespace Tiel.Web.Services;

public delegate bool SettingParser<T>(string text, out T value);

/// <summary>One key of the AppSettings table: how its stored text is parsed, validated, formatted and defaulted.</summary>
public sealed class SettingDefinition<T>
{
    public required string Key { get; init; }

    /// <summary>Converts stored text to a value; returns false when the text is not a valid value.</summary>
    public required SettingParser<T> TryParse { get; init; }

    /// <summary>Converts a valid value to the stored text (invariant culture).</summary>
    public required Func<T, string> Format { get; init; }

    /// <summary>Returns an error message when the value cannot be stored, or null when it can.</summary>
    public Func<T, string?> Validate { get; init; } = _ => null;

    /// <summary>The value used when the row is missing or its text does not parse.</summary>
    public required Func<IConfiguration, T> GetDefault { get; init; }
}

/// <summary>Every known setting, defined once. A new setting is a new entry here, never a new column.</summary>
public static class SettingDefinitions
{
    public const string OllamaBaseUrlConfigurationKey = "Ollama:BaseUrl";
    public const string FallbackOllamaBaseUrl = "http://localhost:11434";

    public static readonly SettingDefinition<string> OllamaBaseUrl = new()
    {
        Key = "Ollama.BaseUrl",
        TryParse = (string text, out string value) =>
        {
            value = NormalizeUrl(text);
            return ValidateUrl(value) is null;
        },
        Format = value => value,
        Validate = ValidateUrl,
        GetDefault = configuration =>
            configuration[OllamaBaseUrlConfigurationKey] is { } configured && ValidateUrl(NormalizeUrl(configured)) is null
                ? NormalizeUrl(configured)
                : FallbackOllamaBaseUrl,
    };

    public static readonly SettingDefinition<Guid?> DefaultModelId = new()
    {
        Key = "Chat.DefaultModelId",
        TryParse = (string text, out Guid? value) =>
        {
            var parsed = Guid.TryParseExact(text, "D", out var id);
            value = parsed ? id : null;
            return parsed;
        },
        Format = value => value?.ToString("D") ?? "",
        GetDefault = _ => null,
    };

    /// <summary>Trims whitespace and trailing slashes, so equal URLs compare equal.</summary>
    public static string NormalizeUrl(string url) => url.Trim().TrimEnd('/');

    private static string? ValidateUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? null
            : "Enter an absolute http or https URL, for example http://localhost:11434.";
}
