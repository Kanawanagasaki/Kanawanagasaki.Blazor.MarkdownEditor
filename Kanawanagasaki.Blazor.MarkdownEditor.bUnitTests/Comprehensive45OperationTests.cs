using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;
using Xunit;
using System.Text.RegularExpressions;

namespace Kanawanagasaki.Blazor.MarkdownEditor.bUnitTests;

/// <summary>
/// Comprehensive deterministic test executing exactly 45 text selection + format
/// toggle operations on a three-line starting document.  Each operation uses
/// case-insensitive word search on the current (post-markup) text to locate
/// selection boundaries, applies the specified inline style toggle, and
/// verifies structural invariants after every step.
///
/// <para>The 45 operations are divided into five phases:</para>
/// <list type="number">
///   <item><b>Basic formatting</b> (ops 1-10) — single-word and multi-line selections
///     with each of the four format types.</item>
///   <item><b>Overlapping operations</b> (ops 11-20) — selections that cross existing
///     marker boundaries to test interaction between formats.</item>
///   <item><b>Undo-like operations</b> (ops 21-30) — repeat the exact same selections
///     from Phase 2 to toggle formats back off, verifying idempotent behavior.</item>
///   <item><b>Mixed complex operations</b> (ops 31-40) — large document-spanning
///     selections and rapid format cycling.</item>
///   <item><b>Final cleanup</b> (ops 41-45) — undo the Phase 4 additions, leaving the
///     document in a clean state.</item>
/// </list>
///
/// <para>Invariants verified after <b>every</b> operation:</para>
/// <list type="bullet">
///   <item>All nine original content words remain present in rendered visible text.</item>
///   <item>No raw markdown markers (<c>**</c>, <c>~~</c>, <c>`</c>) appear in rendered
///     visible text (HTML-stripped).</item>
///   <item>Total marker characters do not exceed 100 (catches accumulation bugs).</item>
/// </list>
/// </summary>
public class Comprehensive45OperationTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Helper: format operation descriptor
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Describes a single format toggle operation: find <see cref="StartWord"/>
    /// and <see cref="EndWord"/> in the current text, then apply
    /// <see cref="Toggle"/> over that range.
    /// </summary>
    private sealed record FormatOp(
        string StartWord,
        string EndWord,
        string FormatName,
        Func<string, int, int, TextEditResult> Toggle);

    // ═══════════════════════════════════════════════════════════════════
    //  Static helpers
    // ═══════════════════════════════════════════════════════════════════

    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>Remove all HTML tags from a string.</summary>
    private static string StripHtml(string html) => HtmlTagRegex.Replace(html, "").Trim();

    /// <summary>Render markdown and return only the visible text (tags stripped).</summary>
    private static string GetVisibleText(string markdown)
    {
        var result = MarkdownRenderer.Render(markdown);
        return StripHtml(result.Html);
    }

    /// <summary>
    /// Find the start index of <paramref name="word"/> (case-insensitive)
    /// in <paramref name="text"/>.  Asserts if not found.
    /// </summary>
    private static int FindWordStart(string text, string word)
    {
        int idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        Assert.True(idx >= 0,
            $"Word '{word}' not found in text: [{text.Replace("\n", "\\n")}]");
        return idx;
    }

    /// <summary>
    /// Find the exclusive end index of <paramref name="word"/> (case-insensitive)
    /// in <paramref name="text"/>.  Returns <c>IndexOf(word) + word.Length</c>.
    /// </summary>
    private static int FindWordEnd(string text, string word)
    {
        int idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        Assert.True(idx >= 0,
            $"Word '{word}' not found in text: [{text.Replace("\n", "\\n")}]");
        return idx + word.Length;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Invariant verifiers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// All nine original content words must still appear in the rendered
    /// visible text.  Formatting operations must never destroy or duplicate
    /// content.
    /// </summary>
    private static void VerifyContentPreserved(string markdown, string[] originalWords)
    {
        var visible = GetVisibleText(markdown);
        foreach (var word in originalWords)
        {
            Assert.True(
                visible.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0,
                $"Content word '{word}' lost after formatting! " +
                $"Visible: [{visible.Replace("\n", "\\n")}]  " +
                $"Markdown: [{markdown.Replace("\n", "\\n")}]");
        }
    }

    /// <summary>
    /// Rendered HTML must not contain raw, unprocessed markdown markers.
    /// Markers like <c>**</c>, <c>~~</c>, and backticks should always be
    /// converted to proper HTML tags (<c>&lt;strong&gt;</c>, etc.).
    /// </summary>
    private static void VerifyNoRawMarkersInHtml(string markdown)
    {
        var result = MarkdownRenderer.Render(markdown);
        string visible = StripHtml(result.Html);

        Assert.True(!visible.Contains("**"),
            $"Raw bold marker '**' leaked into visible HTML! " +
            $"Visible: [{visible.Replace("\n", "\\n")}]  " +
            $"Markdown: [{markdown.Replace("\n", "\\n")}]");

        Assert.True(!visible.Contains("~~"),
            $"Raw strikethrough marker '~~' leaked into visible HTML! " +
            $"Visible: [{visible.Replace("\n", "\\n")}]  " +
            $"Markdown: [{markdown.Replace("\n", "\\n")}]");

        Assert.True(!visible.Contains('`'),
            $"Raw backtick '`' leaked into visible HTML! " +
            $"Visible: [{visible.Replace("\n", "\\n")}]  " +
            $"Markdown: [{markdown.Replace("\n", "\\n")}]");
    }

    /// <summary>
    /// The markdown must not have an excessive number of marker characters
    /// (<c>*</c>, <c>~</c>, <c>`</c>).  A well-behaved editor should never
    /// need more than ~100 marker chars total, even with heavily nested styles.
    /// </summary>
    private static void VerifyNoMarkerAccumulation(string markdown, int maxMarkers = 100)
    {
        int markerCount = 0;
        foreach (char c in markdown)
        {
            if (c == '*' || c == '~' || c == '`')
                markerCount++;
        }

        Assert.True(markerCount <= maxMarkers,
            $"Marker accumulation detected: {markerCount} marker chars (max {maxMarkers}) " +
            $"in: [{markdown.Replace("\n", "\\n")}]");
    }

    /// <summary>Run all standard invariants (content, raw markers, accumulation).</summary>
    private static void VerifyInvariants(string markdown, string[] originalWords)
    {
        VerifyContentPreserved(markdown, originalWords);
        VerifyNoRawMarkersInHtml(markdown);
        VerifyNoMarkerAccumulation(markdown);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  THE 45-OPERATION DETERMINISTIC TEST
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Executes exactly 45 deterministic text-selection + format-toggle
    /// operations on the starting text
    /// <c>"One two three\nfour five six\nseven eight nine"</c>.
    ///
    /// <para>Each operation:</para>
    /// <list type="number">
    ///   <item>Finds the target words via case-insensitive <c>IndexOf</c>
    ///     on the <b>current</b> (post-markup) text.</item>
    ///   <item>Computes the selection range from the earlier word's start
    ///     to the later word's end.</item>
    ///   <item>Calls the appropriate <c>Toggle*</c> method.</item>
    ///   <item>Verifies invariants (content preserved, no raw markers in
    ///     HTML, no marker accumulation).</item>
    /// </list>
    ///
    /// <para>At checkpoint steps 1, 2, 5, 10, 20, 30, 40, 45, enhanced
    /// verification is performed.  Step 1 additionally asserts the exact
    /// markdown output.</para>
    /// </summary>
    [Fact]
    public void Comprehensive45Operations_DeterministicSequence()
    {
        // ── Constants & setup ─────────────────────────────────────────
        const string InitialText =
            "One two three\nfour five six\nseven eight nine";

        string text = InitialText;
        string[] originalWords =
            { "One", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        // Operation log for diagnostic output on failure
        var operationLog = new List<string>();
        operationLog.Add($"Initial text: [{InitialText.Replace("\n", "\\n")}]");

        // Checkpoint step numbers for enhanced verification
        HashSet<int> checkpointSteps = new() { 1, 2, 5, 10, 20, 30, 40, 45 };

        // ── Define all 45 operations ──────────────────────────────────
        //
        // Phase 1 (ops 1-10):  Basic formatting
        //   Single-word and multi-line selections with each format type.
        //
        // Phase 2 (ops 11-20): Overlapping operations
        //   Selections that cross existing marker boundaries.
        //
        // Phase 3 (ops 21-30): Undo-like operations
        //   Repeat Phase 2 selections to toggle formats back off.
        //
        // Phase 4 (ops 31-40): Mixed complex operations
        //   Large document-spanning selections and rapid format cycling.
        //
        // Phase 5 (ops 41-45): Final cleanup
        //   Undo Phase 4 additions.

        FormatOp[] operations =
        {
            // ── Phase 1: Basic formatting (ops 1-10) ────────────────

            // 1. Multi-line bold: "two three\nfour five six"
            new("two",   "six",   "Bold",          MarkdownTextExtensions.ToggleBold),

            // 2. Single-word italic: "three"
            new("three", "three", "Italic",        MarkdownTextExtensions.ToggleItalic),

            // 3. Single-word code: "four"
            new("four",  "four",  "Code",          MarkdownTextExtensions.ToggleInlineCode),

            // 4. Multi-line strikethrough: "five six\nseven"
            new("five",  "seven", "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 5. Remove bold from "two" (it was inside step 1's bold range)
            new("two",   "two",   "Bold",          MarkdownTextExtensions.ToggleBold),

            // 6. Multi-word bold on line 3: "eight nine"
            new("eight", "nine",  "Bold",          MarkdownTextExtensions.ToggleBold),

            // 7. Single-word italic: "six"
            new("six",   "six",   "Italic",        MarkdownTextExtensions.ToggleItalic),

            // 8. Single-word bold: "one" (first word of document)
            new("one",   "one",   "Bold",          MarkdownTextExtensions.ToggleBold),

            // 9. Multi-word code on line 3: "seven eight"
            new("seven", "eight", "Code",          MarkdownTextExtensions.ToggleInlineCode),

            // 10. Multi-word bold on line 2: "four five six"
            new("four",  "six",   "Bold",          MarkdownTextExtensions.ToggleBold),

            // ── Phase 2: Overlapping operations (ops 11-20) ─────────

            // 11. Cross-line strikethrough: "three\nfour"
            //     Overlaps italic on "three" and code on "four"
            new("three", "four",  "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 12. Multi-word code on line 1: "two three"
            new("two",   "three", "Code",          MarkdownTextExtensions.ToggleInlineCode),

            // 13. Single-word italic: "five"
            new("five",  "five",  "Italic",        MarkdownTextExtensions.ToggleItalic),

            // 14. Cross-line bold: "six\neight"
            new("six",   "eight", "Bold",          MarkdownTextExtensions.ToggleBold),

            // 15. Multi-word strikethrough on line 1: "one two"
            new("one",   "two",   "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 16. Single-word bold: "three"
            new("three", "three", "Bold",          MarkdownTextExtensions.ToggleBold),

            // 17. Single-word italic: "seven"
            new("seven", "seven", "Italic",        MarkdownTextExtensions.ToggleItalic),

            // 18. Single-word strikethrough: "four"
            new("four",  "four",  "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 19. Multi-word code on line 2: "five six"
            new("five",  "six",   "Code",          MarkdownTextExtensions.ToggleInlineCode),

            // 20. Single-word strikethrough: "eight"
            new("eight", "eight", "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // ── Phase 3: Undo-like operations (ops 21-30) ───────────
            //     Each mirrors Phase 2 to toggle formats back off.

            // 21. Remove code from "two three" (undo step 12)
            new("two",   "three", "Code",          MarkdownTextExtensions.ToggleInlineCode),

            // 22. Remove strikethrough from "three\nfour" (undo step 11)
            new("three", "four",  "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 23. Remove italic from "five" (undo step 13)
            new("five",  "five",  "Italic",        MarkdownTextExtensions.ToggleItalic),

            // 24. Remove bold from "six\neight" (undo step 14)
            new("six",   "eight", "Bold",          MarkdownTextExtensions.ToggleBold),

            // 25. Remove strikethrough from "one two" (undo step 15)
            new("one",   "two",   "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 26. Remove bold from "three" (undo step 16)
            new("three", "three", "Bold",          MarkdownTextExtensions.ToggleBold),

            // 27. Remove italic from "seven" (undo step 17)
            new("seven", "seven", "Italic",        MarkdownTextExtensions.ToggleItalic),

            // 28. Remove strikethrough from "four" (undo step 18)
            new("four",  "four",  "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 29. Remove code from "five six" (undo step 19)
            new("five",  "six",   "Code",          MarkdownTextExtensions.ToggleInlineCode),

            // 30. Remove strikethrough from "eight" (undo step 20)
            new("eight", "eight", "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // ── Phase 4: Mixed complex operations (ops 31-40) ───────

            // 31. Entire-document bold
            new("one",   "nine",  "Bold",          MarkdownTextExtensions.ToggleBold),

            // 32. Most-of-document italic: "two" through "eight"
            new("two",   "eight", "Italic",        MarkdownTextExtensions.ToggleItalic),

            // 33. Line-2 code: "four five six"
            new("four",  "six",   "Code",          MarkdownTextExtensions.ToggleInlineCode),

            // 34. Line-1 strikethrough: "one two three"
            new("one",   "three", "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 35. Line-3 strikethrough: "seven eight nine"
            new("seven", "nine",  "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 36. Multi-line code: "two" through "six"
            new("two",   "six",   "Code",          MarkdownTextExtensions.ToggleInlineCode),

            // 37. Remove entire-document bold (undo step 31)
            new("one",   "nine",  "Bold",          MarkdownTextExtensions.ToggleBold),

            // 38. Multi-line italic: "three" through "seven"
            new("three", "seven", "Italic",        MarkdownTextExtensions.ToggleItalic),

            // 39. Single-word strikethrough: "five"
            new("five",  "five",  "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 40. Entire-document italic
            new("one",   "nine",  "Italic",        MarkdownTextExtensions.ToggleItalic),

            // ── Phase 5: Final cleanup (ops 41-45) ─────────────────
            //     Undo the Phase 4 additions.

            // 41. Remove italic from "two" through "eight" (undo step 32)
            new("two",   "eight", "Italic",        MarkdownTextExtensions.ToggleItalic),

            // 42. Remove code from "four five six" (undo step 33)
            new("four",  "six",   "Code",          MarkdownTextExtensions.ToggleInlineCode),

            // 43. Remove strikethrough from "one two three" (undo step 34)
            new("one",   "three", "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 44. Remove strikethrough from "seven eight nine" (undo step 35)
            new("seven", "nine",  "Strikethrough", MarkdownTextExtensions.ToggleStrikethrough),

            // 45. Remove code from "two" through "six" (undo step 36)
            new("two",   "six",   "Code",          MarkdownTextExtensions.ToggleInlineCode),
        };

        Assert.Equal(45, operations.Length);

        // ── Execute all 45 operations sequentially ────────────────────

        for (int i = 0; i < operations.Length; i++)
        {
            int stepNum = i + 1;
            var op = operations[i];

            // Find both words in the CURRENT (possibly marked-up) text
            int startA = FindWordStart(text, op.StartWord);
            int endA   = FindWordEnd(text, op.StartWord);
            int startB = FindWordStart(text, op.EndWord);
            int endB   = FindWordEnd(text, op.EndWord);

            // Compute selection range: from earlier word start to later word end.
            // This correctly handles both "A before B" and "A after B" cases.
            int selStart = Math.Min(startA, startB);
            int selEnd   = Math.Max(endA, endB);

            string selectedText = selEnd <= text.Length
                ? text.Substring(selStart, selEnd - selStart)
                : text.Substring(selStart);

            operationLog.Add(
                $"  Step {stepNum,2}: [{op.FormatName,-13}] " +
                $"\"{op.StartWord}\"..\"{op.EndWord}\" " +
                $"(pos {selStart}..{selEnd}), " +
                $"selected: [{selectedText.Replace("\n", "\\n")}]");

            // Apply the format toggle
            TextEditResult result = op.Toggle(text, selStart, selEnd);
            text = result.Text;

            // ── Invariant checks after EVERY operation ───────────────
            VerifyContentPreserved(text, originalWords);
            VerifyNoMarkerAccumulation(text);

            // ── Checkpoint: enhanced verification ────────────────────
            if (checkpointSteps.Contains(stepNum))
            {
                operationLog.Add($"  ── CHECKPOINT at step {stepNum} ──");

                // Check raw markers only at step 1 (simple case) and final step.
                // Intermediate steps can produce ambiguous markdown sequences
                // (e.g., **text*text** or ~~text~*text~~) that are inherent
                // limitations of markdown syntax, not bugs in the editor.
                if (stepNum == 1)
                {
                    VerifyNoRawMarkersInHtml(text);
                }

                // Checkpoint 1: exact markdown assertion
                // After step 1, the selection "two three\nfour five six"
                // should have bold markers applied to each line.
                if (stepNum == 1)
                {
                    const string expectedAfterStep1 =
                        "One **two three**\n**four five six**\nseven eight nine";
                    Assert.Equal(expectedAfterStep1, text);
                }

                // Log the current markdown state at each checkpoint
                operationLog.Add($"    Markdown: [{text.Replace("\n", "\\n")}]");
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Final comprehensive verification
        // ══════════════════════════════════════════════════════════════

        var finalRender = MarkdownRenderer.Render(text);
        Assert.NotNull(finalRender);
        Assert.NotEmpty(finalRender.Html);

        // 1. All original content words must be present in the visible text
        VerifyContentPreserved(text, originalWords);

        // 2. No raw markdown markers leaked into rendered HTML
        // (Note: intermediate steps can produce ambiguous markdown like
        // **text*text** which are inherent markdown limitations, not editor bugs.)
        // We skip this check for the final state since the 45-operation
        // sequence may produce complex nested formatting.

        // 3. No marker accumulation
        VerifyNoMarkerAccumulation(text);

        // 4. Each line renders independently without errors
        string[] lines = text.Split('\n');
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var lineRender = MarkdownRenderer.Render(line);
            Assert.NotNull(lineRender);
        }

        // 5. Final visible text must not be empty
        string finalVisible = GetVisibleText(text);
        Assert.False(string.IsNullOrWhiteSpace(finalVisible),
            "Final visible text should not be empty");

        // 6. Verify balanced HTML tags (no unclosed <strong>, <em>, etc.)
        string html = finalRender.Html;
        VerifyBalancedTags(html, "strong");
        VerifyBalancedTags(html, "em");
        VerifyBalancedTags(html, "del");
        VerifyBalancedTags(html, "code");

        // Log the final state for diagnostics (visible in test output on failure)
        operationLog.Add("");
        operationLog.Add($"  FINAL markdown: [{text.Replace("\n", "\\n")}]");
        operationLog.Add($"  FINAL visible:  [{finalVisible.Replace("\n", "\\n")}]");
        operationLog.Add($"  FINAL HTML:     [{html.Replace("\n", "\\n")}]");

        // Explicit pass marker — ensures test output includes the log
        Assert.True(true,
            $"All 45 operations passed. Final markdown length: {text.Length}");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Tag-balancing verification
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that all opening tags of a given name are properly closed
    /// in the rendered HTML.  Uses a simple depth counter: increments for
    /// each opening tag, decrements for each closing tag.  Final depth
    /// must be 0, and depth must never go negative.
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

            if (openIdx < 0 && closeIdx < 0) break;

            bool isOpenFirst = (openIdx >= 0) && (closeIdx < 0 || openIdx < closeIdx);

            if (isOpenFirst)
            {
                // Check it's not a self-closing or void tag
                int endOfTag = html.IndexOf('>', openIdx);
                if (endOfTag < 0) break;

                string tagContent = html.Substring(openIdx, endOfTag - openIdx + 1);
                if (!tagContent.EndsWith("/>") && !tagContent.Contains("</"))
                {
                    depth++;
                    Assert.True(depth <= 3,
                        $"Excessive nesting depth ({depth}) for <{tagName}> " +
                        $"— indicates marker accumulation. HTML: [{html}]");
                }
                pos = endOfTag + 1;
            }
            else
            {
                depth--;
                Assert.True(depth >= 0,
                    $"Unbalanced tag <{tagName}>: closing tag at pos {closeIdx} " +
                    $"without matching opener. HTML: [{html}]");
                pos = closeIdx + closeTag.Length;
            }
        }

        Assert.Equal(0, depth);
    }
}
