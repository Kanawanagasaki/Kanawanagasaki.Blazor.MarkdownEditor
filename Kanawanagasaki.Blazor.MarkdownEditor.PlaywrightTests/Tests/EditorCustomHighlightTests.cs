using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Tests for the custom div-based selection highlight that replaces the
/// browser's native ::selection background.  Every test selects text on
/// the overlay (via real mouse drag) and then verifies that the correct
/// number of <c>.md-selection-line</c> divs exist and that their
/// positions match the native Selection rects (the ground-truth geometry).
///
/// Scenarios covered (each in both directions):
///   1. One line, edge-to-edge          (full-line selection)
///   2. One line, middle                (partial-line selection)
///   3. Two lines, edge-to-edge         (two full lines)
///   4. Two lines, center-to-center     (partial first & last line)
///   5. Three lines, edge-to-edge       (all three full lines)
///   6. Three lines, center-to-center   (partial first, full middle, partial last)
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorCustomHighlightTests : EditorTestBase
{
    public EditorCustomHighlightTests(TestAppFixture fixture)
        : base(fixture) { }

    private const string TestContent =
        "One Two Three\nFour Five Six\nSeven Eight Nine";

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task FillTestContent()
    {
        await NavigateToEditor();
        await FillContentAndWaitForMappings(TestContent, 3);
    }

    /// <summary>
    /// Returns the bounding box of the n-th occurrence of <paramref name="word"/>
    /// in the overlay's rendered text.
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
                            x: rect.x, y: rect.y,
                            width: rect.width, height: rect.height,
                            right: rect.right, bottom: rect.bottom,
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
    /// Returns the bounding box of a line element identified by its
    /// <c>data-line-index</c> attribute.
    /// </summary>
    private async Task<TextBounds> GetLineBoundsAsync(int lineIndex)
    {
        return await Page.EvaluateAsync<TextBounds>(
            $"() => {{ const el = document.querySelector('.md-overlay [data-line-index=\"{lineIndex}\"]'); if (!el) return null; const r = el.getBoundingClientRect(); return {{ x: r.x, y: r.y, width: r.width, height: r.height, right: r.right, bottom: r.bottom, centerX: r.x + r.width / 2, centerY: r.y + r.height / 2 }}; }}");
    }

    /// <summary>
    /// Performs a mouse drag selection from one point to another on the
    /// overlay with intermediate moves for smooth dragging.
    /// </summary>
    private async Task DragSelectAsync(double startX, double startY, double endX, double endY, int steps = 5)
    {
        await Page.Mouse.MoveAsync((float)startX, (float)startY);
        await Page.Mouse.DownAsync();

        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double x = startX + (endX - startX) * t;
            double y = startY + (endY - startY) * t;
            await Page.Mouse.MoveAsync((float)x, (float)y);
        }

        await Page.Mouse.MoveAsync((float)endX, (float)endY);
        await Page.Mouse.UpAsync();
    }

    /// <summary>
    /// Retrieves both the custom highlight div positions and the merged
    /// native Selection rects, returned as a <see cref="SelectionComparison"/>
    /// for easy assertion.
    /// </summary>
    private async Task<SelectionComparison> GetSelectionComparisonAsync()
    {
        return await Page.EvaluateAsync<SelectionComparison>(@"() => {
            const sel = window.getSelection();
            if (!sel || sel.rangeCount === 0 || sel.isCollapsed) {
                return { highlights: [], nativeRects: [] };
            }

            const range = sel.getRangeAt(0);
            const overlay = document.querySelector('.md-overlay');
            if (!overlay.contains(range.commonAncestorContainer)) {
                return { highlights: [], nativeRects: [] };
            }

            // Highlight div positions
            const bodyRect = document.querySelector('.md-editor-body').getBoundingClientRect();
            const hlDivs = document.querySelectorAll('.md-selection-line');
            const highlights = Array.from(hlDivs).map(d => ({
                top:    parseFloat(d.style.top)    || 0,
                left:   parseFloat(d.style.left)   || 0,
                width:  parseFloat(d.style.width)  || 0,
                height: parseFloat(d.style.height) || 0
            }));

            // Merge native Selection rects (same logic as JS)
            const raw = Array.from(range.getClientRects())
                .filter(r => r.width > 0 && r.height > 0);
            raw.sort((a, b) => a.top - b.top || a.left - b.left);

            const merged = raw.length > 0
                ? [{ top: raw[0].top, left: raw[0].left, right: raw[0].right, bottom: raw[0].bottom }]
                : [];
            for (let i = 1; i < raw.length; i++) {
                const prev = merged[merged.length - 1];
                const curr = raw[i];
                if (curr.top < prev.bottom + 1) {
                    prev.left   = Math.min(prev.left,   curr.left);
                    prev.right  = Math.max(prev.right,  curr.right);
                    prev.top    = Math.min(prev.top,     curr.top);
                    prev.bottom = Math.max(prev.bottom,  curr.bottom);
                } else {
                    merged.push({ top: curr.top, left: curr.left, right: curr.right, bottom: curr.bottom });
                }
            }

            const nativeRects = merged.map(r => ({
                top:    r.top    - bodyRect.top,
                left:   r.left   - bodyRect.left,
                width:  r.right  - r.left,
                height: r.bottom - r.top
            }));

            return { highlights, nativeRects };
        }");
    }

    /// <summary>
    /// Asserts that the custom highlight divs match the native Selection
    /// geometry within a pixel tolerance.
    /// </summary>
    private static void AssertHighlightsMatchNative(
        SelectionComparison comparison, int expectedCount, double tolerance = 3.0)
    {
        Assert.NotNull(comparison.Highlights);
        Assert.NotNull(comparison.NativeRects);
        Assert.Equal(expectedCount, comparison.Highlights!.Length);
        Assert.Equal(expectedCount, comparison.NativeRects!.Length);

        for (int i = 0; i < comparison.Highlights.Length; i++)
        {
            var hl = comparison.Highlights[i];
            var nr = comparison.NativeRects[i];

            Assert.InRange(hl.Top,    nr.Top    - tolerance, nr.Top    + tolerance);
            Assert.InRange(hl.Left,   nr.Left   - tolerance, nr.Left   + tolerance);
            Assert.InRange(hl.Width,  nr.Width  - tolerance, nr.Width  + tolerance);
            Assert.InRange(hl.Height, nr.Height - tolerance, nr.Height + tolerance);
        }
    }

    /// <summary>
    /// Waits until at least one <c>.md-selection-line</c> div exists in the
    /// selection container (i.e. the custom highlight has been rendered).
    /// </summary>
    private async Task WaitForCustomHighlight(int expectedCount = 1)
    {
        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('.md-selection-line').length >= {expectedCount}",
            new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    // ═══════════════════════════════════════════════════════════════
    //  1. One line, edge-to-edge
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Highlight_OneLine_EdgeToEdge_LeftToRight()
    {
        await FillTestContent();

        var line1 = await GetLineBoundsAsync(1);
        Assert.NotNull(line1);

        // Drag from left edge of line 1 to right edge of line 1
        await DragSelectAsync(line1.X + 2, line1.CenterY, line1.Right - 2, line1.CenterY);
        await WaitForCustomHighlight();

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 1);
    }

    [Fact]
    public async Task Highlight_OneLine_EdgeToEdge_RightToLeft()
    {
        await FillTestContent();

        var line1 = await GetLineBoundsAsync(1);
        Assert.NotNull(line1);

        // Drag from right edge of line 1 to left edge of line 1 (reverse)
        await DragSelectAsync(line1.Right - 2, line1.CenterY, line1.X + 2, line1.CenterY);
        await WaitForCustomHighlight();

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 1);
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. One line, middle
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Highlight_OneLine_Middle_LeftToRight()
    {
        await FillTestContent();

        var five = await GetWordBoundsAsync("Five", 0);
        Assert.NotNull(five);

        // Drag from left of "Five" to right of "Five"
        await DragSelectAsync(five.X, five.CenterY, five.Right, five.CenterY);
        await WaitForCustomHighlight();

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 1);

        // The highlight should NOT span the full line width — it should
        // be narrower than the line itself.
        var line1 = await GetLineBoundsAsync(1);
        Assert.NotNull(line1);
        Assert.True(comparison.Highlights[0].Width < line1.Width - 4,
            "Middle-of-line highlight should be narrower than the full line");
    }

    [Fact]
    public async Task Highlight_OneLine_Middle_RightToLeft()
    {
        await FillTestContent();

        var five = await GetWordBoundsAsync("Five", 0);
        Assert.NotNull(five);

        // Drag from right of "Five" to left of "Five" (reverse)
        await DragSelectAsync(five.Right, five.CenterY, five.X, five.CenterY);
        await WaitForCustomHighlight();

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 1);

        var line1 = await GetLineBoundsAsync(1);
        Assert.NotNull(line1);
        Assert.True(comparison.Highlights[0].Width < line1.Width - 4,
            "Middle-of-line highlight should be narrower than the full line");
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. Two lines, edge-to-edge
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Highlight_TwoLines_EdgeToEdge_TopToBottom()
    {
        await FillTestContent();

        var line0 = await GetLineBoundsAsync(0);
        var line1 = await GetLineBoundsAsync(1);
        Assert.NotNull(line0);
        Assert.NotNull(line1);

        // Drag from left edge of line 0 to right edge of line 1
        await DragSelectAsync(line0.X + 2, line0.CenterY, line1.Right - 2, line1.CenterY, steps: 10);
        await WaitForCustomHighlight(2);

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 2);
    }

    [Fact]
    public async Task Highlight_TwoLines_EdgeToEdge_BottomToTop()
    {
        await FillTestContent();

        var line0 = await GetLineBoundsAsync(0);
        var line1 = await GetLineBoundsAsync(1);
        Assert.NotNull(line0);
        Assert.NotNull(line1);

        // Drag from right edge of line 1 to left edge of line 0 (reverse)
        await DragSelectAsync(line1.Right - 2, line1.CenterY, line0.X + 2, line0.CenterY, steps: 10);
        await WaitForCustomHighlight(2);

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 2);
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. Two lines, center-to-center
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Highlight_TwoLines_CenterToCenter_TopToBottom()
    {
        await FillTestContent();

        var two = await GetWordBoundsAsync("Two", 0);
        var five = await GetWordBoundsAsync("Five", 0);
        Assert.NotNull(two);
        Assert.NotNull(five);

        // Drag from center of "Two" (line 0) to center of "Five" (line 1)
        await DragSelectAsync(two.CenterX, two.CenterY, five.CenterX, five.CenterY, steps: 10);
        await WaitForCustomHighlight(2);

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 2);

        // The first highlight should not start at the left edge of the line
        // and the last highlight should not end at the right edge.
        var line0 = await GetLineBoundsAsync(0);
        Assert.NotNull(line0);
        Assert.True(comparison.Highlights[0].Left > line0.X + 2,
            "Center-to-center first highlight should not start at line left edge");
    }

    [Fact]
    public async Task Highlight_TwoLines_CenterToCenter_BottomToTop()
    {
        await FillTestContent();

        var two = await GetWordBoundsAsync("Two", 0);
        var five = await GetWordBoundsAsync("Five", 0);
        Assert.NotNull(two);
        Assert.NotNull(five);

        // Drag from center of "Five" (line 1) to center of "Two" (line 0) (reverse)
        await DragSelectAsync(five.CenterX, five.CenterY, two.CenterX, two.CenterY, steps: 10);
        await WaitForCustomHighlight(2);

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 2);

        var line0 = await GetLineBoundsAsync(0);
        Assert.NotNull(line0);
        Assert.True(comparison.Highlights[0].Left > line0.X + 2,
            "Center-to-center first highlight should not start at line left edge");
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. Three lines, edge-to-edge
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Highlight_ThreeLines_EdgeToEdge_TopToBottom()
    {
        await FillTestContent();

        var line0 = await GetLineBoundsAsync(0);
        var line2 = await GetLineBoundsAsync(2);
        Assert.NotNull(line0);
        Assert.NotNull(line2);

        // Drag from left edge of line 0 to right edge of line 2
        await DragSelectAsync(line0.X + 2, line0.CenterY, line2.Right - 2, line2.CenterY, steps: 15);
        await WaitForCustomHighlight(3);

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 3);
    }

    [Fact]
    public async Task Highlight_ThreeLines_EdgeToEdge_BottomToTop()
    {
        await FillTestContent();

        var line0 = await GetLineBoundsAsync(0);
        var line2 = await GetLineBoundsAsync(2);
        Assert.NotNull(line0);
        Assert.NotNull(line2);

        // Drag from right edge of line 2 to left edge of line 0 (reverse)
        await DragSelectAsync(line2.Right - 2, line2.CenterY, line0.X + 2, line0.CenterY, steps: 15);
        await WaitForCustomHighlight(3);

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 3);
    }

    // ═══════════════════════════════════════════════════════════════
    //  6. Three lines, center-to-center
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Highlight_ThreeLines_CenterToCenter_TopToBottom()
    {
        await FillTestContent();

        var two = await GetWordBoundsAsync("Two", 0);
        var eight = await GetWordBoundsAsync("Eight", 0);
        Assert.NotNull(two);
        Assert.NotNull(eight);

        // Drag from center of "Two" (line 0) to center of "Eight" (line 2)
        await DragSelectAsync(two.CenterX, two.CenterY, eight.CenterX, eight.CenterY, steps: 15);
        await WaitForCustomHighlight(3);

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 3);

        // First highlight should not start at the left edge
        // Last highlight should not end at the right edge
        var line0 = await GetLineBoundsAsync(0);
        var line2 = await GetLineBoundsAsync(2);
        Assert.NotNull(line0);
        Assert.NotNull(line2);
        Assert.True(comparison.Highlights[0].Left > line0.X + 2,
            "Center-to-center first highlight should not start at line left edge");
        Assert.True(comparison.Highlights[2].Left + comparison.Highlights[2].Width < line2.Right - line0.X - 2,
            "Center-to-center last highlight should not end at line right edge");
    }

    [Fact]
    public async Task Highlight_ThreeLines_CenterToCenter_BottomToTop()
    {
        await FillTestContent();

        var two = await GetWordBoundsAsync("Two", 0);
        var eight = await GetWordBoundsAsync("Eight", 0);
        Assert.NotNull(two);
        Assert.NotNull(eight);

        // Drag from center of "Eight" (line 2) to center of "Two" (line 0) (reverse)
        await DragSelectAsync(eight.CenterX, eight.CenterY, two.CenterX, two.CenterY, steps: 15);
        await WaitForCustomHighlight(3);

        var comparison = await GetSelectionComparisonAsync();
        AssertHighlightsMatchNative(comparison, expectedCount: 3);

        var line0 = await GetLineBoundsAsync(0);
        var line2 = await GetLineBoundsAsync(2);
        Assert.NotNull(line0);
        Assert.NotNull(line2);
        Assert.True(comparison.Highlights[0].Left > line0.X + 2,
            "Center-to-center first highlight should not start at line left edge");
        Assert.True(comparison.Highlights[2].Left + comparison.Highlights[2].Width < line2.Right - line0.X - 2,
            "Center-to-center last highlight should not end at line right edge");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Additional: verify native ::selection is hidden
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Highlight_NativeSelectionStyle_ShouldBeTransparent()
    {
        await FillTestContent();

        // Verify that the ::selection override style was injected
        var styleContent = await Page.EvaluateAsync<string>(
            "() => { const s = document.getElementById('md-overlay-selection-style'); return s ? s.textContent : ''; }");
        Assert.Contains("transparent", styleContent);
    }

    [Fact]
    public async Task Highlight_SelectionCleared_OnBlur()
    {
        await FillTestContent();

        var five = await GetWordBoundsAsync("Five", 0);
        Assert.NotNull(five);

        // Select "Five"
        await DragSelectAsync(five.X, five.CenterY, five.Right, five.CenterY);
        await WaitForCustomHighlight();

        // Verify highlight exists
        var countBefore = await Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.md-selection-line').length");
        Assert.True(countBefore > 0, "Highlight divs should exist after selection");

        // Click outside the editor to blur
        await Page.Locator("h1").ClickAsync();
        await Page.WaitForTimeoutAsync(200);

        // Verify highlight divs are cleared
        var countAfter = await Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.md-selection-line').length");
        Assert.Equal(0, countAfter);
    }

    // ═══════════════════════════════════════════════════════════════
    //  DTOs
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

    private class HighlightRect
    {
        public double Top { get; set; }
        public double Left { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private class SelectionComparison
    {
        public HighlightRect[]? Highlights { get; set; }
        public HighlightRect[]? NativeRects { get; set; }
    }
}
