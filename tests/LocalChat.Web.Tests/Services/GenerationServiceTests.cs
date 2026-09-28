using System.Text.Json;
using LocalChat.Web.Data;
using LocalChat.Web.Data.Entities;
using LocalChat.Web.Services;
using LocalChat.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LocalChat.Web.Tests.Services;

public sealed class GenerationServiceTests : IAsyncLifetime
{
    private readonly List<GenerationSubscription> _subscriptions = [];
    private TestDatabase _database = null!;
    private TestServices _services = null!;
    private ConversationDetail _conversation = null!;

    private GenerationService Generation => _services.Generation;
    private FakeChatClient Chat => _services.Chat;

    public async ValueTask InitializeAsync() => await SetUpAsync(fakeChat: true);

    public async ValueTask DisposeAsync()
    {
        _subscriptions.ForEach(s => s.Dispose());
        await _services.Generation.StopAsync(CancellationToken.None);
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Events_arrive_in_order_and_the_reply_ends_complete_then_the_chat_gets_a_title()
    {
        var ct = TestContext.Current.CancellationToken;
        var recorder = SubscribeFromTheStart(_conversation.Id);

        var turn = await Generation.SendAsync(_conversation.Id, "  Hi there  ", null, ct);
        await recorder.WaitForAsync<TitleGenerated>();

        Assert.Equal(
            ["GenerationStarted", "GenerationDelta", "GenerationDelta", "GenerationDelta", "GenerationCompleted", "TitleGenerated"],
            recorder.Events.Select(e => e.GetType().Name));
        var started = Assert.IsType<GenerationStarted>(recorder.Events[0]);
        Assert.Equal("Hi there", started.UserMessage?.Content);
        Assert.Equal(turn.AssistantMessage.Id, started.AssistantMessage.Id);
        Assert.Equal(0, started.TrimmedMessages);
        Assert.Equal("Hello there!", recorder.DeltaText);
        var completed = recorder.Events.OfType<GenerationCompleted>().Single().Message;
        Assert.Equal((MessageStatus.Complete, "Hello there!", 3), (completed.Status, completed.Content, completed.TokenCount));
        Assert.Equal("Greeting the assistant", recorder.Events.OfType<TitleGenerated>().Single().Title);

        var saved = await ReadMessagesAsync(ct);
        Assert.Equal(
            [(1, MessageRole.User, MessageStatus.Complete, "Hi there"), (2, MessageRole.Assistant, MessageStatus.Complete, "Hello there!")],
            saved.Select(m => (m.Sequence, m.Role, m.Status, m.Content)));
        Assert.Equal("Greeting the assistant", (await _services.Conversations.GetAsync(_conversation.Id, ct)).Title);
    }

    [Fact]
    public async Task A_subscriber_that_joins_mid_stream_gets_the_snapshot_plus_exactly_the_later_deltas()
    {
        var ct = TestContext.Current.CancellationToken;
        var live = Chat.GoLive();
        var early = SubscribeFromTheStart(_conversation.Id);
        await Generation.SendAsync(_conversation.Id, "Say hello", null, ct);
        await live.Writer.WriteAsync("Hel", ct);
        await early.WaitForAsync<GenerationDelta>(d => d.Text == "Hel");

        var late = new EventRecorder();
        var subscription = Generation.Subscribe(_conversation.Id, late.Record);
        _subscriptions.Add(subscription);
        await live.Writer.WriteAsync("lo", ct);
        await live.Writer.WriteAsync(" world", ct);
        live.Writer.Complete();
        await late.WaitForAsync<GenerationCompleted>();

        Assert.Equal("Hel", subscription.Snapshot?.Text);
        Assert.DoesNotContain(late.Events, e => e is GenerationStarted);
        var final = late.Events.OfType<GenerationCompleted>().Single().Message.Content;
        Assert.Equal("Hello world", final);
        Assert.Equal(final, subscription.Snapshot!.Text + late.DeltaText);
        Assert.Equal(final, early.DeltaText);
    }

    [Fact]
    public async Task Stop_leaves_the_reply_cancelled_with_the_partial_text_and_no_title()
    {
        var ct = TestContext.Current.CancellationToken;
        var live = Chat.GoLive();
        var recorder = SubscribeFromTheStart(_conversation.Id);
        await Generation.SendAsync(_conversation.Id, "Write an essay", null, ct);
        await live.Writer.WriteAsync("Partial", ct);
        await recorder.WaitForAsync<GenerationDelta>();

        Generation.Stop(_conversation.Id);
        await recorder.WaitForAsync<GenerationCompleted>();
        await Generation.WhenIdleAsync();

        var message = recorder.Events.OfType<GenerationCompleted>().Single().Message;
        Assert.Equal((MessageStatus.Cancelled, "Partial"), (message.Status, message.Content));
        var saved = (await ReadMessagesAsync(ct))[^1];
        Assert.Equal((MessageStatus.Cancelled, "Partial"), (saved.Status, saved.Content));
        Assert.DoesNotContain(recorder.Events, e => e is TitleGenerated);
        Assert.False(Generation.IsGenerating(_conversation.Id));
    }

    [Fact]
    public async Task Disposing_a_subscription_does_not_stop_the_generation()
    {
        var ct = TestContext.Current.CancellationToken;
        var live = Chat.GoLive();
        await Generation.SendAsync(_conversation.Id, "Hello", null, ct);
        var recorder = new EventRecorder();
        Generation.Subscribe(_conversation.Id, recorder.Record).Dispose();

        await live.Writer.WriteAsync("Still ", ct);
        await live.Writer.WriteAsync("here", ct);
        live.Writer.Complete();
        await Generation.WhenIdleAsync();

        var saved = (await ReadMessagesAsync(ct))[^1];
        Assert.Equal((MessageStatus.Complete, "Still here"), (saved.Status, saved.Content));
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task A_client_exception_leaves_the_reply_in_error_and_publishes_GenerationFailed_last()
    {
        var ct = TestContext.Current.CancellationToken;
        Chat.FailWith = new HttpRequestException("Connection refused (localhost:11434)");
        var recorder = SubscribeFromTheStart(_conversation.Id);

        await Generation.SendAsync(_conversation.Id, "Hello", null, ct);
        await recorder.WaitForAsync<GenerationFailed>();
        await Generation.WhenIdleAsync();

        var failed = recorder.Events.OfType<GenerationFailed>().Single().Message;
        Assert.Equal(MessageStatus.Error, failed.Status);
        Assert.Equal("Ollama is unreachable: Connection refused (localhost:11434)", failed.ErrorMessage);
        Assert.IsType<GenerationFailed>(recorder.Events[^1]);
        Assert.DoesNotContain(recorder.Events, e => e is GenerationCompleted or TitleGenerated);
        var saved = (await ReadMessagesAsync(ct))[^1];
        Assert.Equal((MessageStatus.Error, failed.ErrorMessage), (saved.Status, saved.ErrorMessage));
    }

    [Fact]
    public async Task A_second_send_while_one_is_running_throws_Conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        Chat.GoLive();
        await Generation.SendAsync(_conversation.Id, "First", null, ct);

        await Assert.ThrowsAsync<ConflictException>(() => Generation.SendAsync(_conversation.Id, "Second", null, ct));
        await Assert.ThrowsAsync<ConflictException>(() => Generation.RetryAsync(_conversation.Id, ct));
        Assert.True((await _services.Conversations.GetAsync(_conversation.Id, ct)).IsGenerating);

        Generation.Stop(_conversation.Id);
        await Generation.WhenIdleAsync();
        Assert.Equal(2, (await ReadMessagesAsync(ct)).Count);
    }

    [Fact]
    public async Task Retry_works_only_after_an_error_or_a_stop_and_replaces_that_reply()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ConflictException>(() => Generation.RetryAsync(_conversation.Id, ct));
        await Generation.SendAsync(_conversation.Id, "First", null, ct);
        await Generation.WhenIdleAsync();
        await Assert.ThrowsAsync<ConflictException>(() => Generation.RetryAsync(_conversation.Id, ct));

        Chat.FailWith = new HttpRequestException("Connection refused");
        var failed = await Generation.SendAsync(_conversation.Id, "Second", null, ct);
        await Generation.WhenIdleAsync();
        Chat.FailWith = null;

        var retried = await Generation.RetryAsync(_conversation.Id, ct);
        await Generation.WhenIdleAsync();

        Assert.Null(retried.UserMessage);
        Assert.NotEqual(failed.AssistantMessage.Id, retried.AssistantMessage.Id);
        Assert.Equal(failed.AssistantMessage.Sequence, retried.AssistantMessage.Sequence);
        var saved = await ReadMessagesAsync(ct);
        Assert.Equal([1, 2, 3, 4], saved.Select(m => m.Sequence));
        Assert.DoesNotContain(saved, m => m.Id == failed.AssistantMessage.Id);
        Assert.Equal((retried.AssistantMessage.Id, MessageStatus.Complete), (saved[^1].Id, saved[^1].Status));
    }

