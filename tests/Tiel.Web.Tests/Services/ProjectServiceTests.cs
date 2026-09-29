using Tiel.Web.Data;
using Tiel.Web.Services;
using Tiel.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tiel.Web.Tests.Services;

public sealed class ProjectServiceTests : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private TestServices _services = null!;
    private int _projectsChanged;

    private ProjectService Projects => _services.Projects;

    public async ValueTask InitializeAsync()
    {
        _database = TestDatabase.CreateEmpty();
        _services = new TestServices(_database);
        await _services.Seed.RunAsync(TestContext.Current.CancellationToken);
        _services.Notifier.ProjectsChanged += () => _projectsChanged++;
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Creates_reads_updates_and_deletes_a_project()
    {
        var ct = TestContext.Current.CancellationToken;

        var created = await Projects.CreateAsync(new ProjectInput("  Work  ", "  Day job  ", "Answer briefly."), ct);
        Assert.Equal("Work", created.Name);
        Assert.Equal("Day job", created.Description);
        Assert.Equal("Answer briefly.", created.Instructions);
        Assert.False(created.IsGeneral);
        Assert.Equal(created, await Projects.GetAsync(created.Id, ct));

        var updated = await Projects.UpdateAsync(created.Id, new ProjectInput("Office", " ", null), ct);
        Assert.Equal(("Office", (string?)null, (string?)null), (updated.Name, updated.Description, updated.Instructions));
        Assert.Equal(updated, await Projects.GetAsync(created.Id, ct));

        await Projects.DeleteAsync(created.Id, ct);
        await Assert.ThrowsAsync<NotFoundException>(() => Projects.GetAsync(created.Id, ct));
        Assert.Equal(3, _projectsChanged);
    }

    [Fact]
    public async Task Lists_General_first_then_by_name_ignoring_case()
    {
        var ct = TestContext.Current.CancellationToken;
        foreach (var name in new[] { "zeta", "Alpha", "beta" })
        {
            await Projects.CreateAsync(new ProjectInput(name, null, null), ct);
        }

        var projects = await Projects.ListAsync(ct);

        Assert.Equal(["General", "Alpha", "beta", "zeta"], projects.Select(p => p.Name));
        Assert.True(projects[0].IsGeneral);
        Assert.Equal(ProjectIds.General, projects[0].Id);
    }

    [Fact]
    public async Task Counts_each_projects_conversations()
    {
        var ct = TestContext.Current.CancellationToken;
        var work = await Projects.CreateAsync(new ProjectInput("Work", null, null), ct);
        await using (var db = _database.CreateContext())
        {
            var model = TestData.NewModel();
            db.Models.Add(model);
            db.Conversations.AddRange(
                TestData.NewConversation(work.Id, model.Id),
                TestData.NewConversation(work.Id, model.Id),
                TestData.NewConversation(ProjectIds.General, model.Id));
            await db.SaveChangesAsync(ct);
        }

        var projects = await Projects.ListAsync(ct);

        Assert.Equal([1, 2], projects.Select(p => p.ConversationCount));
        Assert.Equal(2, (await Projects.GetAsync(work.Id, ct)).ConversationCount);
    }

    [Fact]
    public async Task Duplicate_names_conflict_regardless_of_case()
    {
        var ct = TestContext.Current.CancellationToken;
        var work = await Projects.CreateAsync(new ProjectInput("Work", null, null), ct);
        var home = await Projects.CreateAsync(new ProjectInput("Home", null, null), ct);

        await Assert.ThrowsAsync<ConflictException>(() => Projects.CreateAsync(new ProjectInput("WORK", null, null), ct));
        await Assert.ThrowsAsync<ConflictException>(() => Projects.CreateAsync(new ProjectInput("general", null, null), ct));
        await Assert.ThrowsAsync<ConflictException>(() => Projects.UpdateAsync(home.Id, new ProjectInput("work", null, null), ct));

        // A project may change the case of its own name.
        Assert.Equal("WORK", (await Projects.UpdateAsync(work.Id, new ProjectInput("WORK", null, null), ct)).Name);
    }

    [Fact]
    public async Task General_cannot_be_deleted_but_can_be_renamed()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ConflictException>(() => Projects.DeleteAsync(ProjectIds.General, ct));
        var renamed = await Projects.UpdateAsync(ProjectIds.General, new ProjectInput("Everyday", null, "Be concise."), ct);

        Assert.Equal("Everyday", renamed.Name);
        Assert.True(renamed.IsGeneral);
    }

    [Fact]
    public async Task Deleting_a_project_deletes_its_conversations_and_raises_both_events()
    {
        var ct = TestContext.Current.CancellationToken;
        var work = await Projects.CreateAsync(new ProjectInput("Work", null, null), ct);
        await using (var db = _database.CreateContext())
        {
            var model = TestData.NewModel();
            var conversation = TestData.NewConversation(work.Id, model.Id);
            db.AddRange(model, conversation, TestData.NewMessage(conversation.Id, 1));
            await db.SaveChangesAsync(ct);
        }

        var changedProjects = new List<Guid>();
        _services.Notifier.ConversationsChanged += changedProjects.Add;

        await Projects.DeleteAsync(work.Id, ct);

        await using var check = _database.CreateContext();
        Assert.Empty(await check.Conversations.ToListAsync(ct));
        Assert.Empty(await check.Messages.ToListAsync(ct));
        Assert.Equal([work.Id], changedProjects);
    }

    [Fact]
    public async Task Rejects_invalid_input_with_field_errors()
    {
        var ct = TestContext.Current.CancellationToken;

        var empty = await Assert.ThrowsAsync<ValidationException>(
            () => Projects.CreateAsync(new ProjectInput("   ", new string('d', 501), null), ct));
        var tooLong = await Assert.ThrowsAsync<ValidationException>(
            () => Projects.CreateAsync(new ProjectInput(new string('n', 101), null, null), ct));

        Assert.Equal("Enter a project name.", empty.Errors[nameof(ProjectInput.Name)]);
        Assert.Equal("Use at most 500 characters.", empty.Errors[nameof(ProjectInput.Description)]);
        Assert.Equal("Use at most 100 characters.", tooLong.Errors[nameof(ProjectInput.Name)]);
        Assert.Single(await Projects.ListAsync(ct));
        Assert.Equal(0, _projectsChanged);
    }

    [Fact]
    public async Task Unknown_ids_throw_NotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var unknown = Guid.CreateVersion7();

        await Assert.ThrowsAsync<NotFoundException>(() => Projects.GetAsync(unknown, ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Projects.UpdateAsync(unknown, new ProjectInput("X", null, null), ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Projects.DeleteAsync(unknown, ct));
    }

    [Fact]
    public async Task An_update_that_changes_nothing_raises_no_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var work = await Projects.CreateAsync(new ProjectInput("Work", "Desc", null), ct);

        var same = await Projects.UpdateAsync(work.Id, new ProjectInput("Work", "Desc", null), ct);

        Assert.Equal(work.UpdatedAt, same.UpdatedAt);
        Assert.Equal(1, _projectsChanged);
    }
}
