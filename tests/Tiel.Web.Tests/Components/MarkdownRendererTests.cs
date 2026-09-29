using Tiel.Web.Components.Chat;

namespace Tiel.Web.Tests.Components;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void Shows_raw_html_as_text()
    {
        var html = MarkdownRenderer.ToHtml("Hi <b onclick=\"x()\">there</b><script>alert(1)</script>").Value;

        Assert.DoesNotContain("<b", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Links_open_in_a_new_tab_without_an_opener()
    {
        var html = MarkdownRenderer.ToHtml("[a](https://example.com) https://auto.example <https://angle.example>").Value;

        Assert.Equal(3, CountOf(html, "target=\"_blank\""));
        Assert.Equal(3, CountOf(html, "rel=\"noopener noreferrer\""));
    }

    [Fact]
    public void Images_become_links_so_nothing_loads_from_elsewhere()
    {
        var html = MarkdownRenderer.ToHtml("![pixel](https://tracker.example/p.png)").Value;

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://tracker.example/p.png\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("[x](data:text/html,hi)")]
    public void Unsafe_link_targets_are_neutralized(string markdown)
    {
        var html = MarkdownRenderer.ToHtml(markdown).Value;

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"data:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Advanced_extensions_are_on()
    {
        var html = MarkdownRenderer.ToHtml("| a | b |\n|---|---|\n| 1 | 2 |\n\n- [x] done").Value;

        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
}
