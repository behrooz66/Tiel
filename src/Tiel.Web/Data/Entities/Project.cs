namespace Tiel.Web.Data.Entities;

public sealed class Project
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Conversation> Conversations { get; set; } = [];
}