    [Fact]
    public async Task Retry_after_a_stop_publishes_a_start_without_a_user_message()
    {
        var ct = TestContext.Current.CancellationToken;
        var live = Chat.GoLive();
        await Generation.SendAsync(_conversation.Id, "Hello", null, ct);
        await live.Writer.WriteAsync("Hal", ct);
        Generation.Stop(_conversation.Id);
        await Generation.WhenIdleAsync();
        live.Writer.Complete();
        var recorder = SubscribeFromTheStart(_conversation.Id);

        await Generation.RetryAsync(_conversation.Id, ct);
        await recorder.WaitForAsync<GenerationCompleted>();

        Assert.Null(recorder.Events.OfType<GenerationStarted>().Single().UserMessage);
        Assert.Equal(MessageStatus.Complete, recorder.Events.OfType<GenerationCompleted>().Single().Message.Status);
    }

    [Fact]
    public async Task A_failing_title_call_falls_back_to_the_truncated_first_message()
    {
        var ct = TestContext.Current.CancellationToken;
        Chat.TitleFailWith = new HttpRequestException("Connection refused");
        var recorder = SubscribeFromTheStart(_conversation.Id);

        await Generation.SendAsync(_conversation.Id, "Please plan a three day trip to Lisbon with museums and food", null, ct);
        await recorder.WaitForAsync<TitleGenerated>();

        Assert.Equal("Please plan a three day trip to Lisbon with museum…", recorder.Events.OfType<TitleGenerated>().Single().Title);
    }

