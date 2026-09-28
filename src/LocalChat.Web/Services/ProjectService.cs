using System.Linq.Expressions;
using LocalChat.Web.Data;
using LocalChat.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LocalChat.Web.Services;

public interface IProjectService
{
    /// <summary>General first, then by name.</summary>
    Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct);

    Task<ProjectDto> GetAsync(Guid id, CancellationToken ct);

    Task<ProjectDto> CreateAsync(ProjectInput input, CancellationToken ct);

    Task<ProjectDto> UpdateAsync(Guid id, ProjectInput input, CancellationToken ct);

    /// <summary>Stops active generations in its chats first. The General project cannot be deleted.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class ProjectService(
    IDbContextFactory<AppDbContext> dbFactory,
    ChangeNotifier notifier,
    TimeProvider time) : IProjectService
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;

    private static readonly Expression<Func<Project, ProjectDto>> ToDto = p => new ProjectDto(
        p.Id, p.Name, p.Description, p.Instructions, p.Id == ProjectIds.General,
        p.Conversations.Count, p.CreatedAt, p.UpdatedAt);

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Name has COLLATE NOCASE, so this order ignores case.
        return await db.Projects
            .OrderBy(p => p.Id != ProjectIds.General)
            .ThenBy(p => p.Name)
            .Select(ToDto)
            .ToListAsync(ct);
    }

    public async Task<ProjectDto> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Projects.Where(p => p.Id == id).Select(ToDto).SingleOrDefaultAsync(ct)
            ?? throw NotFound();
    }

    public async Task<ProjectDto> CreateAsync(ProjectInput input, CancellationToken ct)
    {
        var valid = Validate(input);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureNameIsFreeAsync(db, valid.Name, exceptId: null, ct);

        var now = time.GetUtcNow().UtcDateTime;
        var project = new Project
        {
            Name = valid.Name,
            Description = valid.Description,
            Instructions = valid.Instructions,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        await SaveAsync(db, valid.Name, ct);

        notifier.NotifyProjectsChanged();
        return new ProjectDto(project.Id, project.Name, project.Description, project.Instructions,
            IsGeneral: false, ConversationCount: 0, project.CreatedAt, project.UpdatedAt);
    }

    public async Task<ProjectDto> UpdateAsync(Guid id, ProjectInput input, CancellationToken ct)
    {
        var valid = Validate(input);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        await EnsureNameIsFreeAsync(db, valid.Name, exceptId: id, ct);

        project.Name = valid.Name;
        project.Description = valid.Description;
        project.Instructions = valid.Instructions;
        if (db.ChangeTracker.HasChanges())
        {
            project.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await SaveAsync(db, valid.Name, ct);
            notifier.NotifyProjectsChanged();
        }

        return await db.Projects.Where(p => p.Id == id).Select(ToDto).SingleAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (id == ProjectIds.General)
        {
            throw new ConflictException("The General project cannot be deleted.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // The database cascades the delete to the project's conversations and their messages.
        if (await db.Projects.Where(p => p.Id == id).ExecuteDeleteAsync(ct) == 0)
        {
            throw NotFound();
        }

        notifier.NotifyProjectsChanged();
        notifier.NotifyConversationsChanged(id);
    }

    private static (string Name, string? Description, string? Instructions) Validate(ProjectInput input)
    {
        var name = input.Name.Trim();
        var description = NullIfEmpty(input.Description);
        var errors = new Dictionary<string, string>();
        if (name.Length == 0)
        {
            errors[nameof(ProjectInput.Name)] = "Enter a project name.";
        }
        else if (name.Length > MaxNameLength)
        {
            errors[nameof(ProjectInput.Name)] = $"Use at most {MaxNameLength} characters.";
        }

        if (description?.Length > MaxDescriptionLength)
        {
            errors[nameof(ProjectInput.Description)] = $"Use at most {MaxDescriptionLength} characters.";
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return (name, description, NullIfEmpty(input.Instructions));
    }

    private static async Task EnsureNameIsFreeAsync(AppDbContext db, string name, Guid? exceptId, CancellationToken ct)
    {
        // The comparison uses the column's NOCASE collation.
        if (await db.Projects.AnyAsync(p => p.Name == name && p.Id != exceptId, ct))
        {
            throw DuplicateName(name);
        }
    }

    /// <summary>Saves, turning a unique-name violation from a concurrent write into the same conflict.</summary>
    private static async Task SaveAsync(AppDbContext db, string name, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.Ordinal) == true)
        {
            throw DuplicateName(name);
        }
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static ConflictException DuplicateName(string name) => new($"A project named '{name}' already exists.");

    private static NotFoundException NotFound() => new("The project does not exist.");
}
