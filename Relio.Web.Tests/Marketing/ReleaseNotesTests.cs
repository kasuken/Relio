using Microsoft.Extensions.Logging.Abstractions;
using Relio.Web.Marketing;

namespace Relio.Web.Tests.Marketing;

public sealed class ReleaseNotesTests
{
    [Fact]
    public void Embedded_resource_contains_real_release_notes_with_no_repository_file_dependency()
    {
        var notes = new ReleaseNotes(NullLogger<ReleaseNotes>.Instance);
        notes.Html.Should().NotBeNull().And.Contain("Unreleased").And.Contain("Public marketing pages");
        notes.Html.Should().NotContain("<h1>");
    }

    [Theory]
    [InlineData("[script](javascript:alert%281%29)")]
    [InlineData("[script](JaVaScRiPt:alert%281%29)")]
    [InlineData("[script](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[script](vbscript:msgbox%281%29)")]
    [InlineData("[script](//attacker.example/path)")]
    [InlineData("[script](jav&#x61;script:alert%281%29)")]
    [InlineData("[script](/%2f%2fattacker.example/path)")]
    [InlineData("[script](https://user:secret@example.com/path)")]
    public void Unsafe_links_are_not_rendered_as_anchors(string markdown)
    {
        var html = ReleaseNotes.RenderMarkdown(markdown);
        html.Should().NotContain("<a ").And.Contain("link omitted");
    }

    [Theory]
    [InlineData("![secret](https://attacker.example/tracker.png)")]
    [InlineData("![secret](data:image/png;base64,xxx)")]
    [InlineData("![secret](/img/../Account/Login.png)")]
    [InlineData("![secret](/img/%2e%2e/private.png)")]
    [InlineData("![secret](/img/preview.svg)")]
    [InlineData("![secret](/img/preview.png?token=private)")]
    public void Unsafe_images_do_not_request_remote_or_dynamic_content(string markdown)
    {
        ReleaseNotes.RenderMarkdown(markdown).Should().NotContain("<img ").And.Contain("image omitted");
    }

    [Fact]
    public void Raw_html_is_text_and_valid_local_images_and_public_links_are_preserved()
    {
        var html = ReleaseNotes.RenderMarkdown("""
            <script>alert(1)</script><img src="https://attacker.example/tracker" onerror="alert(1)">

            [Release](https://github.com/kasuken/Relio) ![Preview](/img/og-preview.png)
            """);
        html.Should().NotContain("<script").And.NotContain(" onerror=\"")
            .And.Contain("&lt;script&gt;").And.Contain("href=\"https://github.com/kasuken/Relio\"")
            .And.Contain("src=\"/img/og-preview.png\"");
    }

    [Fact]
    public void Missing_resource_returns_an_explicit_unavailable_state()
    {
        ReleaseNotes.RenderResource(null, NullLogger.Instance).Should().BeNull();
    }
}
