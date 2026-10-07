using System.Globalization;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Relio.Web.Marketing;

/// <summary>Loads the build-embedded public release notes and renders a restricted Markdown dialect.</summary>
public sealed class ReleaseNotes
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    /// <summary>Loads release notes once from the application assembly, with no runtime network or file access.</summary>
    public ReleaseNotes(ILogger<ReleaseNotes> logger)
    {
        using var resource = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("Relio.ReleaseNotes.md");
        Html = RenderResource(resource, logger);
    }

    /// <summary>Gets safely rendered release notes, or null when the build resource is missing.</summary>
    public string? Html { get; }

    /// <summary>Reads a release-note resource; missing content is reported explicitly without a stack trace.</summary>
    public static string? RenderResource(Stream? resource, ILogger logger)
    {
        if (resource is null)
        {
            logger.LogWarning("The embedded Relio release notes are unavailable.");
            return null;
        }

        using var reader = new StreamReader(resource);
        return RenderMarkdown(reader.ReadToEnd());
    }

    /// <summary>
    /// Renders repository Markdown with raw HTML disabled, validated links and local raster images only.
    /// The page owns its h1; release headings start at h2.
    /// </summary>
    public static string RenderMarkdown(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        if (document.FirstOrDefault() is HeadingBlock { Level: 1 })
        {
            document.RemoveAt(0);
        }
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            heading.Level = Math.Max(2, heading.Level);
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.RemoveAll(item => item is LinkInlineRenderer or AutolinkInlineRenderer);
        renderer.ObjectRenderers.Add(new SafeLinkRenderer());
        renderer.ObjectRenderers.Add(new SafeAutolinkRenderer());
        renderer.Render(document);
        return writer.ToString();
    }

    private static bool IsSafeUrl(string? url, bool image)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Any(char.IsControl) || url.Contains('\\'))
        {
            return false;
        }
        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.Contains('%') && !url.Split('/').Any(segment => segment is "." or ".."))
        {
            return !image || (url.StartsWith("/img/", StringComparison.Ordinal)
                && !url.Contains('?') && !url.Contains('#')
                && (url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)));
        }
        if (image)
        {
            return false;
        }
        return url.StartsWith('#')
            || (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && string.IsNullOrEmpty(uri.UserInfo));
    }

    private sealed class SafeLinkRenderer : LinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, LinkInline link)
        {
            if (IsSafeUrl(link.Url, link.IsImage))
            {
                base.Write(renderer, link);
            }
            else
            {
                renderer.WriteChildren(link);
                renderer.Write(link.IsImage ? " (image omitted)" : " (link omitted)");
            }
        }
    }

    private sealed class SafeAutolinkRenderer : AutolinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, AutolinkInline link)
        {
            if (!link.IsEmail && IsSafeUrl(link.Url, image: false))
            {
                base.Write(renderer, link);
            }
            else
            {
                renderer.WriteEscape(link.Url);
                renderer.Write(" (link omitted)");
            }
        }
    }
}
