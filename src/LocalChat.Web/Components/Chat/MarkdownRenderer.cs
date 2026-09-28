using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace LocalChat.Web.Components.Chat;

/// <summary>
/// Renders model output as HTML with Markdig: advanced extensions on and raw HTML shown as text. Links open
/// in a new tab with <c>rel="noopener noreferrer"</c>. Images become plain links, so model output can never make the
/// browser fetch anything, and only http, https and mailto links keep their target.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static MarkupString ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            link.IsImage = false;
            if (!IsSafe(link.Url))
            {
                link.Url = "#";
            }

            OpenInNewTab(link);
        }

        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (!link.IsEmail && !IsSafe(link.Url))
            {
                link.Url = "#";
            }

            OpenInNewTab(link);
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        return new MarkupString(writer.ToString());
    }

    private static bool IsSafe(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto);

    private static void OpenInNewTab(Inline link)
    {
        var attributes = link.GetAttributes();
        attributes.AddPropertyIfNotExist("target", "_blank");
        attributes.AddPropertyIfNotExist("rel", "noopener noreferrer");
    }
}
