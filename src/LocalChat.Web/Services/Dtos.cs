namespace LocalChat.Web.Services;

public record AppSettingsSnapshot(string OllamaBaseUrl, Guid? DefaultModelId);

public record HealthStatus(bool OllamaReachable, string? OllamaVersion, string OllamaBaseUrl);

public record ConnectionTest(bool Ok, string? Version, string? Error);

public record ModelDto(Guid Id, string Tag, string DisplayName, int ContextLength, int? MaxContextLength, bool IsAvailable);

public record SyncResult(int Added, int Updated, int MarkedUnavailable);

public record ProjectInput(string Name, string? Description, string? Instructions);

public record ProjectDto(Guid Id, string Name, string? Description, string? Instructions,
    bool IsGeneral, int ConversationCount, DateTime CreatedAt, DateTime UpdatedAt);