    [Fact]
    public async Task Only_the_first_reply_of_an_untitled_chat_gets_a_title()
    {
        var ct = TestContext.Current.CancellationToken;
        await Generation.SendAsync(_conversation.Id, "First", null, ct);
        await Generation.WhenIdleAsync();
        var second = SubscribeFromTheStart(_conversation.Id);
        await Generation.SendAsync(_conversation.Id, "Second", null, ct);
        await second.WaitForAsync<GenerationCompleted>();
        await Generation.WhenIdleAsync();

        var renamed = await _services.Conversations.CreateAsync(ProjectIds.General, null, ct);
        await _services.Conversations.RenameAsync(renamed.Id, "My own title", ct);
        await Generation.SendAsync(renamed.Id, "Hello", null, ct);
        await Generation.WhenIdleAsync();

        Assert.DoesNotContain(second.Events, e => e is TitleGenerated);
        Assert.Single(Chat.TitleRequests);
        Assert.Equal("My own title", (await _services.Conversations.GetAsync(renamed.Id, ct)).Title);
    }

    [Fact]
    public async Task Shutdown_cancels_active_generations_and_they_end_cancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        var live = Chat.GoLive();
        var recorder = SubscribeFromTheStart(_conversation.Id);
        await Generation.SendAsync(_conversation.Id, "Hello", null, ct);
        await live.Writer.WriteAsync("Half", ct);
        await recorder.WaitForAsync<GenerationDelta>();

        _services.Lifetime.StopApplication();
        await Generation.StopAsync(ct);

