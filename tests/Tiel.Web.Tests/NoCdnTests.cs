namespace Tiel.Web.Tests;

/// <summary>
/// Fluent UI v5 lazy-loads scripts from unpkg.com for a few components. The app must send nothing anywhere
/// except Ollama, so those components stay out of its markup.
/// </summary>
public sealed class NoCdnTests
{
    private static readonly string[] CdnLoadingComponents = ["<FluentNumberInput", "<FluentSortableList", "MaskPattern="];

    [Fact]
    public void No_component_that_loads_scripts_from_a_cdn_is_used()
    {
        var components = Path.Combine(RepositoryRoot(), "src", "Tiel.Web", "Components");
        var offenders =
            from file in Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories)
            let text = File.ReadAllText(file)
            from marker in CdnLoadingComponents
            where text.Contains(marker, StringComparison.Ordinal)
            select $"{Path.GetFileName(file)}: {marker}";

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_markup_links_to_another_host()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "Tiel.Web");
        var offenders =
            from file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            where file.EndsWith(".razor", StringComparison.Ordinal) || file.EndsWith(".html", StringComparison.Ordinal)
            where !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            let text = File.ReadAllText(file)
            where text.Contains("src=\"http", StringComparison.OrdinalIgnoreCase) || text.Contains("href=\"http", StringComparison.OrdinalIgnoreCase)
            select Path.GetFileName(file);

        Assert.Empty(offenders);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tiel.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
