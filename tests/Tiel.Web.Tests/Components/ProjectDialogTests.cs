using Bunit;
using Tiel.Web.Components.Shared;
using Tiel.Web.Services;
using Tiel.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Tiel.Web.Tests.Components;

/// <summary>Opens <see cref="ProjectDialog"/> through the real dialog service, as the Projects page does.</summary>
public sealed class ProjectDialogTests : AppTestContext
{
    private IRenderedComponent<FluentDialogProvider> _provider = null!;
    private Task<DialogResult> _result = null!;

    [Fact]
    public async Task Shows_field_errors_from_validation_inline_and_stays_open()
    {
        var dialog = await OpenAsync(project: null);
        await SetInputAsync(dialog, 1, new string('d', 501));

        await ClickPrimaryAsync(dialog);

        _provider.WaitForAssertion(() =>
        {
            Assert.Contains("Enter a project name.", _provider.Markup, StringComparison.Ordinal);
            Assert.Contains("Use at most 500 characters.", _provider.Markup, StringComparison.Ordinal);
        });
        Assert.Equal(MessageState.Error, dialog.FindComponents<FluentTextInput>()[0].Instance.MessageState);
        Assert.False(_result.IsCompleted);
    }

    [Fact]
    public async Task Shows_a_duplicate_name_as_an_error_on_the_name_field()
    {
        await App.Projects.CreateAsync(new ProjectInput("Work", null, null), TestContext.Current.CancellationToken);
        var dialog = await OpenAsync(project: null);
        await SetInputAsync(dialog, 0, "work");

        await ClickPrimaryAsync(dialog);

        _provider.WaitForAssertion(() => Assert.Equal(
            "A project named 'work' already exists.", dialog.FindComponents<FluentTextInput>()[0].Instance.Message));
        Assert.False(_result.IsCompleted);
    }

    [Fact]
    public async Task Saves_a_valid_project_and_closes_with_it()
    {
        var dialog = await OpenAsync(project: null);
        await SetInputAsync(dialog, 0, "Travel");
        await _provider.InvokeAsync(() => dialog.FindComponent<FluentTextArea>().Instance.ValueChanged.InvokeAsync("Answer in French."));

        await ClickPrimaryAsync(dialog);

        var result = await _result.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(result.Cancelled);
        var saved = result.GetValue<ProjectDto>();
        Assert.Equal(("Travel", "Answer in French."), (saved.Name, saved.Instructions));
        Assert.Contains(await App.Projects.ListAsync(TestContext.Current.CancellationToken), p => p.Name == "Travel");
    }

    [Fact]
    public async Task Editing_starts_from_the_projects_values_and_says_so()
    {
        var project = new ProjectDto(Guid.CreateVersion7(), "Work", "Day job", "Be brief.", false, 0, TestData.Now, TestData.Now);

        var dialog = await OpenAsync(project);

        Assert.Equal("Work", dialog.FindComponents<FluentTextInput>()[0].Instance.Value);
        Assert.Equal("Be brief.", dialog.FindComponent<FluentTextArea>().Instance.Value);
        var options = dialog.Instance.DialogInstance.Options;
        Assert.Equal(("Edit project", "Save"), (options.Header.Title, options.Footer.PrimaryAction.Label));
    }

    private async Task<IRenderedComponent<ProjectDialog>> OpenAsync(ProjectDto? project)
    {
        _provider = Render<FluentDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await _provider.InvokeAsync(() =>
        {
            _result = dialogs.ShowDialogAsync<ProjectDialog>(options => options.Parameters.Add(nameof(ProjectDialog.Project), project));
        });
        _provider.WaitForState(() => _provider.FindComponents<ProjectDialog>().Count == 1);
        return _provider.FindComponent<ProjectDialog>();
    }

    private Task SetInputAsync(IRenderedComponent<ProjectDialog> dialog, int index, string value) =>
        _provider.InvokeAsync(() => dialog.FindComponents<FluentTextInput>()[index].Instance.ValueChanged.InvokeAsync(value));

    private Task ClickPrimaryAsync(IRenderedComponent<ProjectDialog> dialog)
    {
        var instance = dialog.Instance.DialogInstance;
        return _provider.InvokeAsync(() => instance.Options.Footer.PrimaryAction.OnClickAsync!(instance));
    }
}
