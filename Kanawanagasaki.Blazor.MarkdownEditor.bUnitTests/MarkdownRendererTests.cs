using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.bUnitTests;

/// <summary>
/// Tests for the MarkdownRenderer: markdown → HTML + position mapping.
/// Pure C# tests — no browser needed.
/// </summary>
public class MarkdownRendererTests
{
    [Fact]
    public void EmptyString_ReturnsEmptyResult()
    {
        var result = MarkdownRenderer.Render("");

        Assert.Equal("", result.Html);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void NullInput_ReturnsEmptyResult()
    {
        var result = MarkdownRenderer.Render(null!);

        Assert.Equal("", result.Html);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void PlainText_RendersAsParagraph()
    {
        var result = MarkdownRenderer.Render("Hello world");

        Assert.Contains("Hello world", result.Html);
        Assert.Contains("md-line", result.Html);
        Assert.Single(result.Lines);
        Assert.Equal(0, result.Lines[0].SourceStart);
    }

    [Fact]
    public void Heading1_RendersH1Element()
    {
        var result = MarkdownRenderer.Render("# Title");

        Assert.Contains("<h1", result.Html);
        Assert.Contains("md-h1", result.Html);
        Assert.Contains("Title", result.Html);
        Assert.Single(result.Lines);
    }

    [Fact]
    public void Heading2_RendersH2Element()
    {
        var result = MarkdownRenderer.Render("## Subtitle");

        Assert.Contains("<h2", result.Html);
        Assert.Contains("md-h2", result.Html);
        Assert.Contains("Subtitle", result.Html);
    }

    [Fact]
    public void Heading3_RendersH3Element()
    {
        var result = MarkdownRenderer.Render("### Section");

        Assert.Contains("<h3", result.Html);
        Assert.Contains("md-h3", result.Html);
        Assert.Contains("Section", result.Html);
    }

    [Fact]
    public void Bold_RendersStrongElement()
    {
        var result = MarkdownRenderer.Render("**bold text**");

        Assert.Contains("<strong>", result.Html);
        Assert.Contains("</strong>", result.Html);
        Assert.Contains("bold text", result.Html);
    }

    [Fact]
    public void Italic_RendersEmElement()
    {
        var result = MarkdownRenderer.Render("*italic text*");

        Assert.Contains("<em>", result.Html);
        Assert.Contains("</em>", result.Html);
        Assert.Contains("italic text", result.Html);
    }

    [Fact]
    public void Strikethrough_RendersDelElement()
    {
        var result = MarkdownRenderer.Render("~~deleted~~");

        Assert.Contains("<del>", result.Html);
        Assert.Contains("</del>", result.Html);
        Assert.Contains("deleted", result.Html);
    }

    [Fact]
    public void InlineCode_RendersCodeElement()
    {
        var result = MarkdownRenderer.Render("`code`");

        Assert.Contains("md-inline-code", result.Html);
        Assert.Contains("code", result.Html);
    }

    [Fact]
    public void FencedCodeBlock_RendersPreCode()
    {
        var result = MarkdownRenderer.Render("```\ncode line\n```");

        Assert.Contains("<pre", result.Html);
        Assert.Contains("md-codeblock", result.Html);
        Assert.Contains("code line", result.Html);
    }

    [Fact]
    public void UnorderedList_RendersBulletMarker()
    {
        var result = MarkdownRenderer.Render("- list item");

        Assert.Contains("md-li-marker", result.Html);
        Assert.Contains("list item", result.Html);
    }

    [Fact]
    public void OrderedList_RendersNumberMarker()
    {
        var result = MarkdownRenderer.Render("1. first item");

        Assert.Contains("md-oli-marker", result.Html);
        Assert.Contains("first item", result.Html);
    }

    [Fact]
    public void Blockquote_RendersBlockquoteElement()
    {
        var result = MarkdownRenderer.Render("> quoted text");

        Assert.Contains("<blockquote", result.Html);
        Assert.Contains("md-bq", result.Html);
        Assert.Contains("quoted text", result.Html);
    }

    [Fact]
    public void Link_RendersAnchorElement()
    {
        var result = MarkdownRenderer.Render("[click here](https://example.com)");

        Assert.Contains("<a", result.Html);
        Assert.Contains("href=\"https://example.com\"", result.Html);
        Assert.Contains("click here", result.Html);
    }

    [Fact]
    public void Image_RendersImgElement()
    {
        var result = MarkdownRenderer.Render("![alt text](https://example.com/img.png)");

        Assert.Contains("<img", result.Html);
        Assert.Contains("alt=\"alt text\"", result.Html);
        Assert.Contains("src=\"https://example.com/img.png\"", result.Html);
    }

    [Fact]
    public void HorizontalRule_RendersHrElement()
    {
        var result = MarkdownRenderer.Render("---");

        Assert.Contains("<hr", result.Html);
        Assert.Contains("md-hr", result.Html);
    }

    [Fact]
    public void MultipleLines_ProducesCorrectLineCount()
    {
        var markdown = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        var result = MarkdownRenderer.Render(markdown);

        Assert.Equal(5, result.Lines.Length);
    }

    [Fact]
    public void LineMappings_HaveCorrectSourceStarts()
    {
        var markdown = "aaa\nbbb\nccc";
        var result = MarkdownRenderer.Render(markdown);

        Assert.Equal(0, result.Lines[0].SourceStart);
        Assert.Equal(4, result.Lines[1].SourceStart);
        Assert.Equal(8, result.Lines[2].SourceStart);
    }

    [Fact]
    public void BoldSyntax_NotIncludedInVisibleToSource()
    {
        var result = MarkdownRenderer.Render("**bold**");

        // "bold" should map to source positions 2,3,4,5 (skipping the ** markers)
        var mapping = result.Lines[0].VisibleToSource;
        Assert.Equal(4, mapping.Length);
        Assert.Equal(2, mapping[0]);
        Assert.Equal(3, mapping[1]);
        Assert.Equal(4, mapping[2]);
        Assert.Equal(5, mapping[3]);
    }

    [Fact]
    public void EmptyLine_HasEmptyMapping()
    {
        var result = MarkdownRenderer.Render("hello\n\nworld");

        Assert.Equal(3, result.Lines.Length);
        Assert.Empty(result.Lines[1].VisibleToSource);
        Assert.Contains("md-empty", result.Html);
    }

    [Fact]
    public void SpecialCharacters_EscapedInOutput()
    {
        var result = MarkdownRenderer.Render("foo <bar> & baz");

        Assert.Contains("&lt;", result.Html);
        Assert.Contains("&gt;", result.Html);
        Assert.Contains("&amp;", result.Html);
        Assert.DoesNotContain("<bar>", result.Html);
    }

    [Fact]
    public void BoldItalic_ThreeStarSyntax()
    {
        var result = MarkdownRenderer.Render("***bolditalic***");

        Assert.Contains("<strong>", result.Html);
        Assert.Contains("<em>", result.Html);
        Assert.Contains("bolditalic", result.Html);
    }
}
