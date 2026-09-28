using LocalChat.Web.Data.Entities;

namespace LocalChat.Web.Tests.Infrastructure;

/// <summary>Entities with valid defaults, for arranging tests.</summary>
public static class TestData
{
    public static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    public static Model NewModel(string tag = "phi4-mini:latest", bool isAvailable = true) => new()
    {
        Tag = tag,
        DisplayName = tag,
        ContextLength = 4096,
        MaxContextLength = 131072,
        IsAvailable = isAvailable,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    public static Project NewProject(string name) => new() { Name = name, CreatedAt = Now, UpdatedAt = Now };

    public static Conversation NewConversation(Guid projectId, Guid modelId, string title = "New chat") => new()
    {
        ProjectId = projectId,
        ModelId = modelId,
        Title = title,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    public static Message NewMessage(
        Guid conversationId,
        int sequence,
        MessageRole role = MessageRole.User,
        MessageStatus status = MessageStatus.Complete,
        string content = "Hello") => new()
    {
        ConversationId = conversationId,
        Sequence = sequence,
        Role = role,
        Status = status,
        Content = content,
        CreatedAt = Now,
    };
}
