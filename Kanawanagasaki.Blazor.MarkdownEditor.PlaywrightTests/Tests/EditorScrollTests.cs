using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Scroll-synchronisation tests.
///
/// Architecture under test:
///   - Textarea  - transparent, overflow:auto, acts as scroll model.
///   - Overlay   - rendered markdown, overflow-y:auto, scroll position
///                 is derived from textarea.scrollTop via JS mapping.
///   - JS bridge  - textarea 'scroll' event -> syncOverlayFromTextarea()
///                  overlay 'wheel' event  -> handleOverlayWheel()
///
/// Content injection uses fast JS-based FillContentAndWaitForMappings.
/// Scroll sync assertions use WaitForScrollSync instead of Task.Delay.
/// Mouse wheel tests retain real Mouse.WheelAsync to test the wheel handler.
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorScrollTests : EditorTestBase
{
    public EditorScrollTests(TestAppFixture fixture)
        : base(fixture) { }

    // ═══════════════════════════════════════════════════════════════
    //  Content overflow / line rendering
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task LongContent_ShouldRenderCorrectLineCount()
    {
        await NavigateToEditor();

        var content = GenerateLines(40, "Line");
        await FillContentAndWaitForMappings(content, 40);

        var lineCount = await GetOverlayLineCount();
        Assert.Equal(40, lineCount);
    }

    [Fact]
    public async Task LongContent_TextareaShouldBeScrollable()
    {
        await NavigateToEditor();

        var content = GenerateLines(40, "Line");
        await FillContentAndWaitForMappings(content, 40);

        var scrollH = await EvalAsync<int>("() => document.querySelector('.md-textarea').scrollHeight");
        var clientH = await EvalAsync<int>("() => document.querySelector('.md-textarea').clientHeight");

        Assert.True(scrollH > clientH,
            $"Textarea should overflow. scrollHeight={scrollH} vs clientHeight={clientH}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Textarea -> Overlay scroll sync (JS-based scrolling)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TextareaScroll_ToBottom_ShouldSyncOverlayToBottom()
    {
        await NavigateToEditor();

        var content = GenerateLines(30, "Line content here");
        await FillContentAndWaitForMappings(content, 30);

        // Scroll textarea to bottom
        await EvalVoid("() => { const ta = document.querySelector('.md-textarea'); ta.scrollTop = ta.scrollHeight; }");
        await WaitForScrollSync();

        var textareaScroll = await EvalAsync<int>("() => document.querySelector('.md-textarea').scrollTop");
        var overlayScroll  = await EvalAsync<int>("() => document.querySelector('.md-overlay').scrollTop");

        Assert.True(textareaScroll > 0, "Textarea should be scrolled");
        Assert.True(overlayScroll > 0,
            $"Overlay must scroll when textarea scrolls. overlay.scrollTop={overlayScroll}");

        // Both layers should be near their maximum scroll
        var taMax = await EvalAsync<int>("() => document.querySelector('.md-textarea').scrollHeight - document.querySelector('.md-textarea').clientHeight");
        var ovMax = await EvalAsync<int>("() => document.querySelector('.md-overlay').scrollHeight - document.querySelector('.md-overlay').clientHeight");

        if (ovMax > 0)
        {
            var taPct = (double)textareaScroll / taMax;
            var ovPct = (double)overlayScroll  / ovMax;
            // Within 30% tolerance (headings / padding cause non-uniformity)
            Assert.True(Math.Abs(taPct - ovPct) < 0.30,
                $"Scroll positions should be roughly proportional. " +
                $"textarea {taPct:P0} vs overlay {ovPct:P0}");
        }
    }

    [Fact]
    public async Task TextareaScroll_ToMiddle_ShouldSyncOverlayProportionally()
    {
        await NavigateToEditor();

        var content = GenerateLines(30, "Line content here");
        await FillContentAndWaitForMappings(content, 30);

        // Scroll textarea to 50%
        var taScrollH = await EvalAsync<int>("() => document.querySelector('.md-textarea').scrollHeight");
        var taClientH = await EvalAsync<int>("() => document.querySelector('.md-textarea').clientHeight");
        var mid = (taScrollH - taClientH) / 2;
        await EvalVoid($"() => {{ document.querySelector('.md-textarea').scrollTop = {mid}; }}");
        await WaitForScrollSync();

        var textareaScroll = await EvalAsync<int>("() => document.querySelector('.md-textarea').scrollTop");
        var overlayScroll  = await EvalAsync<int>("() => document.querySelector('.md-overlay').scrollTop");

        Assert.True(overlayScroll > 0, "Overlay should have scrolled");

        var ovScrollH = await EvalAsync<int>("() => document.querySelector('.md-overlay').scrollHeight");
        var ovClientH = await EvalAsync<int>("() => document.querySelector('.md-overlay').clientHeight");
        var ovMax = ovScrollH - ovClientH;

        if (ovMax > 0)
        {
            var taPct = (double)textareaScroll / (taScrollH - taClientH);
            var ovPct = (double)overlayScroll  / ovMax;
            Assert.True(Math.Abs(taPct - ovPct) < 0.30,
                $"Middle scroll should be proportional. textarea {taPct:P0} vs overlay {ovPct:P0}");
        }
    }

    [Fact]
    public async Task TextareaScroll_ToTop_ShouldReduceOverlayScroll()
    {
        await NavigateToEditor();

        var content = GenerateLines(30, "Line content here");
        await FillContentAndWaitForMappings(content, 30);

        // Scroll to bottom first
        await EvalVoid("() => { const ta = document.querySelector('.md-textarea'); ta.scrollTop = ta.scrollHeight; }");
        await WaitForScrollSync();
        var bottomOverlayScroll = await EvalAsync<int>("() => document.querySelector('.md-overlay').scrollTop");

        // Scroll to top
        await EvalVoid("() => { document.querySelector('.md-textarea').scrollTop = 0; }");
        await WaitForScrollSync();

        var topOverlayScroll = await EvalAsync<int>("() => document.querySelector('.md-overlay').scrollTop");

        // Overlay should have scrolled significantly less than when at bottom
        Assert.True(topOverlayScroll < bottomOverlayScroll,
            $"Overlay scrollTop at top ({topOverlayScroll}) should be less than at bottom ({bottomOverlayScroll})");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Mouse-wheel on overlay -> textarea scroll (real wheel events)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task MouseWheelOnOverlay_ShouldScrollTextareaAndOverlay()
    {
        await NavigateToEditor();

        var content = GenerateLines(30, "Line content here");
        await FillContentAndWaitForMappings(content, 30);

        var editorBody = Page.Locator(".md-editor-body");
        var box = await editorBody.BoundingBoxAsync();
        Assert.NotNull(box);
        await Page.Mouse.MoveAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);
        await Page.Mouse.WheelAsync(0, 300);
        await WaitForScrollSync();

        var textareaScroll = await EvalAsync<double>("() => document.querySelector('.md-textarea')?.scrollTop || 0");
        var overlayScroll  = await EvalAsync<double>("() => document.querySelector('.md-overlay')?.scrollTop  || 0");

        Assert.True(textareaScroll > 0,
            $"Textarea should scroll after wheel. scrollTop={textareaScroll}");
        Assert.True(overlayScroll > 0,
            $"Overlay should scroll in sync after wheel. scrollTop={overlayScroll}");
    }

    [Fact]
    public async Task MouseWheelOnOverlay_SingleWheel_ShouldScrollTextarea()
    {
        await NavigateToEditor();

        var content = GenerateLines(30, "Line content here");
        await FillContentAndWaitForMappings(content, 30);

        // Reset scroll to top (FillContentAsync may auto-scroll to cursor)
        await EvalVoid("() => { document.querySelector('.md-textarea').scrollTop = 0; }");
        await WaitForScrollSync();

        var scrollBefore = await EvalAsync<int>("() => document.querySelector('.md-textarea').scrollTop");
        Assert.Equal(0, scrollBefore);

        var editorBody = Page.Locator(".md-editor-body");
        var box = await editorBody.BoundingBoxAsync();
        Assert.NotNull(box);
        await Page.Mouse.MoveAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);

        await Page.Mouse.WheelAsync(0, 300);
        await WaitForScrollSync();

        var scrollAfter = await EvalAsync<int>("() => document.querySelector('.md-textarea').scrollTop");
        Assert.True(scrollAfter > scrollBefore,
            $"Textarea scrollTop should increase after wheel. Before={scrollBefore}, After={scrollAfter}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  CSS / layout
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EditorBody_Layout_ShouldBeCorrect()
    {
        await NavigateToEditor();

        var overflow  = await EvalAsync<string>("() => getComputedStyle(document.querySelector('.md-editor-body')).overflow");
        var taPos     = await EvalAsync<string>("() => getComputedStyle(document.querySelector('.md-textarea')).position");
        var ovPos     = await EvalAsync<string>("() => getComputedStyle(document.querySelector('.md-overlay')).position");
        var minHeight = await EvalAsync<double>("() => parseFloat(getComputedStyle(document.querySelector('.md-editor')).minHeight)");

        Assert.Equal("hidden", overflow);
        Assert.Equal("absolute", taPos);
        Assert.Equal("absolute", ovPos);
        Assert.True(minHeight >= 200, $"Editor min-height should be >= 200px. Actual: {minHeight}px");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Line-index data attributes
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderedLines_ShouldHaveDataLineIndex()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("Line 1");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.TypeAsync("Line 2");
        await WaitForOverlayUpdate(minLineCount: 2);

        var count = await EvalAsync<int>(
            "() => document.querySelectorAll('.md-overlay [data-line-index]').length");

        Assert.True(count >= 2, $"Expected >= 2 lines with data-line-index. Actual: {count}");
    }

    [Fact]
    public async Task LineIndexes_ShouldBeSequentialFromZero()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("A");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.TypeAsync("B");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.TypeAsync("C");
        await WaitForOverlayUpdate(minLineCount: 3);

        var indexes = await EvalAsync<int[]>(@"() => {
            const lines = document.querySelectorAll('.md-overlay [data-line-index]');
            return Array.from(lines).map(el => parseInt(el.dataset.lineIndex));
        }");

        Assert.Equal(new[] { 0, 1, 2 }, indexes);
    }
}
