namespace LocalChat.Web.Services;

public record AppSettingsSnapshot(string OllamaBaseUrl, Guid? DefaultModelId);

public record HealthStatus(bool OllamaReachable, string? OllamaVersion, string OllamaBaseUrl);

public record ConnectionTest(bool Ok, string? Version, string? Error);
