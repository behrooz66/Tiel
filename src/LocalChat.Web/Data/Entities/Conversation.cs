namespace LocalChat.Web.Data.Entities;

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProjectId { get; set; }
    public Guid ModelId { get; set; }
    public required string Title { get; set; }
    public string? SystemPrompt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public Model Model { get; set; } = null!;
    public List<Message> Messages { get; set; } = [];
}
