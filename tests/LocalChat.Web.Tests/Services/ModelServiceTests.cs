using LocalChat.Web.Data.Entities;
using LocalChat.Web.Services;
using LocalChat.Web.Tests.Infrastructure;

namespace LocalChat.Web.Tests.Services;

public sealed class ModelServiceTests : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private ModelService _models = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateMigratedAsync();
        _models = new TestServices(_database).Models;
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Lists_models_by_display_name_ignoring_case()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddModelsAsync(ct, Named("b-tag", "phi"), Named("c-tag", "Llama"), Named("a-tag", "qwen", isAvailable: false));

        var available = await _models.ListAsync(includeUnavailable: false, ct);
        var all = await _models.ListAsync(includeUnavailable: true, ct);

        Assert.Equal(["Llama", "phi"], available.Select(m => m.DisplayName));
        Assert.Equal(["Llama", "phi", "qwen"], all.Select(m => m.DisplayName));
    }

    [Fact]
    public async Task Updates_the_display_name_trimmed_and_the_context_length()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = TestData.NewModel();
        await AddModelsAsync(ct, model);

        var updated = await _models.UpdateAsync(model.Id, "  Phi 4 mini  ", 8192, ct);

        Assert.Equal(new ModelDto(model.Id, model.Tag, "Phi 4 mini", 8192, 131072, true), updated);
        Assert.Equal(updated, Assert.Single(await _models.ListAsync(true, ct)));
    }

    [Fact]
    public async Task Null_arguments_leave_fields_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = TestData.NewModel();
        await AddModelsAsync(ct, model);

        var updated = await _models.UpdateAsync(model.Id, displayName: null, contextLength: 2048, ct);

        Assert.Equal(model.DisplayName, updated.DisplayName);
        Assert.Equal(2048, updated.ContextLength);
    }

    [Theory]
    [InlineData(131072, 512, true)]
    [InlineData(131072, 131072, true)]
    [InlineData(131072, 511, false)]
    [InlineData(131072, 131073, false)]
    [InlineData(8192, 8193, false)]
    [InlineData(null, 131072, true)]
    [InlineData(null, 131073, false)]
    public async Task Context_length_must_be_from_512_to_the_max(int? max, int contextLength, bool valid)
    {
        var ct = TestContext.Current.CancellationToken;
        var model = TestData.NewModel();
        model.MaxContextLength = max;
        await AddModelsAsync(ct, model);

        if (valid)
        {
            Assert.Equal(contextLength, (await _models.UpdateAsync(model.Id, null, contextLength, ct)).ContextLength);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ValidationException>(() => _models.UpdateAsync(model.Id, null, contextLength, ct));
            Assert.Equal($"Enter a context length from 512 to {max ?? 131072}.", error.Errors[nameof(ModelDto.ContextLength)]);
            Assert.Equal(4096, Assert.Single(await _models.ListAsync(true, ct)).ContextLength);
        }
    }

    [Fact]
    public async Task Rejects_an_empty_or_long_display_name_and_reports_every_field()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = TestData.NewModel();
        await AddModelsAsync(ct, model);

        var empty = await Assert.ThrowsAsync<ValidationException>(() => _models.UpdateAsync(model.Id, "   ", 100, ct));
        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => _models.UpdateAsync(model.Id, new string('x', 101), null, ct));

        Assert.Equal([nameof(ModelDto.ContextLength), nameof(ModelDto.DisplayName)], empty.Errors.Keys.Order());
        Assert.Contains(nameof(ModelDto.DisplayName), tooLong.Errors.Keys);
        Assert.Equal(model.DisplayName, Assert.Single(await _models.ListAsync(true, ct)).DisplayName);
    }

    [Fact]
    public async Task Updating_an_unknown_model_throws_NotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _models.UpdateAsync(Guid.CreateVersion7(), "Name", null, TestContext.Current.CancellationToken));
    }

    private static Model Named(string tag, string displayName, bool isAvailable = true)
    {
        var model = TestData.NewModel(tag, isAvailable);
        model.DisplayName = displayName;
        return model;
    }

    private async Task AddModelsAsync(CancellationToken ct, params Model[] models)
    {
        await using var db = _database.CreateContext();
        db.Models.AddRange(models);
        await db.SaveChangesAsync(ct);
    }
}
