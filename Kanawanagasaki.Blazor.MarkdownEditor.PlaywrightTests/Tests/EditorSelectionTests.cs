using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Mouse-based selection tests: verifies that drag-selecting text on
/// the rendered overlay correctly updates both the textarea selection
/// and the visible native browser selection, and that toolbar toggle
/// actions (bold / italic) work correctly on mouse-selected ranges.
///
/// All tests use real mouse events (mousedown → mousemove → mouseup)
/// to exercise the full selection pipeline end-to-end.
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorSelectionTests : EditorTestBase
{
    public EditorSelectionTests(TestAppFixture fixture)
        : base(fixture) { }

    private const string TestContent =
        "One Two Three\nFour Five Six\nSeven Eight Nine";

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Fills the editor with the standard 3-line test content and waits
    /// for line mappings to be pushed from Blazor.
    /// </summary>
    private async Task FillTestContent()
    {
        await NavigateToEditor();
        await FillContentAndWaitForMappings(TestContent, 3);
    }

    /// <summary>
    /// Returns the bounding box of the n-th occurrence of <paramref name="word"/>
    /// in the overlay's rendered text. Uses the Range API for pixel-perfect
    /// positioning.
    /// </summary>
    private async Task<TextBounds> GetWordBoundsAsync(string word, int occurrence = 0)
    {
        return await Page.EvaluateAsync<TextBounds>(@"(args) => {
            const [word, occ] = args;
            const overlay = document.querySelector('.md-overlay');
            const walker = document.createTreeWalker(overlay, NodeFilter.SHOW_TEXT);
            let node;
            let count = 0;
            while ((node = walker.nextNode())) {
                let idx = node.textContent.indexOf(word);
                while (idx !== -1) {
                    if (count === occ) {
                        const range = document.createRange();
                        range.setStart(node, idx);
                        range.setEnd(node, idx + word.length);
                        const rect = range.getBoundingClientRect();
                        return {
                            x: rect.x,
                            y: rect.y,
                            width: rect.width,
                            height: rect.height,
                            right: rect.right,
                            bottom: rect.bottom,
                            centerX: rect.x + rect.width / 2,
                            centerY: rect.y + rect.height / 2
                        };
                    }
                    count++;
                    idx = node.textContent.indexOf(word, idx + 1);
                }
            }
            return null;
        }", new object[] { word, occurrence });
    }

    /// <summary>
    /// Performs a mouse drag selection from one point to another on the
    /// overlay. Uses small intermediate moves to simulate real dragging.
    /// </summary>
    private async Task DragSelectAsync(double startX, double startY, double endX, double endY, int steps = 5)
    {
        // Move to start position
        await Page.Mouse.MoveAsync((float)startX, (float)startY);
        await Page.Mouse.DownAsync();

        // Interpolate moves for smooth drag
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double x = startX + (endX - startX) * t;
            double y = startY + (endY - startY) * t;
            await Page.Mouse.MoveAsync((float)x, (float)y);
        }

        // Final position
        await Page.Mouse.MoveAsync((float)endX, (float)endY);
        await Page.Mouse.UpAsync();
    }

    /// <summary>
    /// Asserts that the textarea selection matches the expected range and
    /// that the native browser selection on the overlay contains the expected
    /// visible text. For multi-line selections, the native selection text may
    /// include extra newlines between block-level line elements (browser standard
    /// behavior), so we verify containment of key words rather than exact match.
    /// </summary>
    private async Task AssertSelectionAsync(int expectedStart, int expectedEnd, string expectedSelectedText)
    {
        // Check textarea selection
        var textareaSel = await Page.EvaluateAsync<TextareaSelection>(@"() => {
            const ta = document.querySelector('.md-textarea');
            return { start: ta.selectionStart, end: ta.selectionEnd };
        }");
        Assert.Equal(expectedStart, textareaSel.Start);
        Assert.Equal(expectedEnd, textareaSel.End);

        // Check native browser selection on overlay
        var nativeSelText = await Page.EvaluateAsync<string>(@"() => {
            return window.getSelection()?.toString() || '';
        }");

        // For single-line selections, exact match is expected
        if (!expectedSelectedText.Contains('\n'))
        {
            Assert.Equal(expectedSelectedText, nativeSelText);
        }
        else
        {
            // For multi-line, the browser adds extra newlines between block elements.
            // Verify all non-empty words from expected text appear in the native selection.
            var words = expectedSelectedText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                Assert.Contains(word.Trim(), nativeSelText);
            }
        }
    }

    /// <summary>
    /// Runs the bold → italic → bold → italic toggle sequence and verifies
    /// the raw markdown at each step. Returns after all 4 toggles so the
    /// caller can do final assertions.
    /// </summary>
    private async Task RunToggleSequenceAsync(
        string afterBoldContains,
        string afterItalicContains,
        string afterBoldOffContains,
        string afterItalicOffNotContains)
    {
        var boldBtn = Page.Locator(".md-btn-bold");
        var italicBtn = Page.Locator(".md-btn-italic");

        // Toggle 1: Bold ON
        await boldBtn.ClickAsync();
        await WaitForOverlayUpdate();
        var raw = await GetRawValue();
        Assert.Contains(afterBoldContains, raw);

        // Toggle 2: Italic ON
        await italicBtn.ClickAsync();
        await WaitForOverlayUpdate();
        raw = await GetRawValue();
        Assert.Contains(afterItalicContains, raw);

        // Toggle 3: Bold OFF
        await boldBtn.ClickAsync();
        await WaitForOverlayUpdate();
        raw = await GetRawValue();
        Assert.Contains(afterBoldOffContains, raw);

        // Toggle 4: Italic OFF
        await italicBtn.ClickAsync();
        await WaitForOverlayUpdate();
        raw = await GetRawValue();
        Assert.DoesNotContain(afterItalicOffNotContains, raw);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Test 1: Select "Five" start → end
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DragSelect_Five_StartToEnd_ShouldSelectFive()
    {
        await FillTestContent();

        // Get bounding box of "Five" in the overlay
        var five = await GetWordBoundsAsync("Five", 0);
        Assert.NotNull(five);

        // Drag from start of "Five" to end of "Five"
        await DragSelectAsync(five.X, five.CenterY, five.Right, five.CenterY);

        // "Five" in source: "One Two Three\nFour Five Six\nSeven Eight Nine"
        // Positions: O(0)...F(19)i(20)v(21)e(22) → start=19, end=23
        // Visible selection text should be "Five"
        await AssertSelectionAsync(19, 23, "Five");

        // Bold → Italic → Bold → Italic toggle sequence
        await RunToggleSequenceAsync(
            afterBoldContains: "**Five**",
            afterItalicContains: "***Five***",
            afterBoldOffContains: "*Five*",
            afterItalicOffNotContains: "*Five*"
        );

        // After all toggles, text should be back to original
        var finalRaw = await GetRawValue();
        Assert.DoesNotContain("**", finalRaw);
        Assert.DoesNotContain("*Five*", finalRaw);
        Assert.Contains("Four Five Six", finalRaw);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Test 2: Select "Five" end → start (reverse)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DragSelect_Five_EndToStart_ShouldSelectFive()
    {
        await FillTestContent();

        // Get bounding box of "Five" in the overlay
        var five = await GetWordBoundsAsync("Five", 0);
        Assert.NotNull(five);

        // Drag from END of "Five" to START of "Five" (reverse direction)
        await DragSelectAsync(five.Right, five.CenterY, five.X, five.CenterY);

        // Regardless of direction, textarea sel should be normalized: start=19, end=23
        await AssertSelectionAsync(19, 23, "Five");

        // Same toggle sequence
        await RunToggleSequenceAsync(
            afterBoldContains: "**Five**",
            afterItalicContains: "***Five***",
            afterBoldOffContains: "*Five*",
            afterItalicOffNotContains: "*Five*"
        );

        var finalRaw = await GetRawValue();
        Assert.DoesNotContain("**", finalRaw);
        Assert.DoesNotContain("*Five*", finalRaw);
        Assert.Contains("Four Five Six", finalRaw);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Test 3: Select "Two" through "Eight" start → end (multi-line)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DragSelect_TwoThroughEight_StartToEnd_ShouldSelectMultiLine()
    {
        await FillTestContent();

        // Get bounding boxes
        var two = await GetWordBoundsAsync("Two", 0);
        var eight = await GetWordBoundsAsync("Eight", 0);
        Assert.NotNull(two);
        Assert.NotNull(eight);

        // Drag from start of "Two" to end of "Eight"
        await DragSelectAsync(two.X, two.CenterY, eight.Right, eight.CenterY, steps: 10);

        // Source positions:
        // "Two" → start=4, end=7
        // "Eight" → start=34, end=39
        // Selection spans "Two Three\nFour Five Six\nSeven Eight"
        await AssertSelectionAsync(4, 39, "Two Three\nFour Five Six\nSeven Eight");

        // Verify overlay has native selection visible (via getSelection).
        // Multi-line native selection includes extra newlines between block elements.
        var nativeSel = await Page.EvaluateAsync<string>(@"() => window.getSelection()?.toString() || ''");
        Assert.Contains("Two Three", nativeSel);
        Assert.Contains("Four Five Six", nativeSel);
        Assert.Contains("Seven Eight", nativeSel);

        // Bold → Italic → Bold → Italic toggle sequence
        // For multi-line, ToggleInline wraps each non-empty line with markers
        var boldBtn = Page.Locator(".md-btn-bold");
        var italicBtn = Page.Locator(".md-btn-italic");

        // Toggle 1: Bold ON — each line gets ** markers
        await boldBtn.ClickAsync();
        await WaitForOverlayUpdate();
        var raw = await GetRawValue();
        Assert.Contains("**Two Three**", raw);
        Assert.Contains("**Four Five Six**", raw);
        Assert.Contains("**Seven Eight**", raw);

        // Toggle 2: Italic ON — each line gets * markers (around bold content)
        await italicBtn.ClickAsync();
        await WaitForOverlayUpdate();
        raw = await GetRawValue();
        // After italic, each content line has *** markers (combined bold+italic)
        Assert.Contains("***Two Three***", raw);
        Assert.Contains("***Four Five Six***", raw);
        Assert.Contains("***Seven Eight***", raw);

        // Toggle 3: Bold OFF — ** markers removed, * markers remain
        await boldBtn.ClickAsync();
        await WaitForOverlayUpdate();
        raw = await GetRawValue();
        Assert.DoesNotContain("**Two Three**", raw);
        Assert.DoesNotContain("**Four Five Six**", raw);
        Assert.DoesNotContain("**Seven Eight**", raw);
        // Italic markers should remain
        Assert.Contains("*Two Three*", raw);
        Assert.Contains("*Four Five Six*", raw);
        Assert.Contains("*Seven Eight*", raw);

        // Toggle 4: Italic OFF — * markers removed, back to plain
        await italicBtn.ClickAsync();
        await WaitForOverlayUpdate();
        raw = await GetRawValue();
        Assert.DoesNotContain("*Two Three*", raw);
        Assert.DoesNotContain("*Four Five Six*", raw);
        Assert.DoesNotContain("*Seven Eight*", raw);
        // Should be back to original
        Assert.Contains("One Two Three", raw);
        Assert.Contains("Four Five Six", raw);
        Assert.Contains("Seven Eight Nine", raw);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Test 4: Select "Eight" through "Two" end → start (reverse multi-line)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DragSelect_EightThroughTwo_EndToStart_ShouldSelectMultiLine()
    {
        await FillTestContent();

        // Get bounding boxes
        var two = await GetWordBoundsAsync("Two", 0);
        var eight = await GetWordBoundsAsync("Eight", 0);
        Assert.NotNull(two);
        Assert.NotNull(eight);

        // Drag from END of "Eight" to START of "Two" (reverse direction)
        await DragSelectAsync(eight.Right, eight.CenterY, two.X, two.CenterY, steps: 10);

        // Regardless of direction, selection should be normalized
        await AssertSelectionAsync(4, 39, "Two Three\nFour Five Six\nSeven Eight");

        // Same toggle sequence as Test 3
        var boldBtn = Page.Locator(".md-btn-bold");
        var italicBtn = Page.Locator(".md-btn-italic");

        // Toggle 1: Bold ON
        await boldBtn.ClickAsync();
        await WaitForOverlayUpdate();
        var raw = await GetRawValue();
        Assert.Contains("**Two Three**", raw);
        Assert.Contains("**Four Five Six**", raw);
        Assert.Contains("**Seven Eight**", raw);

        // Toggle 2: Italic ON
        await italicBtn.ClickAsync();
        await WaitForOverlayUpdate();
        raw = await GetRawValue();
        Assert.Contains("***Two Three***", raw);
        Assert.Contains("***Four Five Six***", raw);
        Assert.Contains("***Seven Eight***", raw);

        // Toggle 3: Bold OFF
        await boldBtn.ClickAsync();
        await WaitForOverlayUpdate();
        raw = await GetRawValue();
        Assert.DoesNotContain("**Two Three**", raw);
        Assert.Contains("*Two Three*", raw);

        // Toggle 4: Italic OFF
        await italicBtn.ClickAsync();
        await WaitForOverlayUpdate();
        raw = await GetRawValue();
        Assert.DoesNotContain("*Two Three*", raw);
        Assert.DoesNotContain("*Four Five Six*", raw);
        Assert.DoesNotContain("*Seven Eight*", raw);
        Assert.Contains("One Two Three", raw);
        Assert.Contains("Four Five Six", raw);
        Assert.Contains("Seven Eight Nine", raw);
    }

    // ═══════════════════════════════════════════════════════════════
    //  DTOs for JS interop (classes with parameterless constructors
    //  required by Playwright's EvaluateAsync<T> deserializer)
    // ═══════════════════════════════════════════════════════════════

    private class TextBounds
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
    }

    private class TextareaSelection
    {
        public int Start { get; set; }
        public int End { get; set; }
    }
}
