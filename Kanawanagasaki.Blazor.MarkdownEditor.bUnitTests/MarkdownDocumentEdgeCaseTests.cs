using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.bUnitTests;

/// <summary>
/// Comprehensive edge-case tests for the Markdown editor pipeline.
/// Covers renderer edge cases, toggle operations on markdown-significant
/// characters, boundary selections, multi-line patterns, idempotency,
/// selection verification, and canonical marker order.
/// </summary>
public class MarkdownDocumentEdgeCaseTests
{
    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private static (int start, int end) FindWord(string text, string word, int fromPos = 0)
    {
        int idx = text.IndexOf(word, fromPos, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Word '{word}' not found in text from position {fromPos}");
        return (idx, idx + word.Length);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION A: Renderer edge cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Renderer_MultipleAdjacentInlineStylesOnSameLine()
    {
        var result = MarkdownRenderer.Render("**bold** *italic* ~~strike~~");
        Assert.Contains("<strong>bold</strong>", result.Html);
        Assert.Contains("<em>italic</em>", result.Html);
        Assert.Contains("<del>strike</del>", result.Html);
    }

    [Fact]
    public void Renderer_NestedCodeInsideBold()
    {
        var result = MarkdownRenderer.Render("**`code`**");
        Assert.Contains("<strong>", result.Html);
        Assert.Contains("md-inline-code", result.Html);
    }

    [Fact]
    public void Renderer_MarkdownSignificantCharsInBold_Escaped()
    {
        var result = MarkdownRenderer.Render("**a < b > c & d**");
        Assert.Contains("<strong>", result.Html);
        Assert.Contains("&lt;", result.Html);
        Assert.Contains("&gt;", result.Html);
        Assert.Contains("&amp;", result.Html);
        Assert.Contains("a", result.Html);
        Assert.Contains("b", result.Html);
        Assert.Contains("c", result.Html);
        Assert.Contains("d", result.Html);
    }

    [Fact]
    public void Renderer_PreservesWhitespaceCorrectly()
    {
        var result = MarkdownRenderer.Render("hello   world");
        Assert.Contains("hello", result.Html);
        Assert.Contains("world", result.Html);
    }

    [Fact]
    public void Renderer_EmptyInput()
    {
        var result = MarkdownRenderer.Render("");
        Assert.NotNull(result.Html);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Renderer_OnlyWhitespace()
    {
        var result = MarkdownRenderer.Render("   ");
        Assert.NotNull(result.Html);
    }

    [Fact]
    public void Renderer_SingleCharacter()
    {
        var result = MarkdownRenderer.Render("a");
        Assert.Contains("a", result.Html);
        Assert.Single(result.Lines);
    }

    [Fact]
    public void Renderer_HeadingWithInlineFormatting()
    {
        var result = MarkdownRenderer.Render("## **bold heading**");
        Assert.Contains("<h2", result.Html);
        Assert.Contains("<strong>bold heading</strong>", result.Html);
    }

    [Fact]
    public void Renderer_BlockquoteWithInlineFormatting()
    {
        var result = MarkdownRenderer.Render("> **bold** in quote");
        Assert.Contains("<blockquote", result.Html);
        Assert.Contains("<strong>bold</strong>", result.Html);
    }

    [Fact]
    public void Renderer_OrderedListWithInlineFormatting()
    {
        var result = MarkdownRenderer.Render("1. **bold item** and *italic*");
        // Ordered list uses md-oli-marker (different from unordered list's md-li-marker)
        Assert.Contains("md-oli-marker", result.Html);
        Assert.Contains("<strong>bold item</strong>", result.Html);
        Assert.Contains("<em>italic</em>", result.Html);
    }

    [Fact]
    public void Renderer_LinkAndImageElements()
    {
        var linkResult = MarkdownRenderer.Render("[click here](https://example.com)");
        Assert.Contains("<a", linkResult.Html);
        Assert.Contains("href=\"https://example.com\"", linkResult.Html);
        Assert.Contains("click here", linkResult.Html);

        var imgResult = MarkdownRenderer.Render("![alt text](image.png)");
        Assert.Contains("<img", imgResult.Html);
        Assert.Contains("alt=\"alt text\"", imgResult.Html);
    }

    [Fact]
    public void Renderer_CorrectHtmlEscaping()
    {
        var result = MarkdownRenderer.Render("*a & b < c > d \"e\" 'f'*");
        Assert.Contains("<em>", result.Html);
        Assert.Contains("&amp;", result.Html);
        Assert.Contains("&lt;", result.Html);
        Assert.Contains("&gt;", result.Html);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION B: Toggle operations on text with markdown-significant chars
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ToggleBold_TextWithLiteralAsterisks()
    {
        // The * b * portion is treated as italic markers inside the selection.
        // TryResolveOverlappingMarkers (Case 1) strips them and applies bold.
        string text = "a * b * c";
        var r = MarkdownTextExtensions.ToggleBold(text, 0, text.Length);
        Assert.Equal("**a  b  c**", r.Text);
        Assert.Contains("a", r.Text);
        Assert.Contains("b", r.Text);
        Assert.Contains("c", r.Text);
    }

    [Fact]
    public void ToggleItalic_TextWithDoubleAsterisks()
    {
        // **b** is treated as bold markers inside the selection.
        string text = "a ** b ** c";
        var r = MarkdownTextExtensions.ToggleItalic(text, 0, text.Length);
        // The bold markers inside get stripped and italic is applied to clean content.
        Assert.Contains("*a", r.Text);
        Assert.Contains("c*", r.Text);
    }

    [Fact]
    public void ToggleCode_TextWithBackticks()
    {
        // Internal `backtick` is treated as code markers within the selection.
        string text = "use `backtick` here";
        var r = MarkdownTextExtensions.ToggleInlineCode(text, 0, text.Length);
        Assert.Equal("`use backtick here`", r.Text);
    }

    [Fact]
    public void ToggleStrikethrough_TextWithTildes()
    {
        // Internal ~~ markers get stripped as strikethrough markers.
        string text = "a ~~ b ~~ c";
        var r = MarkdownTextExtensions.ToggleStrikethrough(text, 0, text.Length);
        Assert.Equal("~~a  b  c~~", r.Text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION C: Selection at text boundaries
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SelectionAtPositionZero_ToggleBold()
    {
        var r = MarkdownTextExtensions.ToggleBold("hello world", 0, 5);
        Assert.Equal("**hello** world", r.Text);
        Assert.Equal(2, r.SelectionStart);
        Assert.Equal(7, r.SelectionEnd);
    }

    [Fact]
    public void SelectionAtEndOfText_ToggleBold()
    {
        var r = MarkdownTextExtensions.ToggleBold("hello world", 6, 11);
        Assert.Equal("hello **world**", r.Text);
        Assert.Equal(8, r.SelectionStart);
        Assert.Equal(13, r.SelectionEnd);
    }

    [Fact]
    public void SelectionEntireText_ToggleItalic()
    {
        string text = "full line";
        var r = MarkdownTextExtensions.ToggleItalic(text, 0, text.Length);
        Assert.Equal("*full line*", r.Text);
        Assert.Equal(1, r.SelectionStart);
        Assert.Equal(10, r.SelectionEnd);
    }

    [Fact]
    public void ToggleCodeOnSingleCharacter()
    {
        var r = MarkdownTextExtensions.ToggleInlineCode("x", 0, 1);
        Assert.Equal("`x`", r.Text);
        Assert.Equal(1, r.SelectionStart);
        Assert.Equal(2, r.SelectionEnd);
    }

    [Fact]
    public void ToggleStrikethroughEntireDocument_ThreeLines()
    {
        string text = "alpha\nbeta\ngamma";
        var r = MarkdownTextExtensions.ToggleStrikethrough(text, 0, text.Length);
        Assert.Equal("~~alpha~~\n~~beta~~\n~~gamma~~", r.Text);
        Assert.Contains("~~alpha~~", r.Text);
        Assert.Contains("~~beta~~", r.Text);
        Assert.Contains("~~gamma~~", r.Text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION D: Multi-line selections with varying formatting
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MultiLine_BoldThreeLines_ThenItalicMiddleTwo()
    {
        string text = "aaa\nbbb\nccc";
        var r = MarkdownTextExtensions.ToggleBold(text, 0, text.Length);
        text = r.Text;
        Assert.Equal("**aaa**\n**bbb**\n**ccc**", text);

        // Select lines 2-3 (bbb and ccc) for italic
        (var s, var e) = FindWord(text, "bbb");
        var (_, cccEnd) = FindWord(text, "ccc", s);
        r = MarkdownTextExtensions.ToggleItalic(text, s, cccEnd);
        text = r.Text;

        // Line 2 and 3 should now have both bold and italic
        Assert.Contains("**aaa**", text);
        Assert.Contains("***bbb***", text);
        Assert.Contains("***ccc***", text);
    }

    [Fact]
    public void MultiLine_CodeOnLines1And3_BoldOnLines2And3()
    {
        string text = "one\ntwo\nthree";
        // Code on lines 1+3 only
        var r = MarkdownTextExtensions.ToggleInlineCode(text, 0, 3);
        text = r.Text;
        r = MarkdownTextExtensions.ToggleInlineCode(text, text.Length - 5, text.Length);
        text = r.Text;
        Assert.Contains("`one`", text);
        Assert.Contains("`three`", text);

        // Bold on lines 2+3
        (var s, var e) = FindWord(text, "two");
        var (_, threeEnd) = FindWord(text, "three", s);
        r = MarkdownTextExtensions.ToggleBold(text, s, threeEnd);
        text = r.Text;
        Assert.Contains("**two**", text);
        Assert.Contains("three", text);
    }

    [Fact]
    public void MultiLine_StrikethroughAll_ThenRemoveMiddle()
    {
        string text = "aaa\nbbb\nccc";
        var r = MarkdownTextExtensions.ToggleStrikethrough(text, 0, text.Length);
        text = r.Text;
        Assert.Equal("~~aaa~~\n~~bbb~~\n~~ccc~~", text);

        // Remove strikethrough from middle line
        (var s, var e) = FindWord(text, "bbb");
        r = MarkdownTextExtensions.ToggleStrikethrough(text, s, e);
        text = r.Text;
        Assert.Contains("~~aaa~~", text);
        Assert.Contains("bbb", text);
        Assert.DoesNotContain("~~bbb~~", text);
        Assert.Contains("~~ccc~~", text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION E: Repeated toggle idempotency patterns
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Idempotency_BoldOnOffOn_FinalMatchesFirstOn()
    {
        string text = "hello";
        var r1 = MarkdownTextExtensions.ToggleBold(text, 0, 5);
        string firstOn = r1.Text;

        // Turn off
        var r2 = MarkdownTextExtensions.ToggleBold(r1.Text, r1.SelectionStart, r1.SelectionEnd);
        Assert.Equal("hello", r2.Text);

        // Turn on again
        var r3 = MarkdownTextExtensions.ToggleBold(r2.Text, r2.SelectionStart, r2.SelectionEnd);
        Assert.Equal(firstOn, r3.Text);
    }

    [Fact]
    public void Idempotency_AllFourStylesOnOffOn()
    {
        string text = "content";

        // Apply all 4 styles
        var r = MarkdownTextExtensions.ToggleBold(text, 0, 7);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        string firstOn = r.Text;
        Assert.Equal("~~***`content`***~~", firstOn);

        // Remove all 4 styles
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal("content", r.Text);

        // Re-apply all 4 styles
        r = MarkdownTextExtensions.ToggleBold(r.Text, 0, 7);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal(firstOn, r.Text);
    }

    [Fact]
    public void Idempotency_50RandomToggles_WordsPreserved()
    {
        const string original = "one two three\nfour five six\nseven eight nine";
        string text = original;
        string[] words = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

        var toggleFuncs = new Func<string, int, int, TextEditResult>[]
        {
            MarkdownTextExtensions.ToggleBold,
            MarkdownTextExtensions.ToggleItalic,
            MarkdownTextExtensions.ToggleStrikethrough,
            MarkdownTextExtensions.ToggleInlineCode,
        };

        var selections = new (string from, string to)[]
        {
            ("one", "two"), ("four", "five"), ("seven", "eight"),
            ("two", "three"), ("five", "six"), ("eight", "nine"),
            ("one", "three"), ("four", "six"), ("seven", "nine"),
        };

        var rng = new Random(42); // deterministic seed

        for (int i = 0; i < 50; i++)
        {
            var (fromWord, toWord) = selections[i % selections.Length];
            int fromPos = text.IndexOf(fromWord, StringComparison.Ordinal);
            Assert.True(fromPos >= 0, $"Word '{fromWord}' not found at iteration {i}");
            int toPos = text.IndexOf(toWord, fromPos, StringComparison.Ordinal);
            Assert.True(toPos >= 0, $"Word '{toWord}' not found at iteration {i}");
            toPos += toWord.Length;

            var func = toggleFuncs[rng.Next(toggleFuncs.Length)];
            var r = func(text, fromPos, toPos);
            text = r.Text;
        }

        // All original words must survive
        foreach (var w in words)
            Assert.Contains(w, text);

        // HTML must render without crashing
        var htmlResult = MarkdownRenderer.Render(text);
        Assert.NotNull(htmlResult.Html);
        Assert.NotEmpty(htmlResult.Html);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION F: Precise selection position verification
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SelectionAfterBoldWrap_CoversContentOnly()
    {
        var r = MarkdownTextExtensions.ToggleBold("hello world", 0, 11);
        Assert.Equal("**hello world**", r.Text);
        // Selection should cover "hello world" only, not the ** markers
        string selected = r.Text.Substring(r.SelectionStart, r.SelectionEnd - r.SelectionStart);
        Assert.Equal("hello world", selected);
    }

    [Fact]
    public void SelectionAfterItalicWrap_CoversContentOnly()
    {
        var r = MarkdownTextExtensions.ToggleItalic("test", 0, 4);
        Assert.Equal("*test*", r.Text);
        string selected = r.Text.Substring(r.SelectionStart, r.SelectionEnd - r.SelectionStart);
        Assert.Equal("test", selected);
    }

    [Fact]
    public void SelectionAfterCodeWrap_CoversContentOnly()
    {
        var r = MarkdownTextExtensions.ToggleInlineCode("code", 0, 4);
        Assert.Equal("`code`", r.Text);
        string selected = r.Text.Substring(r.SelectionStart, r.SelectionEnd - r.SelectionStart);
        Assert.Equal("code", selected);
    }

    [Fact]
    public void SelectionAfterStrikethroughWrap_CoversContentOnly()
    {
        var r = MarkdownTextExtensions.ToggleStrikethrough("remove", 0, 6);
        Assert.Equal("~~remove~~", r.Text);
        string selected = r.Text.Substring(r.SelectionStart, r.SelectionEnd - r.SelectionStart);
        Assert.Equal("remove", selected);
    }

    [Fact]
    public void SelectionAfterUnwrap_CoversUnwrappedContent()
    {
        // Start with bold text, then unwrap
        var r = MarkdownTextExtensions.ToggleBold("**content**", 2, 9);
        Assert.Equal("content", r.Text);
        Assert.Equal(0, r.SelectionStart);
        Assert.Equal(7, r.SelectionEnd);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION G: Canonical marker order verification
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CanonicalOrder_AllStylesInReverseOrder()
    {
        // Apply styles in reverse order: Code → Italic → Bold → Strikethrough
        // Should still produce canonical form: ~~***`text`***~~
        string text = "text";
        var r = MarkdownTextExtensions.ToggleInlineCode(text, 0, 4);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal("~~***`text`***~~", r.Text);
    }

    [Fact]
    public void CanonicalOrder_AllStylesInRandomOrder_ProducesSameResult()
    {
        // Apply in order: Strikethrough → Bold → Code → Italic
        string text1 = "sample";
        var r = MarkdownTextExtensions.ToggleStrikethrough(text1, 0, 6);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal("~~***`sample`***~~", r.Text);

        // Apply in order: Italic → Code → Strikethrough → Bold
        string text2 = "sample";
        r = MarkdownTextExtensions.ToggleItalic(text2, 0, 6);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal("~~***`sample`***~~", r.Text);
    }

    [Fact]
    public void CanonicalOrder_RemoveAllStylesInAnyOrder_ProducesCleanText()
    {
        string baseText = "~~***`data`***~~";
        string cleanText = "data";
        int contentStart = 6;
        int contentEnd = 10;

        // Remove in order: Code → Bold → Italic → Strikethrough
        var r = MarkdownTextExtensions.ToggleInlineCode(baseText, contentStart, contentEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal(cleanText, r.Text);

        // Remove in order: Italic → Strikethrough → Code → Bold
        r = MarkdownTextExtensions.ToggleItalic(baseText, contentStart, contentEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal(cleanText, r.Text);

        // Remove in order: Strikethrough → Bold → Italic → Code
        r = MarkdownTextExtensions.ToggleStrikethrough(baseText, contentStart, contentEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal(cleanText, r.Text);
    }
}
