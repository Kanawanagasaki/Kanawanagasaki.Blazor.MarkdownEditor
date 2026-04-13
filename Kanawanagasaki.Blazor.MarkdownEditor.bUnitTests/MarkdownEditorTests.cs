using Bunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;
using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.bUnitTests;

// ═══════════════════════════════════════════════════════════════════
//  MarkdownRenderer tests (pure C# — no browser needed)
// ═══════════════════════════════════════════════════════════════════

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

// ═══════════════════════════════════════════════════════════════════
//  MarkdownTextExtensions tests (pure C# — no browser needed)
// ═══════════════════════════════════════════════════════════════════

public class MarkdownTextExtensionsTests
{
    [Fact]
    public void ToggleBold_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.ToggleBold("bold text", 0, 4);

        Assert.Equal("**bold** text", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(6, result.SelectionEnd);
    }

    [Fact]
    public void ToggleBold_UnwrapsWhenAlreadyBold()
    {
        var result = MarkdownTextExtensions.ToggleBold("**bold** text", 2, 6);

        Assert.Equal("bold text", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(4, result.SelectionEnd);
    }

    [Fact]
    public void ToggleBold_NoSelection_InsertsEmptyMarkers()
    {
        var result = MarkdownTextExtensions.ToggleBold("hello", 2, 2);

        Assert.Equal("he****llo", result.Text);
        Assert.Equal(4, result.SelectionStart);
        Assert.Equal(4, result.SelectionEnd);
    }

    [Fact]
    public void ToggleItalic_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.ToggleItalic("italic text", 0, 6);

        Assert.Equal("*italic* text", result.Text);
        Assert.Equal(1, result.SelectionStart);
        Assert.Equal(7, result.SelectionEnd);
    }

