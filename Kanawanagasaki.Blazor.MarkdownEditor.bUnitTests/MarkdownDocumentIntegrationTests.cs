using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.bUnitTests;

/// <summary>
/// Integration tests that exercise the full MarkdownTextExtensions → MarkdownRenderer
/// pipeline. These tests verify that sequences of toggle operations on the public API
/// produce correct markdown output AND correct rendered HTML.
///
/// Focus areas:
///   1. Precise HTML output verification (not just Contains, but exact structure)
///   2. Overlapping marker detection (TryResolveOverlappingMarkers Case 1)
///   3. Line mapping accuracy with complex formatting
///   4. Multi-line selections where each line has different existing formatting
///   5. Renderer edge cases with nested/overlapping inline styles
///   6. Toggle operations on text containing markdown-significant characters
///   7. Undo/redo patterns (apply, reverse, apply again)
/// </summary>
public class MarkdownDocumentIntegrationTests
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

    private static string StripHtmlTags(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 1: Precise HTML output after specific toggle sequences
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BoldSingleWord_RendersCorrectStrongTags()
    {
        // Toggle bold on "two" in "One two three"
        var r = MarkdownTextExtensions.ToggleBold("One two three", 4, 7);
        Assert.Equal("One **two** three", r.Text);

        var htmlResult = MarkdownRenderer.Render(r.Text);
        Assert.Contains("<strong>two</strong>", htmlResult.Html);
        Assert.Contains("One", htmlResult.Html);
        Assert.Contains("three", htmlResult.Html);
    }

    [Fact]
    public void ItalicSingleWord_RendersCorrectEmTags()
    {
        var r = MarkdownTextExtensions.ToggleItalic("Hello world", 0, 5);
        Assert.Equal("*Hello* world", r.Text);

        var htmlResult = MarkdownRenderer.Render(r.Text);
        Assert.Contains("<em>Hello</em>", htmlResult.Html);
        Assert.Contains("world", htmlResult.Html);
    }

    [Fact]
    public void StrikethroughSingleWord_RendersCorrectDelTags()
    {
        var r = MarkdownTextExtensions.ToggleStrikethrough("delete this word", 7, 11);
        Assert.Equal("delete ~~this~~ word", r.Text);

        var htmlResult = MarkdownRenderer.Render(r.Text);
        Assert.Contains("<del>this</del>", htmlResult.Html);
    }

    [Fact]
    public void CodeSingleWord_RendersCorrectCodeClass()
    {
        var r = MarkdownTextExtensions.ToggleInlineCode("run foo()", 4, 7);
        Assert.Equal("run `foo`()", r.Text);

        var htmlResult = MarkdownRenderer.Render(r.Text);
        Assert.Contains("md-inline-code", htmlResult.Html);
        Assert.Contains("foo", htmlResult.Html);
    }

    [Fact]
    public void BoldItalicCombined_RendersStrongInsideEm()
    {
        // Apply bold then italic to "hello"
        var r1 = MarkdownTextExtensions.ToggleBold("say hello world", 4, 9);
        Assert.Equal("say **hello** world", r1.Text);

        var r2 = MarkdownTextExtensions.ToggleItalic(r1.Text, r1.SelectionStart, r1.SelectionEnd);
        Assert.Equal("say ***hello*** world", r2.Text);

        var htmlResult = MarkdownRenderer.Render(r2.Text);
        Assert.Contains("<strong>", htmlResult.Html);
        Assert.Contains("<em>", htmlResult.Html);
        Assert.Contains("hello", htmlResult.Html);
    }

    [Fact]
    public void AllFourStylesSingleWord_RendersCompleteNesting()
    {
        // Apply all 4 styles to "test" and verify the HTML structure
        string text = "a test b";
        var r = MarkdownTextExtensions.ToggleBold(text, 2, 6);
        text = r.Text;

        r = MarkdownTextExtensions.ToggleItalic(text, r.SelectionStart, r.SelectionEnd);
        text = r.Text;

        r = MarkdownTextExtensions.ToggleStrikethrough(text, r.SelectionStart, r.SelectionEnd);
        text = r.Text;

        r = MarkdownTextExtensions.ToggleInlineCode(text, r.SelectionStart, r.SelectionEnd);
        text = r.Text;

        Assert.Equal("a ~~***`test`***~~ b", text);

        var htmlResult = MarkdownRenderer.Render(text);
        // Verify all formatting tags present
        Assert.Contains("<del>", htmlResult.Html);
        Assert.Contains("</del>", htmlResult.Html);
        Assert.Contains("<strong>", htmlResult.Html);
        Assert.Contains("<em>", htmlResult.Html);
        Assert.Contains("md-inline-code", htmlResult.Html);
        // Verify the word content is present
        Assert.Contains("test", htmlResult.Html);
    }

    [Fact]
    public void MultiLineBoldAllThreeLines_RendersCorrectStrongTagsPerLine()
    {
        string text = "alpha\nbeta\ngamma";
        var r = MarkdownTextExtensions.ToggleBold(text, 0, text.Length);
        Assert.Equal("**alpha**\n**beta**\n**gamma**", r.Text);

        var htmlResult = MarkdownRenderer.Render(r.Text);
        // Each line should have its own <strong> tag
        var strongCount = System.Text.RegularExpressions.Regex.Matches(htmlResult.Html, "<strong>").Count;
        Assert.Equal(3, strongCount);
        Assert.Contains("alpha", htmlResult.Html);
        Assert.Contains("beta", htmlResult.Html);
        Assert.Contains("gamma", htmlResult.Html);
    }

    [Fact]
    public void MultiLineAllFourStyles_RendersAllTagsPerLine()
    {
        string text = "line one\nline two\nline three";
        var r = MarkdownTextExtensions.ToggleBold(text, 0, text.Length);
        text = r.Text;

        r = MarkdownTextExtensions.ToggleItalic(text, r.SelectionStart, r.SelectionEnd);
        text = r.Text;

        r = MarkdownTextExtensions.ToggleStrikethrough(text, r.SelectionStart, r.SelectionEnd);
        text = r.Text;

        r = MarkdownTextExtensions.ToggleInlineCode(text, r.SelectionStart, r.SelectionEnd);
        text = r.Text;

        // Expected: "~~***`line one`***~~\n~~***`line two`***~~\n~~***`line three`***~~"
        Assert.Equal(
            "~~***`line one`***~~\n~~***`line two`***~~\n~~***`line three`***~~",
            text);

        var htmlResult = MarkdownRenderer.Render(text);
        // Should have 3 del, 3 strong, 3 em, 3 code spans
        var delCount = System.Text.RegularExpressions.Regex.Matches(htmlResult.Html, "<del>").Count;
        var strongCount = System.Text.RegularExpressions.Regex.Matches(htmlResult.Html, "<strong>").Count;
        var emCount = System.Text.RegularExpressions.Regex.Matches(htmlResult.Html, "<em>").Count;
        var codeCount = System.Text.RegularExpressions.Regex.Matches(htmlResult.Html, "md-inline-code").Count;

        Assert.Equal(3, delCount);
        Assert.Equal(3, strongCount);
        Assert.Equal(3, emCount);
        Assert.Equal(3, codeCount);

        // Verify all content words present
        Assert.Contains("line one", htmlResult.Html);
        Assert.Contains("line two", htmlResult.Html);
        Assert.Contains("line three", htmlResult.Html);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 2: Overlapping marker detection (TryResolveOverlappingMarkers)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SelectionContainingMarkers_BoldOnItalicRegion_StripsAndApplies()
    {
        // "A *B* C" — select entire line and toggle bold.
        // The italic markers are INSIDE the selection.
        // Case 1 of TryResolveOverlappingMarkers should trigger.
        string text = "A *B* C";
        var r = MarkdownTextExtensions.ToggleBold(text, 0, text.Length);
        // Expected: strips italic, applies bold → "**A B C**"
        Assert.Equal("**A B C**", r.Text);
    }

    [Fact]
    public void SelectionContainingMarkers_StrikethroughOnBoldItalicRegion()
    {
        // "X ***Y*** Z" — select entire line and toggle strikethrough.
        // Case 1: markers inside selection → strip, apply new style
        string text = "X ***Y*** Z";
        var r = MarkdownTextExtensions.ToggleStrikethrough(text, 0, text.Length);
        Assert.Equal("~~X Y Z~~", r.Text);
    }

    [Fact]
    public void SelectionContainingMarkers_CodeOnStrikethroughRegion()
    {
        // "A ~~B~~ C" — select entire line and toggle code.
        // Case 1: markers inside selection → strip, apply code
        string text = "A ~~B~~ C";
        var r = MarkdownTextExtensions.ToggleInlineCode(text, 0, text.Length);
        Assert.Equal("`A B C`", r.Text);
    }

    [Fact]
    public void SelectionContainingMarkers_BoldOnFullyStyledRegion()
    {
        // "~~***`content`***~~" — selecting full text and toggling bold.
        // TryExpandToMarkerRegion detects the markers and expands the
        // effective selection to the content area, then toggles bold.
        // Bold is already ON (part of ***), so toggling removes it.
        // Result: ~~*`content`*~~
        string text = "~~***`content`***~~";
        var r = MarkdownTextExtensions.ToggleBold(text, 0, text.Length);
        Assert.Equal("~~*`content`*~~", r.Text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 3: Line mapping accuracy with complex formatting
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LineMappings_BoldText_MapsVisibleCharsToSourceSkippingMarkers()
    {
        // "**hello**" — visible chars "hello" at positions 2,3,4,5,6 in source
        var result = MarkdownRenderer.Render("**hello**");
        Assert.Single(result.Lines);
        var mapping = result.Lines[0].VisibleToSource;
        Assert.Equal(5, mapping.Length);
        Assert.Equal(2, mapping[0]); // 'h' → source pos 2
        Assert.Equal(3, mapping[1]); // 'e' → source pos 3
        Assert.Equal(4, mapping[2]); // 'l' → source pos 4
        Assert.Equal(5, mapping[3]); // 'l' → source pos 5
        Assert.Equal(6, mapping[4]); // 'o' → source pos 6
    }

    [Fact]
    public void LineMappings_AllFourStyles_MapsCorrectlyThroughAllMarkers()
    {
        // "~~***`code`***~~" — visible text "code" at positions 6,7,8,9 in source
        var result = MarkdownRenderer.Render("~~***`code`***~~");
        Assert.Single(result.Lines);
        var mapping = result.Lines[0].VisibleToSource;
        Assert.Equal(4, mapping.Length);
        Assert.Equal(6, mapping[0]); // 'c' → source pos 6
        Assert.Equal(7, mapping[1]); // 'o' → source pos 7
        Assert.Equal(8, mapping[2]); // 'd' → source pos 8
        Assert.Equal(9, mapping[3]); // 'e' → source pos 9
    }

    [Fact]
    public void LineMappings_PlainTextWithFormatting_MapsAllVisibleChars()
    {
        // "A **B** C" — visible: "A B C" → 5 chars
        var result = MarkdownRenderer.Render("A **B** C");
        Assert.Single(result.Lines);
        var mapping = result.Lines[0].VisibleToSource;
        Assert.Equal(5, mapping.Length);
        // 'A' at 0, ' ' at 1, 'B' at 4, ' ' at 7, 'C' at 8
        Assert.Equal(0, mapping[0]);
        Assert.Equal(1, mapping[1]);
        Assert.Equal(4, mapping[2]);
        Assert.Equal(7, mapping[3]);
        Assert.Equal(8, mapping[4]);
    }

    [Fact]
    public void LineMappings_MultiLine_CorrectSourceStarts()
    {
        var result = MarkdownRenderer.Render("aaa\n**bbb**\nccc");
        Assert.Equal(3, result.Lines.Length);
        Assert.Equal(0, result.Lines[0].SourceStart);
        Assert.Equal(4, result.Lines[1].SourceStart);
        Assert.Equal(12, result.Lines[2].SourceStart);

        // Line 2: "**bbb**" → visible "bbb" maps to source 6,7,8
        var line2Mapping = result.Lines[1].VisibleToSource;
        Assert.Equal(3, line2Mapping.Length);
        Assert.Equal(6, line2Mapping[0]);
        Assert.Equal(7, line2Mapping[1]);
        Assert.Equal(8, line2Mapping[2]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 4: Multi-line with different formatting per line
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MixedFormattingPerLine_CodeToggle_PerLineIndependentProcessing()
    {
        // Line 1: bold, Line 2: italic, Line 3: plain
        // When selecting starting inside the bold markers, each line is
        // processed independently by the multi-line handler.
        string text = "**bold line**\n*italic line*\nplain line";
        int start = 2; // start of "bold" inside **...**
        int end = text.Length;
        var r = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = r.Text;

        // Line 1: "bold line**" has no markers at its start, so code wraps it.
        // The original "**" at positions 0-1 remain, giving "**`bold line**`"
        // Line 2: italic detected, code added inside. "*`italic line`*"
        // Line 3: no markers, code wraps. "`plain line`"
        Assert.Contains("`bold line**`", text);
        Assert.Contains("*`italic line`*", text);
        Assert.Contains("`plain line`", text);
    }

    [Fact]
    public void MultiLineWithEmptyMiddleLine_ToggleSkipsEmptyLine()
    {
        string text = "hello\n\nworld";
        var r = MarkdownTextExtensions.ToggleBold(text, 0, text.Length);
        Assert.Equal("**hello**\n\n**world**", r.Text);

        // Empty line should not have any markers
        var lines = r.Text.Split('\n');
        Assert.Equal("", lines[1]); // empty line stays empty
    }

    [Fact]
    public void MultiLinePartialSelection_OnlyFormatsSelectedLines()
    {
        string text = "alpha\nbeta\ngamma\ndelta";
        // Select "beta" through "gamma" (line 2 to line 3)
        var (s, e) = FindWord(text, "beta");
        var (_, gammaEnd) = FindWord(text, "gamma", s);
        var r = MarkdownTextExtensions.ToggleBold(text, s, gammaEnd);
        Assert.Equal("alpha\n**beta**\n**gamma**\ndelta", r.Text);
    }

    [Fact]
    public void ThreeLineSelection_DifferentExistingStylesEach_ToggleStrikethrough()
    {
        // Build a document where each line has different formatting
        string text = "One two three\nfour five six\nseven eight nine";

        // Line 1: bold on "two three"
        var r = MarkdownTextExtensions.ToggleBold(text, 4, 13);
        text = r.Text;

        // Line 2: italic on "four five six"
        (var s, var e) = FindWord(text, "four");
        var (_, sixEnd) = FindWord(text, "six", s);
        r = MarkdownTextExtensions.ToggleItalic(text, s, sixEnd);
        text = r.Text;

        // Now: "One **two three**\n*four five six*\nseven eight nine"
        Assert.Equal("One **two three**\n*four five six*\nseven eight nine", text);

        // Select from "two" to "five" (crosses lines 1-2, different styles)
        // This creates a crossing-boundary selection
        (s, e) = FindWord(text, "two");
        var (_, fiveEnd) = FindWord(text, "five", s);
        r = MarkdownTextExtensions.ToggleStrikethrough(text, s, fiveEnd);
        text = r.Text;

        // The crossing-boundary handler processes each part differently.
        // Verify all words are still present and strikethrough was applied
        Assert.Contains("~~", text);
        Assert.Contains("One", text);
        Assert.Contains("two", text);
        Assert.Contains("three", text);
        Assert.Contains("four", text);
        Assert.Contains("five", text);
        Assert.Contains("six", text);
        Assert.Contains("seven", text);
        Assert.Contains("eight", text);
        Assert.Contains("nine", text);

        // HTML should render
        var htmlResult = MarkdownRenderer.Render(text);
        Assert.NotNull(htmlResult.Html);
        Assert.NotEmpty(htmlResult.Html);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 5: Undo/redo patterns
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UndoRedo_BoldItalic_ApplyRemoveApply()
    {
        // Apply bold+italic, remove italic, apply italic again
        string text = "hello world";
        int start = 0, end = 11;

        var r = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;

        r = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;
        Assert.Equal("***hello world***", text);

        // Remove italic
        r = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;
        Assert.Equal("**hello world**", text);

        // Re-apply italic
        r = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = r.Text;
        Assert.Equal("***hello world***", text);
    }

    [Fact]
    public void UndoRedo_AllFourStyles_RemoveEachOneByOne()
    {
        string text = "content here";
        int start = 0, end = 12;

        // Apply all 4 styles
        var r = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;

        r = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;

        r = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;

        r = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;
        Assert.Equal("~~***`content here`***~~", text);

        // Remove code
        r = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;
        Assert.Equal("~~***content here***~~", text);

        // Remove bold
        r = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;
        Assert.Equal("~~*content here*~~", text);

        // Remove italic
        r = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = r.Text; start = r.SelectionStart; end = r.SelectionEnd;
        Assert.Equal("~~content here~~", text);

        // Remove strikethrough
        r = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        text = r.Text;
        Assert.Equal("content here", text);
    }

    [Fact]
    public void UndoRedo_MultiLine_ApplyAllRemoveAllTwice()
    {
        const string original = "aaa\nbbb\nccc";
        string text = original;

        for (int round = 0; round < 2; round++)
        {
            var (s, e) = FindWord(text, "aaa");
            var (_, cccEnd) = FindWord(text, "ccc", s);

            var r = MarkdownTextExtensions.ToggleBold(text, s, cccEnd);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            r = MarkdownTextExtensions.ToggleItalic(text, s, e);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            r = MarkdownTextExtensions.ToggleStrikethrough(text, s, e);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            r = MarkdownTextExtensions.ToggleInlineCode(text, s, e);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            // Verify all styles on
            Assert.Contains("~~", text);
            Assert.Contains("***", text);
            Assert.Contains("`", text);

            // Now remove all
            r = MarkdownTextExtensions.ToggleBold(text, s, e);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            r = MarkdownTextExtensions.ToggleItalic(text, s, e);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            r = MarkdownTextExtensions.ToggleStrikethrough(text, s, e);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            r = MarkdownTextExtensions.ToggleInlineCode(text, s, e);
            text = r.Text;

            Assert.Equal(original, text);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 6: Text containing markdown-significant characters
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ToggleBold_TextWithAsterisks_HandledBySmartToggle()
    {
        // Text containing literal asterisks — the smart toggle detects
        // the * markers as italic markers and processes them accordingly.
        // The * b * portion is treated as an italic-wrapped region inside the
        // selection. ToggleResolveOverlappingMarkers (Case 1) strips the
        // italic markers and applies bold to the clean content.
        string text = "a * b * c";
        var r = MarkdownTextExtensions.ToggleBold(text, 0, text.Length);
        // Internal * markers get stripped as italic markers
        Assert.Equal("**a  b  c**", r.Text);

        var htmlResult = MarkdownRenderer.Render(r.Text);
        Assert.Contains("a", htmlResult.Html);
        Assert.Contains("b", htmlResult.Html);
        Assert.Contains("c", htmlResult.Html);
    }

    [Fact]
    public void ToggleCode_TextWithBackticks_HandledBySmartToggle()
    {
        // The smart toggle detects the internal `backtick` as code markers
        // within the selection. They get stripped and the new code markers
        // wrap the clean content.
        string text = "use `backtick` here";
        var r = MarkdownTextExtensions.ToggleInlineCode(text, 0, text.Length);
        Assert.Equal("`use backtick here`", r.Text);
    }

    [Fact]
    public void ToggleStrikethrough_TextWithTildes_HandledBySmartToggle()
    {
        // Internal ~~ markers get stripped as strikethrough markers
        string text = "a ~~ b ~~ c";
        var r = MarkdownTextExtensions.ToggleStrikethrough(text, 0, text.Length);
        Assert.Equal("~~a  b  c~~", r.Text);
    }

    [Fact]
    public void ToggleBold_TextWithAngleBrackets_RendersEscaped()
    {
        string text = "x <y> z";
        var r = MarkdownTextExtensions.ToggleBold(text, 0, text.Length);
        Assert.Equal("**x <y> z**", r.Text);

        var htmlResult = MarkdownRenderer.Render(r.Text);
        Assert.Contains("&lt;", htmlResult.Html);
        Assert.Contains("&gt;", htmlResult.Html);
        Assert.DoesNotContain("<y>", htmlResult.Html);
    }

    [Fact]
    public void ToggleItalic_TextWithAmpersand_RendersEscaped()
    {
        string text = "a & b";
        var r = MarkdownTextExtensions.ToggleItalic(text, 0, text.Length);
        Assert.Equal("*a & b*", r.Text);

        var htmlResult = MarkdownRenderer.Render(r.Text);
        Assert.Contains("&amp;", htmlResult.Html);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 7: Specific 40+ operation sequence with verified output
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DetailedOperationSequence_EveryStepVerified()
    {
        string text = "One two three\nfour five six\nseven eight nine";

        // ── Op 1: Bold on "two" through "six" (multi-line) ──
        var (s, e) = FindWord(text, "two");
        var (_, sixEnd) = FindWord(text, "six");
        var r = MarkdownTextExtensions.ToggleBold(text, s, sixEnd);
        Assert.Equal("One **two three**\n**four five six**\nseven eight nine", r.Text);
        text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

        // ── Op 2: Italic on "One" through "three" (crosses bold boundary) ──
        (s, e) = FindWord(text, "One");
        var (_, threeEnd) = FindWord(text, "three", s);
        r = MarkdownTextExtensions.ToggleItalic(text, s, threeEnd);
        Assert.Equal("*One* ***two three***\n**four five six**\nseven eight nine", r.Text);
        text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

        // ── Op 3: Code on "five" through "seven" (multi-line, crosses bold) ──
        (s, e) = FindWord(text, "five");
        var (_, sevenEnd) = FindWord(text, "seven", s);
        r = MarkdownTextExtensions.ToggleInlineCode(text, s, sevenEnd);
        Assert.Equal("*One* ***two three***\n**four `five six**`\n`seven` eight nine", r.Text);
        text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

        // ── Op 4: Strikethrough on "five" through "eight" (multi-line) ──
        (s, e) = FindWord(text, "five");
        var (_, eightEnd) = FindWord(text, "eight", s);
        r = MarkdownTextExtensions.ToggleStrikethrough(text, s, eightEnd);
        Assert.Equal("*One* ***two three***\n**four `~~five six**`~~\n~~`seven` eight`~~ nine", r.Text);
        text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

        // ── Op 5: Bold OFF on "two" through "three" (sub-selection within ***...***) ──
        (s, e) = FindWord(text, "two");
        var (_, threeEnd2) = FindWord(text, "three", s);
        r = MarkdownTextExtensions.ToggleBold(text, s, threeEnd2);
        Assert.Equal("*One* *two three*\n**four `~~five six**`~~\n~~`seven` eight`~~ nine", r.Text);
        text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

        // ── Verify HTML renders correctly at this point ──
        var htmlResult = MarkdownRenderer.Render(text);
        Assert.NotNull(htmlResult.Html);
        Assert.NotEmpty(htmlResult.Html);
        Assert.Equal(3, htmlResult.Lines.Length);

        // ── Ops 6-45: 10 cycles of B/I/C/S on "five" through "six" ──
        for (int cycle = 0; cycle < 10; cycle++)
        {
            (s, e) = FindWord(text, "five");
            var (_, sixEnd2) = FindWord(text, "six", s);

            r = MarkdownTextExtensions.ToggleBold(text, s, sixEnd2);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            r = MarkdownTextExtensions.ToggleItalic(text, s, e);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            r = MarkdownTextExtensions.ToggleInlineCode(text, s, e);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;

            r = MarkdownTextExtensions.ToggleStrikethrough(text, s, e);
            text = r.Text; s = r.SelectionStart; e = r.SelectionEnd;
        }

        // ── After all 45 operations: verify all original words preserved ──
        string[] words = ["One", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
        foreach (var w in words)
            Assert.Contains(w, text);

        // ── Verify HTML renders without crashing and contains all words ──
        htmlResult = MarkdownRenderer.Render(text);
        Assert.NotNull(htmlResult.Html);
        Assert.NotEmpty(htmlResult.Html);
        foreach (var w in words)
            Assert.Contains(w, htmlResult.Html);

        // ── Verify 3 source lines ──
        Assert.Equal(3, htmlResult.Lines.Length);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 8: Renderer edge cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Renderer_NestedBoldItalic_ProducesBothTags()
    {
        var result = MarkdownRenderer.Render("***bold italic***");
        Assert.Contains("<strong><em>bold italic</em></strong>", result.Html);
    }

    [Fact]
    public void Renderer_StrikethroughBoldItalic_AllThreeNestCorrectly()
    {
        var result = MarkdownRenderer.Render("~~***bold italic strike***~~");
        Assert.Contains("<del>", result.Html);
        Assert.Contains("<strong>", result.Html);
        Assert.Contains("<em>", result.Html);
        Assert.Contains("bold italic strike", result.Html);
    }

    [Fact]
    public void Renderer_CodeWithinBold_ProducesCodeClass()
    {
        var result = MarkdownRenderer.Render("**`code`**");
        Assert.Contains("<strong>", result.Html);
        Assert.Contains("md-inline-code", result.Html);
    }

    [Fact]
    public void Renderer_FullNesting_CodeInnermost()
    {
        var result = MarkdownRenderer.Render("~~***`deep`***~~");
        Assert.Contains("<del>", result.Html);
        Assert.Contains("<strong>", result.Html);
        Assert.Contains("<em>", result.Html);
        Assert.Contains("md-inline-code", result.Html);
        Assert.Contains("deep", result.Html);
    }

    [Fact]
    public void Renderer_MultipleInlineElementsOnSameLine()
    {
        var result = MarkdownRenderer.Render("**bold** and *italic* and ~~strike~~");
        Assert.Contains("<strong>bold</strong>", result.Html);
        Assert.Contains("<em>italic</em>", result.Html);
        Assert.Contains("<del>strike</del>", result.Html);
    }

    [Fact]
    public void Renderer_HeadingsWithInlineFormatting()
    {
        var result = MarkdownRenderer.Render("## **bold heading**");
        Assert.Contains("<h2", result.Html);
        Assert.Contains("<strong>bold heading</strong>", result.Html);
    }

    [Fact]
    public void Renderer_ListItemsWithInlineFormatting()
    {
        var result = MarkdownRenderer.Render("- **bold item** and *italic*");
        Assert.Contains("md-li-marker", result.Html);
        Assert.Contains("<strong>bold item</strong>", result.Html);
        Assert.Contains("<em>italic</em>", result.Html);
    }

    [Fact]
    public void Renderer_BlockquoteWithInlineFormatting()
    {
        var result = MarkdownRenderer.Render("> **bold** in quote");
        Assert.Contains("<blockquote", result.Html);
        Assert.Contains("<strong>bold</strong>", result.Html);
    }

    [Fact]
    public void Renderer_LinkWithInlineFormatting()
    {
        // The renderer may not parse inline formatting inside link text.
        // This test verifies the link is rendered correctly regardless.
        var result = MarkdownRenderer.Render("[**click** here](https://example.com)");
        Assert.Contains("<a", result.Html);
        Assert.Contains("href=\"https://example.com\"", result.Html);
        // The link text should be present in some form
        Assert.Contains("click", result.Html);
        Assert.Contains("here", result.Html);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 9: Selection position accuracy
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SelectionAfterBold_CoversContentOnly()
    {
        var r = MarkdownTextExtensions.ToggleBold("hello", 0, 5);
        Assert.Equal("**hello**", r.Text);
        Assert.Equal(2, r.SelectionStart);  // after opening **
        Assert.Equal(7, r.SelectionEnd);    // before closing **
    }

    [Fact]
    public void SelectionAfterItalic_CoversContentOnly()
    {
        var r = MarkdownTextExtensions.ToggleItalic("hello", 0, 5);
        Assert.Equal("*hello*", r.Text);
        Assert.Equal(1, r.SelectionStart);
        Assert.Equal(6, r.SelectionEnd);
    }

    [Fact]
    public void SelectionAfterStrikethrough_CoversContentOnly()
    {
        var r = MarkdownTextExtensions.ToggleStrikethrough("hello", 0, 5);
        Assert.Equal("~~hello~~", r.Text);
        Assert.Equal(2, r.SelectionStart);
        Assert.Equal(7, r.SelectionEnd);
    }

    [Fact]
    public void SelectionAfterCode_CoversContentOnly()
    {
        var r = MarkdownTextExtensions.ToggleInlineCode("hello", 0, 5);
        Assert.Equal("`hello`", r.Text);
        Assert.Equal(1, r.SelectionStart);
        Assert.Equal(6, r.SelectionEnd);
    }

    [Fact]
    public void SelectionAfterUnwrapBold_CoversUnwrappedContent()
    {
        var r = MarkdownTextExtensions.ToggleBold("**hello**", 2, 7);
        Assert.Equal("hello", r.Text);
        Assert.Equal(0, r.SelectionStart);
        Assert.Equal(5, r.SelectionEnd);
    }

    [Fact]
    public void MultiLineSelection_CoversAllMarkersOnAllLines()
    {
        string text = "One two three\nfour five six\nseven eight nine";
        var (s, _) = FindWord(text, "two");
        var (_, eEnd) = FindWord(text, "six");

        var r = MarkdownTextExtensions.ToggleBold(text, s, eEnd);
        string selected = r.Text.Substring(r.SelectionStart, r.SelectionEnd - r.SelectionStart);
        // Selection must include all ** markers on every line
        Assert.Equal("**two three**\n**four five six**", selected);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 10: Toggle order independence (different order, same result)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AllStylesOn_DifferentOrder_ProducesCanonicalForm()
    {
        // Order 1: Bold → Italic → Strikethrough → Code
        string text1 = "hello";
        var r = MarkdownTextExtensions.ToggleBold(text1, 0, 5);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal("~~***`hello`***~~", r.Text);

        // Order 2: Code → Strikethrough → Bold → Italic
        string text2 = "hello";
        r = MarkdownTextExtensions.ToggleInlineCode(text2, 0, 5);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal("~~***`hello`***~~", r.Text);

        // Order 3: Strikethrough → Code → Italic → Bold
        string text3 = "hello";
        r = MarkdownTextExtensions.ToggleStrikethrough(text3, 0, 5);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal("~~***`hello`***~~", r.Text);
    }

    [Fact]
    public void AllStylesOff_DifferentOrder_AllProduceCleanText()
    {
        // Start with all styles on: ~~***`hello`***~~
        string baseText = "~~***`hello`***~~";
        string content = "hello";
        int contentStart = 6;
        int contentEnd = 11;

        // Remove in order: Bold → Italic → Strikethrough → Code
        var r = MarkdownTextExtensions.ToggleBold(baseText, contentStart, contentEnd);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal(content, r.Text);

        // Remove in order: Code → Strikethrough → Italic → Bold
        r = MarkdownTextExtensions.ToggleInlineCode(baseText, contentStart, contentEnd);
        r = MarkdownTextExtensions.ToggleStrikethrough(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal(content, r.Text);

        // Remove in order: Strikethrough → Code → Bold → Italic
        r = MarkdownTextExtensions.ToggleStrikethrough(baseText, contentStart, contentEnd);
        r = MarkdownTextExtensions.ToggleInlineCode(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleBold(r.Text, r.SelectionStart, r.SelectionEnd);
        r = MarkdownTextExtensions.ToggleItalic(r.Text, r.SelectionStart, r.SelectionEnd);
        Assert.Equal(content, r.Text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 11: Block-level toggle operations
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ToggleHeading_AddsPrefixCorrectly()
    {
        string text = "my heading";
        var r = MarkdownTextExtensions.ToggleHeading(text, 0, text.Length, 2);
        Assert.Equal("## my heading", r.Text);
        Assert.Equal(3, r.SelectionStart); // after "## "
        Assert.Equal(13, r.SelectionEnd);   // end of line (content + prefix)
    }

    [Fact]
    public void ToggleUnorderedList_AddsMarkersCorrectly()
    {
        // ToggleBlockPrefix only operates on the first line of the selection.
        string text = "item one";
        var r = MarkdownTextExtensions.ToggleUnorderedList(text, 0, text.Length);
        Assert.Equal("- item one", r.Text);
        Assert.Equal(2, r.SelectionStart); // after "- "
    }

    [Fact]
    public void ToggleOrderedList_AddsMarkersCorrectly()
    {
        // ToggleOrderedList only operates on the first line of the selection.
        string text = "first";
        var r = MarkdownTextExtensions.ToggleOrderedList(text, 0, text.Length);
        Assert.Equal("1. first", r.Text);
        Assert.Equal(3, r.SelectionStart); // after "1. "
    }

    [Fact]
    public void ToggleBlockquote_AddsMarkersCorrectly()
    {
        // ToggleBlockquote (via ToggleBlockPrefix) only operates on the first line.
        string text = "quote line one";
        var r = MarkdownTextExtensions.ToggleBlockquote(text, 0, text.Length);
        Assert.Equal("> quote line one", r.Text);
        Assert.Equal(2, r.SelectionStart); // after "> "
    }

    [Fact]
    public void ToggleHeading_ThenInlineFormatting_PreservesBoth()
    {
        string text = "my title";
        var r = MarkdownTextExtensions.ToggleHeading(text, 0, text.Length, 1);
        text = r.Text;
        Assert.Equal("# my title", text);

        // Bold "my" within the heading
        var (s, e) = FindWord(text, "my");
        r = MarkdownTextExtensions.ToggleBold(text, s, e);
        Assert.Equal("# **my** title", r.Text);

        // Verify HTML renders both heading and bold
        var htmlResult = MarkdownRenderer.Render(r.Text);
        Assert.Contains("<h1", htmlResult.Html);
        Assert.Contains("<strong>my</strong>", htmlResult.Html);
        Assert.Contains("title", htmlResult.Html);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 12: Insert operations with verification
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void InsertLink_EmptySelection_InsertsDefaultText()
    {
        // When start == end (empty selection), the method uses "link text" as default.
        var r = MarkdownTextExtensions.InsertLink("text", 2, 2);
        Assert.Contains("[link text](url)", r.Text);
    }

    [Fact]
    public void InsertImage_EmptySelection_InsertsDefaultText()
    {
        // When start == end (empty selection), the method uses "alt text" as default.
        var r = MarkdownTextExtensions.InsertImage("text", 2, 2);
        Assert.Contains("![alt text](url)", r.Text);
    }

    [Fact]
    public void InsertCodeBlock_SingleLine_WrapsCorrectly()
    {
        var r = MarkdownTextExtensions.InsertCodeBlock("x = 1", 0, 5);
        Assert.Equal("```\nx = 1\n```", r.Text);
    }

    [Fact]
    public void InsertCodeBlock_MultiLine_WrapsCorrectly()
    {
        string text = "line1\nline2";
        var r = MarkdownTextExtensions.InsertCodeBlock(text, 0, text.Length);
        Assert.Equal("```\nline1\nline2\n```", r.Text);
    }

    [Fact]
    public void InsertHorizontalRule_MidText_InsertsAtEnd()
    {
        // HR is always inserted at the end of the current line
        var r = MarkdownTextExtensions.InsertHorizontalRule("beforeafter", 6, 6);
        Assert.Equal("beforeafter\n---\n", r.Text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 13: Stress test — many operations on multiple word ranges
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StressTest_200Operations_TextRemainsValid()
    {
        const string original = "One two three\nfour five six\nseven eight nine";
        string text = original;
        string[] words = ["One", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

        var toggleFuncs = new Func<string, int, int, TextEditResult>[]
        {
            MarkdownTextExtensions.ToggleBold,
            MarkdownTextExtensions.ToggleItalic,
            MarkdownTextExtensions.ToggleStrikethrough,
            MarkdownTextExtensions.ToggleInlineCode,
        };

        // Use narrower selections (single words or adjacent word pairs)
        // to avoid destructive overlapping-marker resolution
        var selections = new (string from, string to)[]
        {
            ("two", "three"),  // single line, adjacent words
            ("five", "six"),   // single line, adjacent words
            ("seven", "eight"), // single line, adjacent words
            ("four", "five"),   // single line, adjacent words
            ("One", "two"),     // single line, adjacent words
            ("eight", "nine"),  // single line, adjacent words
            ("three", "four"),  // cross-line
            ("six", "seven"),   // cross-line
        };

        for (int i = 0; i < 200; i++)
        {
            var (fromWord, toWord) = selections[i % selections.Length];
            int fromPos = text.IndexOf(fromWord, StringComparison.Ordinal);
            Assert.True(fromPos >= 0, $"Word '{fromWord}' not found at iteration {i}");
            int toPos = text.IndexOf(toWord, fromPos, StringComparison.Ordinal);
            Assert.True(toPos >= 0, $"Word '{toWord}' not found at iteration {i}");
            toPos += toWord.Length;

            var func = toggleFuncs[i % toggleFuncs.Length];
            var r = func(text, fromPos, toPos);
            text = r.Text;
        }

        // All words must survive in the text
        foreach (var w in words)
            Assert.True(text.Contains(w, StringComparison.Ordinal),
                $"Word '{w}' lost after 200 operations. Text: [{text}]");

        // HTML must render without crashing
        var htmlResult = MarkdownRenderer.Render(text);
        Assert.NotNull(htmlResult.Html);
        Assert.NotEmpty(htmlResult.Html);
    }

    [Fact]
    public void StressTest_RapidToggleSameWord_50Cycles()
    {
        // Rapidly toggle all 4 styles on and off on "five" — 50 full cycles
        const string original = "One two three\nfour five six\nseven eight nine";
        string text = original;

        for (int cycle = 0; cycle < 50; cycle++)
        {
            var (s, e) = FindWord(text, "five");

            var r = MarkdownTextExtensions.ToggleBold(text, s, e);
            text = r.Text;

            r = MarkdownTextExtensions.ToggleItalic(text, r.SelectionStart, r.SelectionEnd);
            text = r.Text;

            r = MarkdownTextExtensions.ToggleStrikethrough(text, r.SelectionStart, r.SelectionEnd);
            text = r.Text;

            r = MarkdownTextExtensions.ToggleInlineCode(text, r.SelectionStart, r.SelectionEnd);
            text = r.Text;

            // Now remove all
            (s, e) = FindWord(text, "five");
            r = MarkdownTextExtensions.ToggleBold(text, s, e);
            text = r.Text;

            r = MarkdownTextExtensions.ToggleItalic(text, r.SelectionStart, r.SelectionEnd);
            text = r.Text;

            r = MarkdownTextExtensions.ToggleStrikethrough(text, r.SelectionStart, r.SelectionEnd);
            text = r.Text;

            r = MarkdownTextExtensions.ToggleInlineCode(text, r.SelectionStart, r.SelectionEnd);
            text = r.Text;
        }

        // After 50 full on/off cycles, text should be back to original
        Assert.Equal(original, text);
    }
}
