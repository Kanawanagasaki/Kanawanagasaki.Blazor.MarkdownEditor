using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;
using Xunit;
using System.Text.RegularExpressions;

namespace Kanawanagasaki.Blazor.MarkdownEditor.bUnitTests;

/// <summary>
/// Stress tests that simulate a user randomly selecting text across multiple
/// lines and clicking formatting buttons.  These tests verify that the editor
/// produces correct, well-formed markdown and clean HTML after many
/// consecutive toggle operations — especially when selections span line
/// boundaries and overlap with existing markers.
///
/// The 40 "random" operations use a seeded <see cref="Random"/> so the test
/// is fully deterministic and reproducible.
/// </summary>
public class RandomSelectionStressTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Helper: strip HTML tags to get visible text
    // ═══════════════════════════════════════════════════════════════════

    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private static string StripHtml(string html) => HtmlTagRegex.Replace(html, "").Trim();

    /// <summary>
    /// Extracts "visible text" from rendered HTML — the text a user would
    /// actually read on screen.  Used to verify that formatting operations
    /// don't destroy or duplicate content.
    /// </summary>
    private static string GetVisibleText(string markdown)
    {
        var result = MarkdownRenderer.Render(markdown);
        return StripHtml(result.Html);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Word-finding helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find the start index of a word (case-insensitive) in the text.
    /// </summary>
    private static int FindWordStart(string text, string word)
    {
        int idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        Assert.True(idx >= 0, $"Word '{word}' not found in text: [{text.Replace("\n", "\\n")}]");
        return idx;
    }

    /// <summary>
    /// Find the end index (exclusive) of a word (case-insensitive) in the text.
    /// </summary>
    private static int FindWordEnd(string text, string word)
    {
        int idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        Assert.True(idx >= 0, $"Word '{word}' not found in text: [{text.Replace("\n", "\\n")}]");
        return idx + word.Length;
    }

    /// <summary>
    /// Extract all unique alpha-words from the text for random selection.
    /// </summary>
    private static List<string> ExtractWords(string text)
    {
        var words = new List<string>();
        var matches = Regex.Matches(text, @"\b[a-zA-Z]+\b");
        foreach (Match m in matches)
        {
            if (!words.Contains(m.Value, StringComparer.OrdinalIgnoreCase))
                words.Add(m.Value);
        }
        return words;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Invariant verifiers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that all original content words are still present in the
    /// visible (rendered) text.  Formatting operations should NEVER destroy
    /// or duplicate content.
    /// </summary>
    private static void VerifyContentPreserved(string markdown, string[] originalWords)
    {
        var visible = GetVisibleText(markdown);
        foreach (var word in originalWords)
        {
            Assert.True(
                visible.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0,
                $"Content word '{word}' lost after formatting! Visible: [{visible.Replace("\n", "\\n")}]  Markdown: [{markdown.Replace("\n", "\\n")}]");
        }
    }

    /// <summary>
    /// Verifies that the rendered HTML does not contain raw, unprocessed
    /// markdown markers.  Syntax like <c>**</c>, <c>~~</c>, and backticks
    /// should always be converted to HTML tags, not leak into visible text.
    /// </summary>
    private static void VerifyNoRawMarkersInHtml(string markdown)
    {
        var result = MarkdownRenderer.Render(markdown);
        string visible = StripHtml(result.Html);

        // Check for raw ** (bold markers that weren't consumed)
        Assert.True(
            !visible.Contains("**"),
            $"Raw bold marker '**' leaked into visible HTML!  Visible: [{visible.Replace("\n", "\\n")}]  Markdown: [{markdown.Replace("\n", "\\n")}]");

        // Check for raw ~~ (strikethrough markers that weren't consumed)
        Assert.True(
            !visible.Contains("~~"),
            $"Raw strikethrough marker '~~' leaked into visible HTML!  Visible: [{visible.Replace("\n", "\\n")}]  Markdown: [{markdown.Replace("\n", "\\n")}]");

        // Check for raw ` (backtick markers that weren't consumed)
        // Note: backticks may appear inside <code> elements, but those are
        // rendered as HTML, not visible text.  If a backtick appears in the
        // stripped text, it means it wasn't properly converted.
        Assert.True(
            !visible.Contains('`'),
            $"Raw backtick '`' leaked into visible HTML!  Visible: [{visible.Replace("\n", "\\n")}]  Markdown: [{markdown.Replace("\n", "\\n")}]");
    }

    /// <summary>
    /// Verifies that the markdown doesn't have an excessive number of
    /// marker characters, which would indicate marker accumulation bugs.
    /// A well-behaved editor should never need more than ~10 marker chars
    /// per word for any combination of styles.
    /// </summary>
    private static void VerifyNoMarkerAccumulation(string markdown, int maxMarkers = 300)
    {
        int markerCount = 0;
        foreach (char c in markdown)
        {
            if (c == '*' || c == '~' || c == '`')
                markerCount++;
        }

        Assert.True(
            markerCount <= maxMarkers,
            $"Marker accumulation detected: {markerCount} marker chars (max {maxMarkers}) in: [{markdown.Replace("\n", "\\n")}]");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MAIN STRESS TEST
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Comprehensive stress test simulating a user randomly selecting text
    /// across multiple lines and clicking formatting buttons.
    ///
    /// <para>Starting text: "One two three\nfour five six\nseven eight nine"</para>
    ///
    /// <para>Specific initial operations:</para>
    /// <list type="number">
    ///   <item>Select "two"→"six", toggle Bold</item>
    ///   <item>Select "one"→"three", toggle Italic</item>
    ///   <item>Select "five"→"seven", toggle Code</item>
    ///   <item>Select "five"→"eight", toggle Strikethrough</item>
    ///   <item>Select "two"→"three", toggle Bold</item>
    /// </list>
    ///
    /// <para>Then 40 more operations with seeded RNG (seed=42), picking
    /// random word pairs and random actions.</para>
    ///
    /// <para>Final verification checks both markdown and HTML for:</para>
    /// <list type="bullet">
    ///   <item>Content preservation (all original words still visible)</item>
    ///   <item>No raw markers in rendered HTML</item>
    ///   <item>No marker accumulation</item>
    ///   <item>Clean, well-formed markdown</item>
    /// </list>
    /// </summary>
    [Fact]
    public void RandomSelections_45Operations_ShouldProduceCorrectMarkdownAndHtml()
    {
        // Deterministic seed for reproducibility
        const int Seed = 42;
        var rng = new Random(Seed);

        string text = "One two three\nfour five six\nseven eight nine";
        string[] originalWords = { "One", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        // Available formatting actions
        Func<string, int, int, TextEditResult>[] actions = {
            MarkdownTextExtensions.ToggleBold,
            MarkdownTextExtensions.ToggleItalic,
            MarkdownTextExtensions.ToggleStrikethrough,
            MarkdownTextExtensions.ToggleInlineCode,
        };
        string[] actionNames = { "Bold", "Italic", "Strikethrough", "Code" };

        // Track the sequence of operations for debugging
        var operationLog = new List<string>();

        // ── Helper: apply a toggle and verify invariants ──────────────
        void ApplyToggle(string actionName, Func<string, int, int, TextEditResult> action,
                         string fromWord, string toWord, int stepNum)
        {
            int fromStart = FindWordStart(text, fromWord);
            int toEnd = FindWordEnd(text, toWord);

            // Normalize: ensure start <= end
            int start = Math.Min(fromStart, toEnd);
            int end = Math.Max(fromStart, toEnd);

            if (start == end) end = start + fromWord.Length;

            string selectedText = text.Substring(start, Math.Min(end, text.Length) - start);
            operationLog.Add($"  Step {stepNum}: [{actionName}] select \"{fromWord}\"..\"{toWord}\" (pos {start}..{end}), selected: [{selectedText.Replace("\n", "\\n")}]");

            var result = action(text, start, end);
            text = result.Text;

            // Verify invariants after EVERY operation
            VerifyContentPreserved(text, originalWords);
            VerifyNoMarkerAccumulation(text);
        }

        // ══════════════════════════════════════════════════════════════
        //  PHASE 1: SPECIFIED INITIAL OPERATIONS
        // ══════════════════════════════════════════════════════════════

        // Step 1: Select "two" to "six", toggle Bold (multi-line)
        ApplyToggle("Bold", MarkdownTextExtensions.ToggleBold, "two", "six", 1);
        Assert.Equal("One **two three**\n**four five six**\nseven eight nine", text);

        // Step 2: Select "one" to "three", toggle Italic
        // "one" is at position 0, "three" is at position 10-14 in current text
        ApplyToggle("Italic", MarkdownTextExtensions.ToggleItalic, "one", "three", 2);

        // Step 3: Select "five" to "seven", toggle Code (multi-line)
        ApplyToggle("Code", MarkdownTextExtensions.ToggleInlineCode, "five", "seven", 3);

        // Step 4: Select "five" to "eight", toggle Strikethrough (multi-line)
        ApplyToggle("Strikethrough", MarkdownTextExtensions.ToggleStrikethrough, "five", "eight", 4);

        // Step 5: Select "two" to "three", toggle Bold
        ApplyToggle("Bold", MarkdownTextExtensions.ToggleBold, "two", "three", 5);

        // ══════════════════════════════════════════════════════════════
        //  PHASE 2: 40 RANDOM OPERATIONS (seeded)
        // ══════════════════════════════════════════════════════════════

        for (int i = 0; i < 40; i++)
        {
            var currentWords = ExtractWords(text);
            if (currentWords.Count < 2) break;

            // Pick two random words (may be same word for zero-width selection)
            int fromIdx = rng.Next(currentWords.Count);
            int toIdx = rng.Next(currentWords.Count);
            int actionIdx = rng.Next(actions.Length);

            ApplyToggle(
                actionNames[actionIdx],
                actions[actionIdx],
                currentWords[fromIdx],
                currentWords[toIdx],
                6 + i);
        }

        // ══════════════════════════════════════════════════════════════
        //  PHASE 3: FINAL VERIFICATION
        // ══════════════════════════════════════════════════════════════

        // Render the final markdown
        var finalRender = MarkdownRenderer.Render(text);
        Assert.NotNull(finalRender);
        Assert.NotEmpty(finalRender.Html);

        // 1. All original content words must be present in the visible text
        VerifyContentPreserved(text, originalWords);

        // 2. No raw markers should leak into the rendered HTML
        VerifyNoRawMarkersInHtml(text);

        // 3. No marker accumulation
        VerifyNoMarkerAccumulation(text);

        // 4. Verify each line renders independently (no cross-line corruption)
        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            // Each line should render without throwing
            var lineRender = MarkdownRenderer.Render(line);
            Assert.NotNull(lineRender);
        }

        // 5. Verify the HTML structure is well-formed (all opened tags are closed)
        string html = finalRender.Html;
        VerifyBalancedTags(html, "strong");
        VerifyBalancedTags(html, "em");
        VerifyBalancedTags(html, "del");
        VerifyBalancedTags(html, "code");

        // Log the final state for diagnostics
        string finalVisible = GetVisibleText(text);
        Assert.False(string.IsNullOrWhiteSpace(finalVisible),
            "Final visible text should not be empty");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Tag-balancing verification
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that all opening tags of a given type are properly closed
    /// in the HTML.  Uses a simple depth counter — starts at 0, increments
    /// for each opening tag, decrements for each closing tag.  The final
    /// depth must be 0, and the depth must never go negative.
    /// </summary>
    private static void VerifyBalancedTags(string html, string tagName)
    {
        int depth = 0;
        int pos = 0;
        string openTag = $"<{tagName}";
        string closeTag = $"</{tagName}>";

        while (pos < html.Length)
        {
            int openIdx = html.IndexOf(openTag, pos, StringComparison.OrdinalIgnoreCase);
            int closeIdx = html.IndexOf(closeTag, pos, StringComparison.OrdinalIgnoreCase);

            // Determine which comes first
            if (openIdx < 0 && closeIdx < 0) break;

            bool isOpenFirst = (openIdx >= 0) && (closeIdx < 0 || openIdx < closeIdx);

            if (isOpenFirst)
            {
                // Check it's not a self-closing or void tag
                int endOfTag = html.IndexOf('>', openIdx);
                string tagContent = html.Substring(openIdx, endOfTag - openIdx + 1);
                if (!tagContent.EndsWith("/>") && !tagContent.Contains("</"))
                {
                    depth++;
                    Assert.True(depth <= 3,
                        $"Excessive nesting depth ({depth}) for <{tagName}> — indicates marker accumulation in markdown: [{html}]");
                }
                pos = endOfTag + 1;
            }
            else
            {
                depth--;
                Assert.True(depth >= 0,
                    $"Unbalanced tag <{tagName}>: closing tag found without matching opener at pos {closeIdx} in: [{html}]");
                pos = closeIdx + closeTag.Length;
            }
        }

        Assert.Equal(0, depth);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Simpler unit-level toggle cycle tests
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that toggling the same formatting on and off 10 times
    /// over a multi-line selection returns the text to its original state.
    /// </summary>
    [Theory]
    [InlineData("Bold", "two", "six")]
    [InlineData("Italic", "two", "six")]
    [InlineData("Strikethrough", "two", "six")]
    [InlineData("Code", "two", "six")]
    [InlineData("Bold", "five", "seven")]
    [InlineData("Italic", "five", "seven")]
    [InlineData("Strikethrough", "five", "seven")]
    [InlineData("Code", "five", "seven")]
    [InlineData("Bold", "one", "nine")]
    [InlineData("Italic", "one", "nine")]
    [InlineData("Strikethrough", "one", "nine")]
    [InlineData("Code", "one", "nine")]
    public void ToggleOnOff_10Times_MultiLine_ShouldReturnToOriginal(
        string styleName, string fromWord, string toWord)
    {
        const string OriginalText = "One two three\nfour five six\nseven eight nine";

        Func<string, int, int, TextEditResult> action = styleName switch
        {
            "Bold" => MarkdownTextExtensions.ToggleBold,
            "Italic" => MarkdownTextExtensions.ToggleItalic,
            "Strikethrough" => MarkdownTextExtensions.ToggleStrikethrough,
            "Code" => MarkdownTextExtensions.ToggleInlineCode,
            _ => throw new ArgumentException($"Unknown style: {styleName}")
        };

        string text = OriginalText;
        int start = FindWordStart(text, fromWord);
        int end = FindWordEnd(text, toWord);

        // Toggle ON then OFF, 10 times
        for (int i = 0; i < 10; i++)
        {
            var result = action(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;

            // Every even iteration should be back to original
            if (i % 2 == 1)
            {
                Assert.True(text == OriginalText,
                    $"After {(i + 1)} toggles of {styleName}, text should be back to original.  Got: [{text.Replace("\n", "\\n")}]");
            }

            // Verify no marker accumulation at any point
            VerifyNoMarkerAccumulation(text);
            VerifyNoRawMarkersInHtml(text);
        }

        // Final state must be original
        Assert.Equal(OriginalText, text);
    }

    /// <summary>
    /// Verifies that toggling all 4 formatting styles on and off in sequence
    /// over a multi-line selection returns the text to its original state.
    /// </summary>
    [Fact]
    public void ToggleAllFourStyles_Cycle_MultiLine_ShouldReturnToOriginal()
    {
        const string OriginalText = "One two three\nfour five six\nseven eight nine";
        string text = OriginalText;

        Func<string, int, int, TextEditResult>[] actions = {
            MarkdownTextExtensions.ToggleBold,
            MarkdownTextExtensions.ToggleItalic,
            MarkdownTextExtensions.ToggleStrikethrough,
            MarkdownTextExtensions.ToggleInlineCode,
        };

        int start = FindWordStart(text, "two");
        int end = FindWordEnd(text, "six");

        // Full cycle: Bold ON, Italic ON, Strikethrough ON, Code ON,
        //             Bold OFF, Italic OFF, Strikethrough OFF, Code OFF
        for (int cycle = 0; cycle < 4; cycle++)
        {
            foreach (var action in actions)
            {
                var result = action(text, start, end);
                text = result.Text;
                start = result.SelectionStart;
                end = result.SelectionEnd;

                VerifyNoMarkerAccumulation(text);
                VerifyNoRawMarkersInHtml(text);
            }
        }

        // After 4 full cycles (16 toggles), should be back to original
        Assert.Equal(OriginalText, text);
    }

    /// <summary>
    /// Simulates realistic user behavior: apply styles, change selection,
    /// apply more styles, change selection again, etc.
    /// </summary>
    [Fact]
    public void RealisticUserWorkflow_MixedSelections_ShouldStayClean()
    {
        string text = "One two three\nfour five six\nseven eight nine";
        string[] originalWords = { "One", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        // User scenario:
        // 1. Bold "two" through "six" (multi-line)
        int start = FindWordStart(text, "two");
        int end = FindWordEnd(text, "six");
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text;

        // 2. Italic "three" (single word, within existing bold)
        start = FindWordStart(text, "three");
        end = FindWordEnd(text, "three");
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = result.Text;

        VerifyNoRawMarkersInHtml(text);

        // 3. Code on "four" through "seven" (multi-line, crossing bold boundary)
        start = FindWordStart(text, "four");
        end = FindWordEnd(text, "seven");
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = result.Text;

        VerifyNoRawMarkersInHtml(text);
        VerifyContentPreserved(text, originalWords);

        // 4. Strikethrough "five" through "eight" (multi-line, crossing multiple boundaries)
        start = FindWordStart(text, "five");
        end = FindWordEnd(text, "eight");
        result = MarkdownTextExtensions.ToggleStrikethrough(text, start, end);
        text = result.Text;

        VerifyNoRawMarkersInHtml(text);
        VerifyContentPreserved(text, originalWords);

        // 5. Remove Bold from "two" (within already-formatted text)
        start = FindWordStart(text, "two");
        end = FindWordEnd(text, "two");
        result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text;

        VerifyNoRawMarkersInHtml(text);
        VerifyContentPreserved(text, originalWords);
        VerifyNoMarkerAccumulation(text);

        // 6. Remove Italic from "three"
        start = FindWordStart(text, "three");
        end = FindWordEnd(text, "three");
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        text = result.Text;

        VerifyNoRawMarkersInHtml(text);
        VerifyContentPreserved(text, originalWords);

        // 7. Remove Code from "six"
        start = FindWordStart(text, "six");
        end = FindWordEnd(text, "six");
        result = MarkdownTextExtensions.ToggleInlineCode(text, start, end);
        text = result.Text;

        VerifyNoRawMarkersInHtml(text);
        VerifyContentPreserved(text, originalWords);
    }
}
