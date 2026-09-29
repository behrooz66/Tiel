namespace Tiel.Web.Data.Entities;

public sealed class Message
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ConversationId { get; set; }
    public Guid? ModelId { get; set; }
    public int Sequence { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = "";
    public MessageStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public int? TokenCount { get; set; }
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public Model? Model { get; set; }
}

public enum MessageRole
{
    User,
    Assistant,
}

public enum MessageStatus
{
    Streaming,
    Complete,
    Cancelled,
    Error,
}