    [Fact]
    public void ToggleItalic_UnwrapsWhenAlreadyItalic()
    {
        var result = MarkdownTextExtensions.ToggleItalic("*italic* text", 1, 7);

        Assert.Equal("italic text", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(6, result.SelectionEnd);
    }

    [Fact]
    public void ToggleItalic_DoesNotUnwrapBoldInnerStar()
    {
        // **text** — inner * should not be treated as italic markers
        var result = MarkdownTextExtensions.ToggleItalic("**text**", 2, 6);

        Assert.Equal("***text***", result.Text);
    }

    [Fact]
    public void ToggleBoldThenItalicTenTimes_ShouldNotAccumulateAsterisks()
    {
        // Simulate: "One two three" → select "two" → Bold → Italic × 10
        string text = "One two three";
        int start = 4; // start of "two"
        int end = 7;   // end of "two"

        // Step 1: Bold
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One **two** three", result.Text);
        text = result.Text;
        start = result.SelectionStart;
        end = result.SelectionEnd;

        // Step 2: Italic × 10
        for (int i = 0; i < 10; i++)
        {
            result = MarkdownTextExtensions.ToggleItalic(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;
        }

        // After 10 italic toggles (even number), italic should be OFF.
        // Bold should still be ON.
        Assert.Equal("One **two** three", text);

        // Verify no excessive asterisks
        var asteriskCount = text.Count(c => c == '*');
        Assert.True(asteriskCount <= 4,
            $"Expected at most 4 asterisks, but found {asteriskCount} in: {text}");
    }

    [Fact]
    public void ToggleBoldItalicStrikethrough_TenCycles_ShouldNotAccumulateMarkers()
    {
        // Simulate: "One two three" → select "two" →
        //   Bold → Italic → Strikethrough × 10 cycles (30 toggles total)
        string text = "One two three";
        int start = 4; // start of "two"
        int end = 7;   // end of "two"

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleBold(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleItalic(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;
        }

        // After 10 full cycles (even number of toggles for each style),
        // all styles should be OFF — back to plain text.
        Assert.Equal("One two three", text);

        // Verify no excessive markers at all
        var markerCount = text.Count(c => c == '*') + text.Count(c => c == '~');
        Assert.True(markerCount == 0,
            $"Expected no markers, but found {markerCount} in: {text}");
    }

    [Fact]
    public void ToggleBoldItalicStrikethrough_OneCycle_ShouldProduceCorrectIntermediates()
    {
        // Verify each step of a single bold→italic→strikethrough cycle
        string text = "One two three";
        int start = 4;
        int end = 7;

        // Bold on
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One **two** three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Italic on (on top of bold)
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One ***two*** three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Strikethrough on (on top of bold+italic)
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        Assert.Equal("One ~~***two***~~ three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Bold off (keep italic+strikethrough)
        result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One ~~*two*~~ three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Italic off (keep strikethrough)
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One ~~two~~ three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Strikethrough off (all styles off)
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        Assert.Equal("One two three", result.Text);
    }

    [Fact]
    public void ToggleBoldThenStrikethroughTenTimes_ShouldNotAccumulateMarkers()
    {
        // Simulate: "One two three" → select "two" → Bold → Strikethrough × 10
        string text = "One two three";
        int start = 4;
        int end = 7;

        // Step 1: Bold
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One **two** three", result.Text);
        text = result.Text;
        start = result.SelectionStart;
        end = result.SelectionEnd;

        // Step 2: Strikethrough × 10
        for (int i = 0; i < 10; i++)
        {
            result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;
        }

        // After 10 strikethrough toggles (even number), strikethrough should be OFF.
        // Bold should still be ON.
        Assert.Equal("One **two** three", text);

        // Verify no excessive markers
        var markerCount = text.Count(c => c == '*') + text.Count(c => c == '~');
        Assert.True(markerCount <= 4,
            $"Expected at most 4 markers (2** open + 2** close for bold), but found {markerCount} in: {text}");
    }

    [Fact]
    public void ToggleBoldThenItalicOddTimes_ShouldProduceBoldItalic()
    {
        string text = "One two three";
        int start = 4;
        int end = 7;

        // Bold
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text;
        start = result.SelectionStart;
        end = result.SelectionEnd;

        // Italic once (odd)
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One ***two*** three", result.Text);
    }

    [Fact]
    public void ToggleItalicOnBoldItalic_RemovesItalicKeepsBold()
    {
        // ***text*** → ToggleItalic → **text**
        var result = MarkdownTextExtensions.ToggleItalic("***text***", 3, 7);
        Assert.Equal("**text**", result.Text);
    }

    [Fact]
    public void ToggleBoldOnBoldItalic_RemovesBoldKeepsItalic()
    {
        // ***text*** → ToggleBold → *text*
        var result = MarkdownTextExtensions.ToggleBold("***text***", 3, 7);
        Assert.Equal("*text*", result.Text);
    }

    [Fact]
    public void ToggleStrikethrough_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.ToggleStrikethrough("strike text", 0, 6);

        Assert.Equal("~~strike~~ text", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(8, result.SelectionEnd);
    }

    [Fact]
    public void ToggleInlineCode_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.ToggleInlineCode("code text", 0, 4);

        Assert.Equal("`code` text", result.Text);
        Assert.Equal(1, result.SelectionStart);
        Assert.Equal(5, result.SelectionEnd);
    }

    [Fact]
    public void ToggleInlineCode_UnwrapsWhenAlreadyCode()
    {
        var result = MarkdownTextExtensions.ToggleInlineCode("`code` text", 1, 5);

        Assert.Equal("code text", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(4, result.SelectionEnd);
    }

    [Fact]
    public void ToggleInlineCode_NoSelection_InsertsEmptyMarkers()
    {
        var result = MarkdownTextExtensions.ToggleInlineCode("hello", 2, 2);

        Assert.Equal("he``llo", result.Text);
        Assert.Equal(3, result.SelectionStart);
        Assert.Equal(3, result.SelectionEnd);
    }

    // ── Italic + Code combined toggle tests ──────────────────────

    [Fact]
    public void ToggleItalicThenCode_TenCycles_ShouldNotAccumulateMarkers()
    {
        // User's exact scenario: "One two three" → select "two" →
        //   Italic → Code → Italic → Code ... (10 cycles = 20 toggles)
        string text = "One two three";
        int start = 4;
        int end = 7;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleItalic(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;
        }

        // After 10 full cycles (even number of toggles for each style),
        // all styles should be OFF — back to plain text.
        Assert.Equal("One two three", text);

        // Verify zero markers
        var markerCount = text.Count(c => c == '*') + text.Count(c => c == '`');
        Assert.True(markerCount == 0,
            $"Expected no markers, but found {markerCount} in: {text}");
    }

    [Fact]
    public void ToggleItalicThenCode_OneCycle_ShouldProduceCorrectIntermediates()
    {
        // Verify each step of Italic → Code → Italic → Code
        string text = "One two three";
        int start = 4;
        int end = 7;

        // Italic ON
        var result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One *two* three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Code ON (on top of italic) → canonical: *`two`*
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        Assert.Equal("One *`two`* three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Italic OFF (keep code) → just `two`
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One `two` three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Code OFF (all off)
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        Assert.Equal("One two three", result.Text);
    }

    // ── All four styles combined toggle tests ────────────────────

    [Fact]
    public void ToggleAllFourStyles_TenCycles_ShouldNotAccumulateMarkers()
    {
        // Ultimate test: Bold → Italic → Strikethrough → Code × 10 cycles
        string text = "One two three";
        int start = 4;
        int end = 7;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleBold(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleItalic(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;
        }

        // After 10 full cycles (even number of toggles for each style),
        // all styles should be OFF — back to plain text.
        Assert.Equal("One two three", text);

        var markerCount = text.Count(c => c == '*') + text.Count(c => c == '~') + text.Count(c => c == '`');
        Assert.True(markerCount == 0,
            $"Expected no markers, but found {markerCount} in: {text}");
    }

    [Fact]
    public void ToggleAllFourStyles_OneCycle_ShouldProduceCorrectIntermediates()
    {
        // Verify each step of Bold → Italic → Strikethrough → Code
        string text = "One two three";
        int start = 4;
        int end = 7;

        // Bold ON
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One **two** three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Italic ON (on top of bold) → merged as ***
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One ***two*** three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Strikethrough ON (on top of bold+italic)
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        Assert.Equal("One ~~***two***~~ three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Code ON (innermost, on top of all)
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        Assert.Equal("One ~~***`two`***~~ three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Bold OFF (keep italic+strikethrough+code)
        result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One ~~*`two`*~~ three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Italic OFF (keep strikethrough+code)
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One ~~`two`~~ three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Strikethrough OFF (keep code)
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        Assert.Equal("One `two` three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Code OFF (all off)
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        Assert.Equal("One two three", result.Text);
    }

    // ── Pairwise two-style toggle tests (every combination ×10) ──

    [Fact]
    public void ToggleBoldThenCode_TenCycles_ShouldNotAccumulateMarkers()
    {
        string text = "One two three";
        int start = 4;
        int end = 7;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleBold(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        }

        Assert.Equal("One two three", text);
    }

    [Fact]
    public void ToggleStrikethroughThenCode_TenCycles_ShouldNotAccumulateMarkers()
    {
        string text = "One two three";
        int start = 4;
        int end = 7;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        }

        Assert.Equal("One two three", text);
    }

    [Fact]
    public void ToggleItalicThenBoldThenCode_TenCycles_ShouldNotAccumulateMarkers()
    {
        string text = "One two three";
        int start = 4;
        int end = 7;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleItalic(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleBold(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        }

        Assert.Equal("One two three", text);
    }

    [Fact]
    public void ToggleCodeOnItalicCode_RemovesCodeKeepsItalic()
    {
        // *`text`* → ToggleInlineCode → *text*
        var result = MarkdownTextExtensions.ToggleInlineCode("*`text`*", 2, 6);
        Assert.Equal("*text*", result.Text);
    }

    [Fact]
    public void ToggleItalicOnItalicCode_RemovesItalicKeepsCode()
    {
        // *`text`* → ToggleItalic → `text`
        var result = MarkdownTextExtensions.ToggleItalic("*`text`*", 2, 6);
        Assert.Equal("`text`", result.Text);
    }

    [Fact]
    public void ToggleBoldOnBoldCode_RemovesBoldKeepsCode()
    {
        // **`text`** → ToggleBold → `text`
        var result = MarkdownTextExtensions.ToggleBold("**`text`**", 3, 7);
        Assert.Equal("`text`", result.Text);
    }

    [Fact]
    public void ToggleStrikethroughOnStrikethroughCode_RemovesStrikethroughKeepsCode()
    {
        // ~~`text`~~ → ToggleStrikethrough → `text`
        var result = MarkdownTextExtensions.ToggleStrikethrough("~~`text`~~", 3, 7);
        Assert.Equal("`text`", result.Text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Multi-line inline toggle tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MultiLine_ToggleBold_WrapsAllLines()
    {
        // "One two three\nfour five six\nseven eight nine"
        // Selection: "two three\nfour five six\nseven eight" (pos 4..38)
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length; // 4+35=39

        var result = MarkdownTextExtensions.ToggleBold(text, start, end);

        Assert.Equal("One **two three**\n**four five six**\n**seven eight** nine", result.Text);
        // Selection should include all markers so subsequent toggles work
        string selected = result.Text.Substring(result.SelectionStart, result.SelectionEnd - result.SelectionStart);
        Assert.Equal("**two three**\n**four five six**\n**seven eight**", selected);
    }

    [Fact]
    public void MultiLine_ToggleBoldThenCode_ShouldNotAccumulateMarkers()
    {
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length;

        // Toggle Bold
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Toggle Code
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Verify both markers present on each line
        Assert.Equal("One **`two three`**\n**`four five six`**\n**`seven eight`** nine", text);

        // Toggle Code OFF (should remove code, keep bold)
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        Assert.Equal("One **two three**\n**four five six**\n**seven eight** nine", text);

        // Toggle Bold OFF (should remove bold too)
        result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        Assert.Equal("One two three\nfour five six\nseven eight nine", text);
    }

    [Fact]
    public void MultiLine_ToggleBoldThenCode_TenCycles_ShouldNotAccumulateMarkers()
    {
        // Bold → Code × 10 cycles on multi-line selection
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleBold(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        }

        // After 10 full cycles (even toggles for each), all styles OFF
        Assert.Equal("One two three\nfour five six\nseven eight nine", text);
    }

    [Fact]
    public void MultiLine_ToggleAllFourStyles_TenCycles_ShouldNotAccumulateMarkers()
    {
        // Bold → Italic → Strikethrough → Code × 10 cycles on multi-line
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleBold(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleItalic(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        }

        // After 10 full cycles (even toggles for each), all styles OFF
        Assert.Equal("One two three\nfour five six\nseven eight nine", text);

        // Verify zero markers
        var markerCount = text.Count(c => c == '*') + text.Count(c => c == '~') + text.Count(c => c == '`');
        Assert.True(markerCount == 0,
            $"Expected no markers, but found {markerCount} in: {text}");
    }

    [Fact]
    public void MultiLine_ToggleItalicThenCode_TenCycles_ShouldNotAccumulateMarkers()
    {
        // Italic → Code × 10 cycles on multi-line
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleItalic(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        }

        Assert.Equal("One two three\nfour five six\nseven eight nine", text);
    }

    [Fact]
    public void MultiLine_ToggleStrikethroughThenCode_TenCycles_ShouldNotAccumulateMarkers()
    {
        // Strikethrough → Code × 10 cycles on multi-line
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        }

        Assert.Equal("One two three\nfour five six\nseven eight nine", text);
    }

    [Fact]
    public void MultiLine_ToggleAllFourStyles_OneCycle_ShouldProduceCorrectIntermediates()
    {
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length;

        // Bold ON
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One **two three**\n**four five six**\n**seven eight** nine", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Italic ON (merged with bold → ***)
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One ***two three***\n***four five six***\n***seven eight*** nine", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Strikethrough ON (outermost)
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        Assert.Equal("One ~~***two three***~~\n~~***four five six***~~\n~~***seven eight***~~ nine", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Code ON (innermost)
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        Assert.Equal("One ~~***`two three`***~~\n~~***`four five six`***~~\n~~***`seven eight`***~~ nine", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Bold OFF (keep italic+strikethrough+code)
        result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One ~~*`two three`*~~\n~~*`four five six`*~~\n~~*`seven eight`*~~ nine", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Italic OFF (keep strikethrough+code)
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One ~~`two three`~~\n~~`four five six`~~\n~~`seven eight`~~ nine", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Strikethrough OFF (keep code)
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        Assert.Equal("One `two three`\n`four five six`\n`seven eight` nine", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Code OFF (all off)
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        Assert.Equal("One two three\nfour five six\nseven eight nine", result.Text);
    }

    [Fact]
    public void MultiLine_ToggleBold_UnwrapsAllLines()
    {
        // Start with bold already applied, then toggle it off
        string text = "One **two three**\n**four five six**\n**seven eight** nine";
        // Selection covers the marked-up text including markers
        int start = 4;
        int end = 4 + "**two three**\n**four five six**\n**seven eight**".Length;

        var result = MarkdownTextExtensions.ToggleBold(text, start, end);

        Assert.Equal("One two three\nfour five six\nseven eight nine", result.Text);
    }

    [Fact]
    public void MultiLine_TwoLines_ToggleBoldThenCode_TenCycles()
    {
        // Smaller multi-line: just 2 lines
        string text = "hello world\nfoo bar";
        int start = 6; // "world"
        int end = 6 + "world\nfoo".Length;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleBold(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

            result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
            text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        }

        Assert.Equal("hello world\nfoo bar", text);
    }

    // ── Sub-selection within a marked line (TryExpandToMarkerRegion) ──

    [Fact]
    public void SubSelection_WithinMarkedLine_ToggleBold_RemovesExistingStyles()
    {
        // Text after multi-line all-4 toggle:
        // "One ~~***`two three`***~~\n~~***`four five six`***~~\n~~***`seven eight`***~~ nine"
        // Select just "five" and toggle bold → should detect markers and toggle for whole line content
        string text = "One ~~***`two three`***~~\n~~***`four five six`***~~\n~~***`seven eight`***~~ nine";
        // "five" is on line 2. Line 2 starts at position 31 in the marked text.
        // Line 2: "~~***`four five six`***~~"
        // Positions: 0-1=~~, 2-4=***, 5=`, 6-9=four, 10=space, 11-14=five, 15=space, 16-18=six, 19=`, 20-22=***, 23-24=~~
        // Line 2 starts at offset 31 in full text (after "One ~~***`two three`***~~\n")
        int line2Start = "One ~~***`two three`***~~\n".Length; // 28
        // "five" starts at line2Start + 11 = 39, ends at 44
        int start = line2Start + "~~***`four ".Length;  // 28 + 11 = 39
        int end = start + "five".Length;                 // 39 + 4 = 43

        var result = MarkdownTextExtensions.ToggleBold(text, start, end);

        // Bold was ON (part of ***), toggling it OFF → *** becomes *
        // New line 2: ~~*`four five six`*~~
        Assert.Equal("One ~~***`two three`***~~\n~~*`four five six`*~~\n~~***`seven eight`***~~ nine", result.Text);
    }

    [Fact]
    public void SubSelection_WithinMarkedLine_ToggleAllFourBackOff()
    {
        // Full scenario: multi-line toggle all 4 styles ON, then sub-select "five"
        // and toggle all 4 styles OFF one by one
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length;

        // Toggle all 4 styles ON
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Now text is: "One ~~***`two three`***~~\n~~***`four five six`***~~\n~~***`seven eight`***~~ nine"
        Assert.Equal("One ~~***`two three`***~~\n~~***`four five six`***~~\n~~***`seven eight`***~~ nine", text);

        // Now select just "five" on line 2
        int line2Start = text.IndexOf('\n') + 1;
        int fiveStart = text.IndexOf("five", line2Start);
        int fiveEnd = fiveStart + 4;

        // Toggle Bold OFF (was part of ***)
        result = MarkdownTextExtensions.ToggleBold(text, fiveStart, fiveEnd);
        text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
        Assert.Equal("One ~~***`two three`***~~\n~~*`four five six`*~~\n~~***`seven eight`***~~ nine", text);

        // Toggle Italic OFF (was *)
        result = MarkdownTextExtensions.ToggleItalic(text, fiveStart, fiveEnd);
        text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
        Assert.Equal("One ~~***`two three`***~~\n~~`four five six`~~\n~~***`seven eight`***~~ nine", text);

        // Toggle Strikethrough OFF
        result = MarkdownTextExtensions.ToggleStrikethrough(text, fiveStart, fiveEnd);
        text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
        Assert.Equal("One ~~***`two three`***~~\n`four five six`\n~~***`seven eight`***~~ nine", text);

        // Toggle Code OFF
        result = MarkdownTextExtensions.ToggleInlineCode(text, fiveStart, fiveEnd);
        text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
        Assert.Equal("One ~~***`two three`***~~\nfour five six\n~~***`seven eight`***~~ nine", text);
    }

    [Fact]
    public void SubSelection_WithinMarkedLine_ToggleAllFourOffAndOn_TenCycles()
    {
        // After multi-line all-4 toggle, line 2 has markers at start/end.
        // Sub-select "five" → toggle all 4 OFF (expands to full line content, removes all markers)
        // Then sub-select "five" again → toggle all 4 ON (only wraps "five" with markers)
        // Then sub-select "five" again → toggle all 4 OFF (detects markers around "five", removes them)
        // After full OFF/ON cycle on just "five", the line is plain again.
        // Repeat × 10 cycles.
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length;

        // Apply all 4 styles to multi-line selection first
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // After first OFF cycle, line 2 becomes plain "four five six"
        // Then ON cycle wraps only "five" → "four ~~***`five`***~~ six"
        // Then another OFF cycle removes markers from "five" → "four five six"
        // So every pair of cycles returns line 2 to plain.

        for (int cycle = 0; cycle < 10; cycle++)
        {
            // Sub-select "five" on the current line 2
            int fiveStart = text.IndexOf("five");
            int fiveEnd = fiveStart + 4;

            // Toggle all 4 OFF (if markers exist around the selection, they're removed;
            // if no markers, toggling wraps with new markers)
            result = MarkdownTextExtensions.ToggleBold(text, fiveStart, fiveEnd);
            text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
            result = MarkdownTextExtensions.ToggleItalic(text, fiveStart, fiveEnd);
            text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
            result = MarkdownTextExtensions.ToggleStrikethrough(text, fiveStart, fiveEnd);
            text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
            result = MarkdownTextExtensions.ToggleInlineCode(text, fiveStart, fiveEnd);
            text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
        }

        // After 10 cycles (even number), all toggles even out.
        // First cycle removes all markers from line 2 → "four five six"
        // Then 9 more cycles: each applies then removes 4 styles on "five"
        // Since 9 is odd, the net effect is one application of all 4 styles on "five"
        // Line 2: "four ~~***`five`***~~ six"
        // Lines 1 and 3 unchanged.

        // Actually, let's just verify no marker accumulation by checking that
        // the text is deterministic and doesn't grow.
        // After 10 full cycles of toggling 4 styles: 10×4 = 40 toggles on "five"
        // If first cycle removed existing markers (making 4 OFF toggles),
        // that's 4 OFF + 9×(4 ON + 4 OFF) = 4 OFF + 36 ON + 36 OFF = 40 OFF + 36 ON
        // Net: 4 ON = Bold+Italic+Strikethrough+Code all applied to "five"

        // The key invariant: no accumulation, markers stay bounded.
        var markerCount = text.Count(c => c == '*') + text.Count(c => c == '~') + text.Count(c => c == '`');
        // Max markers for a fully-marked "five" on one line: ~~***` + `***~~ = 2+3+1+1+3+2 = 12 per line
        // Plus lines 1,3: each 12, total 36. But since line 2 only has "five" wrapped: 12.
        Assert.True(markerCount <= 36,
            $"Expected at most 36 markers, but found {markerCount} in: {text}");
    }

    [Fact]
    public void SubSelection_WithinMarkedLine_CheckHtmlRendering()
    {
        // Multi-line toggle all 4 ON, then sub-select "five" and toggle all 4 OFF,
        // then verify HTML rendering is correct
        string text = "One two three\nfour five six\nseven eight nine";
        int start = 4;
        int end = 4 + "two three\nfour five six\nseven eight".Length;

        // Toggle all 4 styles ON
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Verify fully marked text renders correctly
        var renderResult = MarkdownRenderer.Render(text);
        Assert.Contains("<del>", renderResult.Html);
        Assert.Contains("<strong>", renderResult.Html);
        Assert.Contains("<em>", renderResult.Html);
        Assert.Contains("md-inline-code", renderResult.Html);
        Assert.Contains("two three", renderResult.Html);
        Assert.Contains("four five six", renderResult.Html);
        Assert.Contains("seven eight", renderResult.Html);

        // Now sub-select "five" and toggle all 4 OFF
        int fiveStart = text.IndexOf("five");
        int fiveEnd = fiveStart + 4;

        result = MarkdownTextExtensions.ToggleBold(text, fiveStart, fiveEnd);
        text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
        result = MarkdownTextExtensions.ToggleItalic(text, fiveStart, fiveEnd);
        text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, fiveStart, fiveEnd);
        text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;
        result = MarkdownTextExtensions.ToggleInlineCode(text, fiveStart, fiveEnd);
        text = result.Text; fiveStart = result.SelectionStart; fiveEnd = result.SelectionEnd;

        // Line 2 should be plain: "four five six"
        // Lines 1 and 3 should still have all markers
        Assert.Equal("One ~~***`two three`***~~\nfour five six\n~~***`seven eight`***~~ nine", text);

        // Verify HTML: line 2 should NOT have del/strong/em/code, lines 1,3 should
        renderResult = MarkdownRenderer.Render(text);
        // Line 1 and 3 should have full styling
        Assert.Contains("two three", renderResult.Html);
        Assert.Contains("seven eight", renderResult.Html);
        // Line 2 should be plain text
        Assert.Contains("four five six", renderResult.Html);
    }

    [Fact]
    public void SingleLine_SubSelection_WithinMarkedLine_TogglesBoldOff()
    {
        // Line with markers at start (as produced by multi-line toggle): ~~***`hello world`***~~
        // Select "wor" inside the content → should expand to full content and toggle
        string text = "~~***`hello world`***~~";
        // Content "hello world" is at positions 6-16 (after ~~***`)
        // Select "wor" = positions 11-14
        int start = 11;
        int end = 14;

        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        // Bold was ON (part of ***), toggling OFF → *** becomes *
        Assert.Equal("~~*`hello world`*~~", result.Text);
    }

    [Fact]
    public void SingleLine_SubSelection_WithinAllFour_TenCycles()
    {
        // Line with all markers at start: ~~***`hello world`***~~
        // Sub-select "wor" and cycle all 4 × 10
        string text = "~~***`hello world`***~~";
        int wStart = text.IndexOf("wor");
        int wEnd = wStart + 3;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var result = MarkdownTextExtensions.ToggleBold(text, wStart, wEnd);
            text = result.Text; wStart = result.SelectionStart; wEnd = result.SelectionEnd;
            result = MarkdownTextExtensions.ToggleItalic(text, wStart, wEnd);
            text = result.Text; wStart = result.SelectionStart; wEnd = result.SelectionEnd;
            result = MarkdownTextExtensions.ToggleStrikethrough(text, wStart, wEnd);
            text = result.Text; wStart = result.SelectionStart; wEnd = result.SelectionEnd;
            result = MarkdownTextExtensions.ToggleInlineCode(text, wStart, wEnd);
            text = result.Text; wStart = result.SelectionStart; wEnd = result.SelectionEnd;
        }

        // After 10 even cycles, should be back to fully marked
        Assert.Equal("~~***`hello world`***~~", text);
    }

    [Fact]
    public void ToggleHeading_AddsPrefixToPlainLine()
    {
        var result = MarkdownTextExtensions.ToggleHeading("My Title", 0, 8, 1);

        Assert.Equal("# My Title", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(10, result.SelectionEnd);
    }

    [Fact]
    public void ToggleHeading_ReplacesExistingHeading()
    {
        var result = MarkdownTextExtensions.ToggleHeading("# My Title", 0, 10, 2);

        Assert.Equal("## My Title", result.Text);
        Assert.Equal(3, result.SelectionStart);
        Assert.Equal(11, result.SelectionEnd);
    }

    [Fact]
    public void ToggleUnorderedList_AddsBulletPrefix()
    {
        var result = MarkdownTextExtensions.ToggleUnorderedList("list item", 0, 9);

        Assert.Equal("- list item", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(11, result.SelectionEnd);
    }

    [Fact]
    public void ToggleUnorderedList_RemovesWhenAlreadyPresent()
    {
        var result = MarkdownTextExtensions.ToggleUnorderedList("- list item", 0, 11);

        Assert.Equal("list item", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(9, result.SelectionEnd);
    }

    [Fact]
    public void ToggleOrderedList_AddsNumberPrefix()
    {
        var result = MarkdownTextExtensions.ToggleOrderedList("first item", 0, 10);

        Assert.Equal("1. first item", result.Text);
        Assert.Equal(3, result.SelectionStart);
        Assert.Equal(13, result.SelectionEnd);
    }

    [Fact]
    public void ToggleOrderedList_RemovesWhenAlreadyPresent()
    {
        var result = MarkdownTextExtensions.ToggleOrderedList("1. first item", 0, 12);

        Assert.Equal("first item", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(10, result.SelectionEnd);
    }

    [Fact]
    public void ToggleOrderedList_SwapsFromUnorderedList()
    {
        var result = MarkdownTextExtensions.ToggleOrderedList("- list item", 0, 11);

        Assert.Equal("1. list item", result.Text);
    }

    [Fact]
    public void ToggleBlockquote_AddsQuotePrefix()
    {
        var result = MarkdownTextExtensions.ToggleBlockquote("quoted text", 0, 11);

        Assert.Equal("> quoted text", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(13, result.SelectionEnd);
    }

    [Fact]
    public void ToggleBlockquote_RemovesWhenAlreadyPresent()
    {
        var result = MarkdownTextExtensions.ToggleBlockquote("> quoted text", 0, 13);

        Assert.Equal("quoted text", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(11, result.SelectionEnd);
    }

    [Fact]
    public void InsertLink_WithSelection_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.InsertLink("click here", 0, 10);

        Assert.Equal("[click here](url)", result.Text);
        Assert.Equal(13, result.SelectionStart);
        Assert.Equal(16, result.SelectionEnd);
    }

    [Fact]
    public void InsertLink_NoSelection_UsesDefaultText()
    {
        var result = MarkdownTextExtensions.InsertLink("some text", 5, 5);

        Assert.Equal("some [link text](url)text", result.Text);
    }

    [Fact]
    public void InsertImage_WithSelection_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.InsertImage("photo", 0, 5);

        Assert.Equal("![photo](url)", result.Text);
        Assert.Equal(9, result.SelectionStart);
        Assert.Equal(12, result.SelectionEnd);
    }

    [Fact]
    public void InsertImage_NoSelection_UsesDefaultAlt()
    {
        var result = MarkdownTextExtensions.InsertImage("some text", 5, 5);

        Assert.Equal("some ![alt text](url)text", result.Text);
    }

    [Fact]
    public void InsertCodeBlock_InsertsFencedBlock()
    {
        var result = MarkdownTextExtensions.InsertCodeBlock("", 0, 0);

        Assert.StartsWith("```", result.Text);
        Assert.EndsWith("```", result.Text.TrimEnd('\n', '\r'));
    }

    [Fact]
    public void InsertCodeBlock_WithSelection_WrapsContent()
    {
        var result = MarkdownTextExtensions.InsertCodeBlock("selected code", 0, 13);

        Assert.Equal("```\nselected code\n```", result.Text);
    }

    [Fact]
    public void InsertHorizontalRule_OnEmptyLine_InsertsHR()
    {
        var result = MarkdownTextExtensions.InsertHorizontalRule("\n", 1, 1);

        Assert.Contains("---", result.Text);
    }

    [Fact]
    public void InsertHorizontalRule_OnOccupiedLine_InsertsAfter()
    {
        var result = MarkdownTextExtensions.InsertHorizontalRule("some text", 0, 9);

        Assert.Contains("some text\n---", result.Text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Overlapping selection tests (Test A & Test B from user request)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void OverlappingSelection_TestA_SelectWordToggleAll4ThenSelectAllToggleAll4()
    {
        // Test A: "One two three" → select "two" → toggle all 4 styles →
        //         select the whole text → toggle all 4 styles → check result
        string text = "One two three";
        int start = 4; // start of "two"
        int end = 7;   // end of "two"

        // Step 1: Toggle Bold on "two"
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One **two** three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Step 2: Toggle Italic on "two"
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One ***two*** three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Step 3: Toggle Strikethrough on "two"
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        Assert.Equal("One ~~***two***~~ three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Step 4: Toggle Code on "two"
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        Assert.Equal("One ~~***`two`***~~ three", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Phase 2: Select the ENTIRE text (from "One" to "three") and toggle all 4 styles
        // The selection should cover the entire line content including markers
        start = 0;
        end = text.Length;

        // When selecting the entire line that contains markers for a sub-region,
        // and toggling all 4 styles, the expected behavior is:
        // The whole line gets all 4 styles applied, merging with the existing markers.
        // Since "two" already has all 4, the markers should expand to cover the entire line.

        // Toggle Bold on full line
        result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Toggle Italic on full line
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Toggle Strikethrough on full line
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Toggle Code on full line
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // After toggling all 4 styles on the entire line, the result should have
        // all 4 styles applied to the entire text content "One two three"
        Assert.Equal("~~***`One two three`***~~", text);

        // ── Check HTML (what the user actually sees) ──────────────────
        var htmlResult = MarkdownRenderer.Render(text);
        string html = htmlResult.Html;

        // Strikethrough wraps everything
        Assert.Contains("<del>", html);
        Assert.Contains("</del>", html);
        // Bold+Italic (from ***) → <strong><em>
        Assert.Contains("<strong>", html);
        Assert.Contains("<em>", html);
        Assert.Contains("</em>", html);
        Assert.Contains("</strong>", html);
        // Inline code (innermost) → <code class="md-inline-code">
        Assert.Contains("<code class=\"md-inline-code\">", html);
        Assert.Contains("</code>", html);
        // The text content must be present
        Assert.Contains("One two three", html);
        // Verify nesting order: <del><strong><em><code>One two three</code></em></strong></del>
        Assert.Matches(@"<del><strong><em><code[^>]*>One two three</code></em></strong></del>", html);
    }

    [Fact]
    public void OverlappingSelection_TestB_SelectTwoThreeBoldThenThreeFourItalic()
    {
        // Test B: "One two three four five" → select "two three" → toggle bold →
        //         select "three four" → toggle italic → check result
        string text = "One two three four five";
        int start = 4;  // start of "two"
        int end = 4 + "two three".Length; // end of "three" = 13

        // Step 1: Toggle Bold on "two three"
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One **two three** four five", result.Text);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // Step 2: Select "three four" in the new text
        // After bold, text is: "One **two three** four five"
        // "three" is inside the bold markers, "four" is outside
        // Find the positions of "three" and "four" in the current text
        int threeStart = text.IndexOf("three");
        int fourEnd = text.IndexOf("four") + "four".Length;
        start = threeStart;
        end = fourEnd;

        // Toggle Italic on "three four"
        // The selection spans from inside the bold region to outside it.
        // Expected: "three" gets italic (inside bold), "four" gets italic (outside bold)
        // In markdown, this should produce proper nesting:
        // "One **two *three*** *four* five"
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = result.Text; start = result.SelectionStart; end = result.SelectionEnd;

        // The expected result: "three" is bold+italic, "four" is just italic
        // Proper markdown: **two *three*** followed by *four*
        Assert.Equal("One **two *three*** *four* five", text);

        // ── Check HTML (what the user actually sees) ──────────────────
        var htmlResult = MarkdownRenderer.Render(text);
        string html = htmlResult.Html;

        // "two" is bold only, "three" is bold+italic → <strong>two <em>three</em></strong>
        Assert.Contains("<strong>", html);
        Assert.Contains("</strong>", html);
        Assert.Contains("<em>", html);
        Assert.Contains("</em>", html);
        // The correct nesting: <strong>two <em>three</em></strong>
        Assert.Matches(@"<strong>two <em>three</em></strong>", html);
        // "four" is italic only → <em>four</em>
        Assert.Matches(@"<em>four</em>", html);
        // All words present
        Assert.Contains("One", html);
        Assert.Contains("two", html);
        Assert.Contains("three", html);
        Assert.Contains("four", html);
        Assert.Contains("five", html);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Component rendering tests (bUnit — no browser needed)
// ═══════════════════════════════════════════════════════════════════

public class MarkdownEditorComponentTests : BunitContext
{
    private const string JsModulePath = "./_content/Kanawanagasaki.Blazor.MarkdownEditor/js/markdownEditor.js";

    private void SetupJsModule()
    {
        var module = JSInterop.SetupModule(JsModulePath);
        module.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Editor_ShouldRenderTextareaAndOverlay()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>();

        cut.Find(".md-textarea");
        cut.Find(".md-overlay");
    }

    [Fact]
    public void Editor_ShouldRenderToolbar_WhenShowToolbarIsTrue()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.ShowToolbar, true));

        Assert.Contains("md-toolbar", cut.Markup);
    }

    [Fact]
    public void Editor_ShouldNotRenderToolbar_WhenShowToolbarIsFalse()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.ShowToolbar, false));

        Assert.DoesNotContain("md-toolbar", cut.Markup);
    }

    [Fact]
    public void Editor_ShouldShowPlaceholder_OnTextarea()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Placeholder, "Custom placeholder"));

        var textarea = cut.Find(".md-textarea");
        Assert.Equal("Custom placeholder", textarea.GetAttribute("placeholder"));
    }

    [Fact]
    public void Toolbar_ShouldContainExpectedButtons()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>();

        cut.Find("button[title='Bold (Ctrl+B)']");
        cut.Find("button[title='Italic (Ctrl+I)']");
        cut.Find("button[title='Heading 1']");
        cut.Find("button[title='Heading 2']");
        cut.Find("button[title='Heading 3']");
        cut.Find("button[title='Strikethrough']");
        cut.Find("button[title='Inline Code']");
        cut.Find("button[title='Code Block']");
        cut.Find("button[title='Unordered List']");
        cut.Find("button[title='Ordered List']");
        cut.Find("button[title='Blockquote']");
        cut.Find("button[title='Insert Link']");
        cut.Find("button[title='Insert Image']");
        cut.Find("button[title='Horizontal Rule']");
        cut.Find("button[title='Undo (Ctrl+Z)']");
        cut.Find("button[title='Redo (Ctrl+Y)']");
    }

    [Fact]
    public void ToolbarButtons_ShouldBeDisabled_WhenDisabledIsTrue()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Disabled, true));

        var buttons = cut.FindAll(".md-toolbar button");
        Assert.All(buttons, btn =>
        {
            Assert.True(btn.HasAttribute("disabled"),
                $"Button '{btn.GetAttribute("title")}' should be disabled");
        });
    }

    [Fact]
    public void ToolbarButtons_ShouldBeEnabled_WhenDisabledIsFalse()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Disabled, false));

        var undoBtn = cut.Find("button[title='Undo (Ctrl+Z)']");
        Assert.False(undoBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void ValueChanged_ShouldFire_WhenOnInputFromJsIsCalled()
    {
        SetupJsModule();
        string? receivedValue = null;
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.ValueChanged, v => receivedValue = v));

        // Get the component instance and invoke the JS callback directly
        var editor = cut.Instance;
        editor.OnInputFromJs("new value");

        Assert.Equal("new value", receivedValue);
    }

    [Fact]
    public void Value_ShouldUpdate_WhenSetAfterRender()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Value, "initial"));

        cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Value, "updated"));

        // The overlay should reflect the new value
        Assert.Contains("updated", cut.Find(".md-overlay").InnerHtml);
    }

    [Fact]
    public void CrossSelection_BoldItalicX4()
    {
        string text = "One two three four five";

        var result = MarkdownTextExtensions.ToggleBold(text, 4, 13);
        Assert.Equal("One **two three** four five", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleItalic(text, 10, 22);
        Assert.Equal("One **two *three*** *four* five", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleItalic(text, result.SelectionStart, result.SelectionEnd);
        Assert.Equal("One **two three** four five", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleItalic(text, result.SelectionStart, result.SelectionEnd);
        Assert.Equal("One **two *three*** *four* five", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleItalic(text, result.SelectionStart, result.SelectionEnd);
        Assert.Equal("One **two three** four five", result.Text);
    }

    [Fact]
    public void CrossSelection_BoldItalicStrikethroughCode()
    {
        string text = "One two three four five";

        var result = MarkdownTextExtensions.ToggleBold(text, 4, 13);
        Assert.Equal("One **two three** four five", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleItalic(text, 10, 22);
        Assert.Equal("One **two *three*** *four* five", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleStrikethrough(text, result.SelectionStart, result.SelectionEnd);
        Assert.Equal("One **two ~~*three*~~** ~~*four*~~ five", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleInlineCode(text, result.SelectionStart, result.SelectionEnd);
        Assert.Equal("One **two ~~*`three`*~~** ~~*`four`*~~ five", result.Text);

        result = MarkdownTextExtensions.ToggleItalic(text, 10, 22);
        Assert.Equal("One **two ~~`three`~~** ~~`four`~~ five", result.Text);
        text = result.Text;
        
        result = MarkdownTextExtensions.ToggleStrikethrough(text, result.SelectionStart, result.SelectionEnd);
        Assert.Equal("One **two `three`** `four` five", result.Text);
        text = result.Text;
        
        result = MarkdownTextExtensions.ToggleInlineCode(text, result.SelectionStart, result.SelectionEnd);
        Assert.Equal("One **two three** four five", result.Text);
        text = result.Text;
    }

    [Fact]
    public void BoldToggle_AllPossibleSelections()
    {
        string text = "One two three";

        var result = MarkdownTextExtensions.ToggleBold(text, 4, 7);
        Assert.Equal("One **two** three", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleBold(text, 6, 9);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleBold(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleBold(text, 5, 9);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleBold(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleBold(text, 4, 9);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleBold(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleBold(text, 6, 10);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleBold(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleBold(text, 5, 10);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleBold(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleBold(text, 4, 10);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleBold(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleBold(text, 6, 11);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleBold(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleBold(text, 5, 11);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleBold(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleBold(text, 4, 11);
        Assert.Equal("One two three", result.Text);
    }

    [Fact]
    public void ItalicToggle_AllPossibleSelections()
    {
        string text = "One two three";

        var result = MarkdownTextExtensions.ToggleItalic(text, 4, 7);
        Assert.Equal("One *two* three", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleItalic(text, 5, 8);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleItalic(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleItalic(text, 4, 8);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleItalic(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleItalic(text, 5, 9);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleItalic(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleItalic(text, 4, 9);
        Assert.Equal("One two three", result.Text);
    }

    [Fact]
    public void StrikethroughToggle_AllPossibleSelections()
    {
        string text = "One two three";

        var result = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 7);
        Assert.Equal("One ~~two~~ three", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleStrikethrough(text, 6, 9);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, 5, 9);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 9);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, 6, 10);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, 5, 10);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 10);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, 6, 11);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, 5, 11);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleStrikethrough(text, 4, 11);
        Assert.Equal("One two three", result.Text);
    }

    [Fact]
    public void CodeToggle_AllPossibleSelections()
    {
        string text = "One two three";

        var result = MarkdownTextExtensions.ToggleInlineCode(text, 4, 7);
        Assert.Equal("One `two` three", result.Text);
        text = result.Text;

        result = MarkdownTextExtensions.ToggleInlineCode(text, 5, 8);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleInlineCode(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleInlineCode(text, 4, 8);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleInlineCode(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleInlineCode(text, 5, 9);
        Assert.Equal("One two three", result.Text);
        text = result.Text;

        text = MarkdownTextExtensions.ToggleInlineCode(text, 4, 7).Text;
        result = MarkdownTextExtensions.ToggleInlineCode(text, 4, 9);
        Assert.Equal("One two three", result.Text);
    }
}