        var saved = (await ReadMessagesAsync(ct))[^1];
        Assert.Equal((MessageStatus.Cancelled, "Half"), (saved.Status, saved.Content));
    }

    [Fact]
    public async Task Partial_text_is_saved_at_most_every_two_seconds()
    {
        var ct = TestContext.Current.CancellationToken;
        var live = Chat.GoLive();
        var recorder = SubscribeFromTheStart(_conversation.Id);
        await Generation.SendAsync(_conversation.Id, "Hello", null, ct);
        await live.Writer.WriteAsync("One", ct);
        await recorder.WaitForAsync<GenerationDelta>(d => d.Text == "One");
        Assert.Equal("", (await ReadMessagesAsync(ct))[^1].Content);

        _services.Time.Advance(TimeSpan.FromSeconds(2));
        await live.Writer.WriteAsync(" two", ct);
        await recorder.WaitForAsync<GenerationDelta>(d => d.Text == " two");

        await EventuallyAsync(async () => (await ReadMessagesAsync(ct))[^1].Content == "One two");
        live.Writer.Complete();
    }

    [Fact]
    public async Task Sending_validates_the_content_the_conversation_and_the_model()
    {
        var ct = TestContext.Current.CancellationToken;

        var empty = await Assert.ThrowsAsync<ValidationException>(() => Generation.SendAsync(_conversation.Id, "  \n ", null, ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Generation.SendAsync(Guid.CreateVersion7(), "Hi", null, ct));
        await Assert.ThrowsAsync<ValidationException>(() => Generation.SendAsync(_conversation.Id, "Hi", Guid.CreateVersion7(), ct));

        Assert.Contains("Content", empty.Errors.Keys);
        Assert.Empty(await ReadMessagesAsync(ct));
        Assert.False(Generation.IsGenerating(_conversation.Id));
    }

    [Fact]
    public async Task Sending_with_another_model_switches_the_conversation_to_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var llama = (await _services.Models.ListAsync(false, ct)).Single(m => m.Tag == "llama3.2:3b");

        var turn = await Generation.SendAsync(_conversation.Id, "Hi", llama.Id, ct);
        await Generation.WhenIdleAsync();

        Assert.Equal(llama.Id, turn.AssistantMessage.ModelId);
        Assert.Equal(llama.Id, (await _services.Conversations.GetAsync(_conversation.Id, ct)).ModelId);
        Assert.Equal("llama3.2:3b", Chat.StreamingRequests.Single().Options?.ModelId);
    }

    [Fact]
    public async Task Deleting_the_conversation_stops_its_generation()
    {
        var ct = TestContext.Current.CancellationToken;
        Chat.GoLive();
        await Generation.SendAsync(_conversation.Id, "Hello", null, ct);

        await _services.Conversations.DeleteAsync(_conversation.Id, ct);
        await Generation.WhenIdleAsync();

        Assert.False(Generation.IsGenerating(_conversation.Id));
        Assert.Empty(await ReadMessagesAsync(ct));
    }

    [Fact]
    public async Task Reports_trimmed_messages_when_the_history_does_not_fit()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = (await _services.Models.ListAsync(false, ct)).Single(m => m.Id == _conversation.ModelId);
        await _services.Models.UpdateAsync(model.Id, null, 512, ct);
        // Budget for 512 is 384 tokens; each history message costs 404, so both go.
        await using (var db = _database.CreateContext())
        {
            db.Messages.AddRange(
                TestData.NewMessage(_conversation.Id, 1, content: new string('a', 1400)),
                TestData.NewMessage(_conversation.Id, 2, MessageRole.Assistant, content: new string('b', 1400)));
            await db.SaveChangesAsync(ct);
        }

        var recorder = SubscribeFromTheStart(_conversation.Id);
        await Generation.SendAsync(_conversation.Id, "Short question", null, ct);
        await recorder.WaitForAsync<GenerationCompleted>();

        Assert.Equal(2, recorder.Events.OfType<GenerationStarted>().Single().TrimmedMessages);
        Assert.Equal(["Short question"], Chat.StreamingRequests.Single().Messages.Select(m => m.Text));
    }

    [Fact]
    public async Task Num_ctx_and_the_model_reach_ollama_and_its_token_count_is_saved()
    {
        var ct = TestContext.Current.CancellationToken;
        await _services.Generation.StopAsync(ct);
        await _database.DisposeAsync();
        await SetUpAsync(fakeChat: false);
        var model = (await _services.Models.ListAsync(false, ct)).Single(m => m.Id == _conversation.ModelId);
        await _services.Models.UpdateAsync(model.Id, null, 8192, ct);

        await Generation.SendAsync(_conversation.Id, "Salut", null, ct);
        await Generation.WhenIdleAsync();

        var requests = _services.Ollama.ChatRequests.ToList();
        Assert.Equal(2, requests.Count); // the reply, then the title
        foreach (var request in requests)
        {
            Assert.Equal(model.Tag, request.GetProperty("model").GetString());
            Assert.Equal(8192, request.GetProperty("options").GetProperty("num_ctx").GetInt32());
        }

        Assert.Equal("Salut", requests[0].GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString());
        var saved = (await ReadMessagesAsync(ct))[^1];
        Assert.Equal((MessageStatus.Complete, "Bonjour !", 7), (saved.Status, saved.Content, saved.TokenCount));
    }

    private async Task SetUpAsync(bool fakeChat)
    {
        var ct = TestContext.Current.CancellationToken;
        _database = TestDatabase.CreateEmpty();
        _services = new TestServices(_database, fakeChat: fakeChat);
        _services.Ollama.Models.Add(new FakeOllamaModel("phi4-mini:latest", Capabilities: ["completion"]));
        _services.Ollama.Models.Add(new FakeOllamaModel("llama3.2:3b", Capabilities: ["completion"], Architecture: "llama"));
        await _services.Seed.RunAsync(ct);
        var phi = (await _services.Models.ListAsync(false, ct)).Single(m => m.Tag == "phi4-mini:latest");
        _conversation = await _services.Conversations.CreateAsync(ProjectIds.General, phi.Id, ct);
    }

    /// <summary>
    /// Subscribes the moment the next turn registers, before its background task can publish anything,
    /// the same way another tab joins on <c>ConversationsChanged</c>.
    /// </summary>
    private EventRecorder SubscribeFromTheStart(Guid conversationId)
    {
        var recorder = new EventRecorder();
        var subscribed = false;
        _services.Notifier.ConversationsChanged += _ =>
        {
            if (!subscribed && Generation.IsGenerating(conversationId))
            {
                subscribed = true;
                _subscriptions.Add(Generation.Subscribe(conversationId, recorder.Record));
            }
        };
        return recorder;
    }

    private async Task<List<Message>> ReadMessagesAsync(CancellationToken ct)
    {
        await using var db = _database.CreateContext();
        return await db.Messages.AsNoTracking().Where(m => m.ConversationId == _conversation.Id).OrderBy(m => m.Sequence).ToListAsync(ct);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not become true within 5 seconds.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
