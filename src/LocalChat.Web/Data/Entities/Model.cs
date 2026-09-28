namespace LocalChat.Web.Data.Entities;

/// <summary>A model known to Ollama. Never deleted; <see cref="IsAvailable"/> is false once it is uninstalled.</summary>
public sealed class Model
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Tag { get; set; }
    public required string DisplayName { get; set; }
    public int ContextLength { get; set; }
    public int? MaxContextLength { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
