using Bunit;
using LocalChat.Web.Components.Chat;
using LocalChat.Web.Components.Pages;
using LocalChat.Web.Data;
using LocalChat.Web.Services;
using LocalChat.Web.Tests.Infrastructure;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LocalChat.Web.Tests.Components;

public sealed class SettingsPageTests : AppTestContext
{
    [Fact]
    public async Task Test_connection_checks_the_typed_url_and_shows_the_version_or_the_error()
    {
        Health.Test = url => url.Contains("wrong", StringComparison.Ordinal)
            ? new ConnectionTest(false, null, "Connection refused (wrong-box:11434)")
            : new ConnectionTest(true, "0.34.4", null);
        var cut = RenderSettings();

        await SetUrlAsync(cut, "http://gpu-box:11434");
        await ClickAsync(cut, "Test connection");
        cut.WaitForAssertion(() => Assert.Contains("Connected to Ollama 0.34.4.", cut.Markup, StringComparison.Ordinal));

        await SetUrlAsync(cut, "http://wrong-box:11434");
        await ClickAsync(cut, "Test connection");
        cut.WaitForAssertion(() => Assert.Contains("Couldn't connect: Connection refused (wrong-box:11434)", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("http://localhost:11434", (await App.Settings.GetAsync(TestContext.Current.CancellationToken)).OllamaBaseUrl);
    }

    [Fact]
    public async Task Saving_an_invalid_url_shows_the_error_inline_and_saves_nothing()
    {
        var cut = RenderSettings();

        await SetUrlAsync(cut, "localhost:11434");
        await ClickAsync(cut, "Save");

        cut.WaitForAssertion(() => Assert.StartsWith(
            "Enter an absolute http or https URL", cut.FindComponents<FluentTextInput>()[0].Instance.Message, StringComparison.Ordinal));
        Assert.Equal("http://localhost:11434", (await App.Settings.GetAsync(TestContext.Current.CancellationToken)).OllamaBaseUrl);
    }

    [Fact]
    public async Task Saving_a_url_stores_it_and_syncs_models_from_it()
    {
        var cut = RenderSettings();

        await SetUrlAsync(cut, "http://gpu-box:11434/");
        await ClickAsync(cut, "Save");

        cut.WaitForAssertion(() => Assert.Contains("Saved. Models were synced from this Ollama.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("http://gpu-box:11434", (await App.Settings.GetAsync(TestContext.Current.CancellationToken)).OllamaBaseUrl);
        Assert.Contains(App.Ollama.Requests, u => u.ToString() == "http://gpu-box:11434/api/tags");
    }

    [Fact]
    public async Task Sync_shows_its_summary_and_the_new_models()
    {
        App.Ollama.Models.Add(new FakeOllamaModel("llama3.2:3b", Capabilities: ["completion"], Architecture: "llama"));
        var cut = RenderSettings();

        await ClickAsync(cut, "Sync from Ollama");

        cut.WaitForAssertion(() => Assert.Contains("Synced: 1 added, 0 updated, 0 marked unavailable.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(["llama3.2:3b", "phi4-mini:latest"], cut.FindAll("td.tag").Select(td => td.TextContent));
    }

    [Fact]
    public async Task Display_name_and_context_length_edits_persist_and_errors_show_inline()
    {
        var ct = TestContext.Current.CancellationToken;
        var cut = RenderSettings();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("td.tag")));

        await cut.InvokeAsync(() => cut.FindComponents<FluentTextInput>()[1].Instance.ValueChanged.InvokeAsync("Phi 4 mini"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentTextInput>()[2].Instance.ValueChanged.InvokeAsync("8192"));
        await ClickAsync(cut, "Save", last: true);

        var model = Assert.Single(await App.Models.ListAsync(true, ct));
        Assert.Equal(("Phi 4 mini", 8192), (model.DisplayName, model.ContextLength));

        await cut.InvokeAsync(() => cut.FindComponents<FluentTextInput>()[2].Instance.ValueChanged.InvokeAsync("100"));
        await ClickAsync(cut, "Save", last: true);
        cut.WaitForAssertion(() => Assert.Equal(
            "Enter a context length from 512 to 131072.", cut.FindComponents<FluentTextInput>()[2].Instance.Message));
        Assert.Equal(8192, Assert.Single(await App.Models.ListAsync(true, ct)).ContextLength);
    }

    [Fact]
    public async Task A_context_length_that_is_not_a_whole_number_is_rejected_inline()
    {
        var cut = RenderSettings();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("td.tag")));

        await cut.InvokeAsync(() => cut.FindComponents<FluentTextInput>()[2].Instance.ValueChanged.InvokeAsync("4k"));
        await ClickAsync(cut, "Save", last: true);

        cut.WaitForAssertion(() => Assert.Equal("Enter a whole number.", cut.FindComponents<FluentTextInput>()[2].Instance.Message));
        Assert.Equal(4096, Assert.Single(await App.Models.ListAsync(true, TestContext.Current.CancellationToken)).ContextLength);
    }

    [Fact]
    public async Task A_new_chat_starts_with_the_default_model_chosen_here()
    {
        var ct = TestContext.Current.CancellationToken;
        App.Ollama.Models.Add(new FakeOllamaModel("llama3.2:3b", Capabilities: ["completion"], Architecture: "llama"));
        await App.Models.SyncAsync(ct);
        var phi = (await App.Models.ListAsync(false, ct)).Single(m => m.Tag == "phi4-mini:latest");
        var settings = RenderSettings();
        settings.WaitForAssertion(() => Assert.Equal(2, settings.FindAll("td.tag").Count));

        await settings.InvokeAsync(() => settings.FindComponent<ModelPicker>().Instance.OnChanged.InvokeAsync(phi.Id));

        Assert.Equal(phi.Id, (await App.Settings.GetAsync(ct)).DefaultModelId);
        var newChat = Render<NewChat>(p => p.Add(x => x.ProjectId, ProjectIds.General));
        newChat.WaitForAssertion(() => Assert.Equal(phi.Id, newChat.FindComponent<ModelPicker>().Instance.ModelId));
    }

    private IRenderedComponent<Settings> RenderSettings()
    {
        var cut = Render<Settings>();
        cut.WaitForAssertion(() => Assert.Equal("http://localhost:11434", cut.FindComponents<FluentTextInput>()[0].Instance.Value));
        return cut;
    }

    private static Task SetUrlAsync(IRenderedComponent<Settings> cut, string url) =>
        cut.InvokeAsync(() => cut.FindComponents<FluentTextInput>()[0].Instance.ValueChanged.InvokeAsync(url));

    private static Task ClickAsync(IRenderedComponent<Settings> cut, string text, bool last = false)
    {
        var buttons = cut.FindComponents<FluentButton>().Where(b => b.Find("fluent-button").TextContent.Trim() == text).ToList();
        var button = last ? buttons[^1] : buttons[0];
        return cut.InvokeAsync(() => button.Instance.OnClick.InvokeAsync());
    }
}
