using LocalChat.Web.Data;
using LocalChat.Web.Data.Entities;
using LocalChat.Web.Services;
using LocalChat.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LocalChat.Web.Tests.Services;

public sealed class ConversationServiceTests : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private TestServices _services = null!;
    private ModelDto _llama = null!;
    private ModelDto _phi = null!;

    private ConversationService Conversations => _services.Conversations;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _database = TestDatabase.CreateEmpty();
        _services = new TestServices(_database);
        _services.Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: ["completion"]));
        _services.Ollama.Models.Add(new FakeOllamaModel("llama3.2:3b", Capabilities: ["completion"], Architecture: "llama"));
        await _services.Seed.RunAsync(ct);
        var models = await _services.Models.ListAsync(includeUnavailable: false, ct);
        _llama = models.Single(m => m.Tag == "llama3.2:3b");
        _phi = models.Single(m => m.Tag == "phi4-mini:latest");
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Creating_without_a_model_uses_the_default_model()
    {
        var ct = TestContext.Current.CancellationToken;
        await _services.Settings.SetDefaultModelAsync(_phi.Id, ct);

        var created = await Conversations.CreateAsync(ProjectIds.General, modelId: null, ct);

        Assert.Equal(_phi.Id, created.ModelId);
        Assert.Equal("New chat", created.Title);
        Assert.Empty(created.Messages);
    }

    [Fact]
    public async Task Creating_without_a_model_or_a_usable_default_uses_the_first_available_by_display_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await _services.Settings.SetDefaultModelAsync(null, ct);
        var unset = await Conversations.CreateAsync(ProjectIds.General, null, ct);

        await _services.Settings.SetDefaultModelAsync(_phi.Id, ct);
        await MarkUnavailableAsync(_phi.Id, ct);
        var unavailableDefault = await Conversations.CreateAsync(ProjectIds.General, null, ct);

        Assert.Equal(_llama.Id, unset.ModelId);
        Assert.Equal(_llama.Id, unavailableDefault.ModelId);
    }

    [Fact]
    public async Task Creating_with_no_available_model_throws_Validation()
    {
        var ct = TestContext.Current.CancellationToken;
        await MarkUnavailableAsync(_phi.Id, ct);
        await MarkUnavailableAsync(_llama.Id, ct);

        var error = await Assert.ThrowsAsync<ValidationException>(() => Conversations.CreateAsync(ProjectIds.General, null, ct));

        Assert.Contains("ModelId", error.Errors.Keys);
    }

    [Fact]
    public async Task Creating_with_a_given_model_uses_it_and_rejects_an_unavailable_one()
    {
        var ct = TestContext.Current.CancellationToken;

        var created = await Conversations.CreateAsync(ProjectIds.General, _phi.Id, ct);
        await MarkUnavailableAsync(_llama.Id, ct);

        Assert.Equal(_phi.Id, created.ModelId);
        await Assert.ThrowsAsync<ValidationException>(() => Conversations.CreateAsync(ProjectIds.General, _llama.Id, ct));
        await Assert.ThrowsAsync<ValidationException>(() => Conversations.CreateAsync(ProjectIds.General, Guid.CreateVersion7(), ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Conversations.CreateAsync(Guid.CreateVersion7(), _phi.Id, ct));
    }

    [Fact]
    public async Task Lists_newest_first_and_renames_and_model_changes_bump_UpdatedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await CreateAtNextMinuteAsync(ct);
        var second = await CreateAtNextMinuteAsync(ct);
        var third = await CreateAtNextMinuteAsync(ct);
        Assert.Equal([third.Id, second.Id, first.Id], await ListIdsAsync(ct));

        _services.Time.Advance(TimeSpan.FromMinutes(1));
        await Conversations.RenameAsync(first.Id, "Trip planning", ct);
        Assert.Equal([first.Id, third.Id, second.Id], await ListIdsAsync(ct));

        _services.Time.Advance(TimeSpan.FromMinutes(1));
        await Conversations.SetModelAsync(second.Id, _phi.Id, ct);
        Assert.Equal([second.Id, first.Id, third.Id], await ListIdsAsync(ct));

        var list = await Conversations.ListAsync(ProjectIds.General, ct);
        Assert.Equal(_services.Time.GetUtcNow().UtcDateTime, list[0].UpdatedAt);
        Assert.Equal("Trip planning", list[1].Title);
        Assert.Equal(_phi.Id, list[0].ModelId);
    }

    [Fact]
    public async Task Switching_to_an_unavailable_or_unknown_model_throws_Validation()
    {
        var ct = TestContext.Current.CancellationToken;
        var conversation = await Conversations.CreateAsync(ProjectIds.General, _llama.Id, ct);
        await MarkUnavailableAsync(_phi.Id, ct);

        var unavailable = await Assert.ThrowsAsync<ValidationException>(() => Conversations.SetModelAsync(conversation.Id, _phi.Id, ct));
        await Assert.ThrowsAsync<ValidationException>(() => Conversations.SetModelAsync(conversation.Id, Guid.CreateVersion7(), ct));

        Assert.Contains("ModelId", unavailable.Errors.Keys);
        Assert.Equal(_llama.Id, (await Conversations.GetAsync(conversation.Id, ct)).ModelId);
    }

    [Fact]
    public async Task Get_returns_the_messages_in_sequence_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var conversation = await Conversations.CreateAsync(ProjectIds.General, _llama.Id, ct);
        await using (var db = _database.CreateContext())
        {
            db.Messages.AddRange(
                TestData.NewMessage(conversation.Id, 3, content: "third"),
                TestData.NewMessage(conversation.Id, 1, content: "first"),
                TestData.NewMessage(conversation.Id, 2, MessageRole.Assistant, content: "second"));
            await db.SaveChangesAsync(ct);
        }

        var detail = await Conversations.GetAsync(conversation.Id, ct);

        Assert.Equal(["first", "second", "third"], detail.Messages.Select(m => m.Content));
        Assert.Equal([1, 2, 3], detail.Messages.Select(m => m.Sequence));
        Assert.Equal(MessageRole.Assistant, detail.Messages[1].Role);
    }

    [Theory]
    [InlineData("   ", "Enter a title.")]
    [InlineData(null, "Use at most 200 characters.")]
    public async Task Rejects_an_empty_or_long_title(string? title, string error)
    {
        var ct = TestContext.Current.CancellationToken;
        var conversation = await Conversations.CreateAsync(ProjectIds.General, null, ct);

        var thrown = await Assert.ThrowsAsync<ValidationException>(
            () => Conversations.RenameAsync(conversation.Id, title ?? new string('t', 201), ct));

        Assert.Equal(error, thrown.Errors[nameof(ConversationDetail.Title)]);
    }

    [Fact]
    public async Task Sets_and_clears_the_system_prompt()
    {
        var ct = TestContext.Current.CancellationToken;
        var conversation = await Conversations.CreateAsync(ProjectIds.General, null, ct);

        await Conversations.SetSystemPromptAsync(conversation.Id, "  Answer in French.  ", ct);
        var set = await Conversations.GetAsync(conversation.Id, ct);
        await Conversations.SetSystemPromptAsync(conversation.Id, " ", ct);
        var cleared = await Conversations.GetAsync(conversation.Id, ct);

        Assert.Equal("Answer in French.", set.SystemPrompt);
        Assert.Null(cleared.SystemPrompt);
    }

    [Fact]
    public async Task Deletes_a_conversation_with_its_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        var conversation = await Conversations.CreateAsync(ProjectIds.General, null, ct);
        await using (var db = _database.CreateContext())
        {
            db.Messages.Add(TestData.NewMessage(conversation.Id, 1));
            await db.SaveChangesAsync(ct);
        }

        await Conversations.DeleteAsync(conversation.Id, ct);

        await Assert.ThrowsAsync<NotFoundException>(() => Conversations.GetAsync(conversation.Id, ct));
        await using var check = _database.CreateContext();
        Assert.Empty(await check.Messages.ToListAsync(ct));
    }

    [Fact]
    public async Task Writes_raise_ConversationsChanged_for_the_project()
    {
        var ct = TestContext.Current.CancellationToken;
        var changed = new List<Guid>();
        _services.Notifier.ConversationsChanged += changed.Add;

        var conversation = await Conversations.CreateAsync(ProjectIds.General, null, ct);
        await Conversations.RenameAsync(conversation.Id, "Renamed", ct);
        await Conversations.RenameAsync(conversation.Id, "Renamed", ct); // no change, no event
        await Conversations.SetModelAsync(conversation.Id, _phi.Id, ct);
        await Conversations.SetSystemPromptAsync(conversation.Id, "Be brief.", ct);
        await Conversations.DeleteAsync(conversation.Id, ct);

        Assert.Equal(Enumerable.Repeat(ProjectIds.General, 5), changed);
    }

    [Fact]
    public async Task Unknown_ids_throw_NotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var unknown = Guid.CreateVersion7();

        await Assert.ThrowsAsync<NotFoundException>(() => Conversations.ListAsync(unknown, ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Conversations.GetAsync(unknown, ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Conversations.RenameAsync(unknown, "Title", ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Conversations.SetModelAsync(unknown, _phi.Id, ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Conversations.SetSystemPromptAsync(unknown, null, ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Conversations.DeleteAsync(unknown, ct));
    }

    private async Task<ConversationDetail> CreateAtNextMinuteAsync(CancellationToken ct)
    {
        _services.Time.Advance(TimeSpan.FromMinutes(1));
        return await Conversations.CreateAsync(ProjectIds.General, _llama.Id, ct);
    }

    private async Task<List<Guid>> ListIdsAsync(CancellationToken ct) =>
        (await Conversations.ListAsync(ProjectIds.General, ct)).Select(c => c.Id).ToList();

    private async Task MarkUnavailableAsync(Guid modelId, CancellationToken ct)
    {
        await using var db = _database.CreateContext();
        await db.Models.Where(m => m.Id == modelId).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsAvailable, false), ct);
    }
}
