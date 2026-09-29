using Tiel.Web.Data.Entities;

namespace Tiel.Web.Services;

public record AppSettingsSnapshot(string OllamaBaseUrl, Guid? DefaultModelId);

public record HealthStatus(bool OllamaReachable, string? OllamaVersion, string OllamaBaseUrl);

public record ConnectionTest(bool Ok, string? Version, string? Error);

public record ModelDto(Guid Id, string Tag, string DisplayName, int ContextLength, int? MaxContextLength, bool IsAvailable);

public record SyncResult(int Added, int Updated, int MarkedUnavailable);

public record ProjectInput(string Name, string? Description, string? Instructions);

public record ProjectDto(Guid Id, string Name, string? Description, string? Instructions,
    bool IsGeneral, int ConversationCount, DateTime CreatedAt, DateTime UpdatedAt);

public record ConversationSummary(Guid Id, Guid ProjectId, string Title, Guid ModelId, DateTime UpdatedAt);

public record ConversationDetail(Guid Id, Guid ProjectId, string Title, Guid ModelId, string? SystemPrompt,
    DateTime CreatedAt, DateTime UpdatedAt, bool IsGenerating, IReadOnlyList<MessageDto> Messages);

public record MessageDto(Guid Id, int Sequence, MessageRole Role, string Content, MessageStatus Status,
    string? ErrorMessage, Guid? ModelId, int? TokenCount, DateTime CreatedAt);

public record TurnStarted(MessageDto? UserMessage, MessageDto AssistantMessage);
