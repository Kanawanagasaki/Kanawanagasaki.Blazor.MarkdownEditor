using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Scroll synchronization tests: verifies that the overlay scrolls in
/// sync with the textarea when content overflows, and that mouse wheel
/// events on the overlay propagate correctly.
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorScrollTests : IAsyncLifetime
{
    private readonly TestAppFixture _fixture;
    private IPage _page = null!;

    public EditorScrollTests(TestAppFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _page = await _fixture.CreatePageAsync();
    }

    public async Task DisposeAsync()
    {
        await _page.CloseAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private async Task NavigateToEditor()
    {
        await _page.GotoAsync(_fixture.BaseAddress, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // Wait for Blazor WASM to finish loading
        await _page.WaitForFunctionAsync(
            "() => !document.getElementById('app')?.textContent?.includes('Loading...')",
            new PageWaitForFunctionOptions { Timeout = 60000 });

        await _page.WaitForSelectorAsync(".md-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await _page.WaitForFunctionAsync(
            "() => document.querySelector('.md-textarea') !== null && document.querySelector('.md-overlay') !== null",
            new PageWaitForFunctionOptions { Timeout = 30000 });
        await Task.Delay(1000);
    }

    private async Task ClickOverlayAndType(string text)
    {
        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);
        await _page.Keyboard.TypeAsync(text);
        await Task.Delay(500);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Content overflow and line count
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task LongContent_ShouldRenderManyLines()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        for (int i = 0; i < 30; i++)
        {
            await _page.Keyboard.TypeAsync($"Line {i + 1}");
            await _page.Keyboard.PressAsync("Enter");
        }
        await Task.Delay(500);

        var lineCount = await _page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.md-overlay [data-line-index]').length");

        Assert.True(lineCount >= 30, $"Should have at least 30 lines. Actual: {lineCount}");
    }

    [Fact]
    public async Task LongContent_OverlayShouldScroll()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        for (int i = 0; i < 40; i++)
        {
            await _page.Keyboard.TypeAsync($"Line {i + 1} with some content");
            await _page.Keyboard.PressAsync("Enter");
        }
        await Task.Delay(500);

        var textareaScrollHeight = await _page.EvaluateAsync<int>(
            "() => document.querySelector('.md-textarea')?.scrollHeight || 0");
        var textareaClientHeight = await _page.EvaluateAsync<int>(
            "() => document.querySelector('.md-textarea')?.clientHeight || 0");

        Assert.True(textareaScrollHeight > textareaClientHeight,
            $"Textarea should be scrollable. scrollHeight={textareaScrollHeight}, clientHeight={textareaClientHeight}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scroll synchronization
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TextareaScroll_ShouldSyncToOverlay()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        for (int i = 0; i < 30; i++)
        {
            await _page.Keyboard.TypeAsync($"Line {i + 1} with enough content to wrap");
            await _page.Keyboard.PressAsync("Enter");
        }
        await Task.Delay(500);

        // Scroll the textarea to the bottom using JS
        await _page.EvaluateAsync("() => { const ta = document.querySelector('.md-textarea'); if (ta) ta.scrollTop = ta.scrollHeight; }");
        await Task.Delay(300);

        var overlayScrollTop = await _page.EvaluateAsync<double>(
            "() => document.querySelector('.md-overlay')?.scrollTop || 0");

        Assert.True(overlayScrollTop > 0, $"Overlay should have scrolled. scrollTop={overlayScrollTop}");
    }

    [Fact]
    public async Task MouseWheelOnOverlay_ShouldScrollContent()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        for (int i = 0; i < 30; i++)
        {
            await _page.Keyboard.TypeAsync($"Line {i + 1} with enough content to wrap");
            await _page.Keyboard.PressAsync("Enter");
        }
        await Task.Delay(500);

        var editorBody = _page.Locator(".md-editor-body");
        var box = await editorBody.BoundingBoxAsync();
        if (box != null)
        {
            await _page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
            await Task.Delay(100);
            await _page.Mouse.WheelAsync(0, 300);
            await Task.Delay(300);
        }

        var textareaScrollTop = await _page.EvaluateAsync<double>(
            "() => document.querySelector('.md-textarea')?.scrollTop || 0");

        Assert.True(textareaScrollTop > 0, $"Textarea should have scrolled after wheel event. scrollTop={textareaScrollTop}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Editor body height and overflow
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EditorBody_ShouldHaveOverflowHidden()
    {
        await NavigateToEditor();

        var overflow = await _page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.md-editor-body')).overflow");

        Assert.Equal("hidden", overflow);
    }

    [Fact]
    public async Task Textarea_ShouldBeAbsolutelyPositioned()
    {
        await NavigateToEditor();

        var position = await _page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.md-textarea')).position");

        Assert.Equal("absolute", position);
    }

    [Fact]
    public async Task Overlay_ShouldBeAbsolutelyPositioned()
    {
        await NavigateToEditor();

        var position = await _page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.md-overlay')).position");

        Assert.Equal("absolute", position);
    }

    [Fact]
    public async Task Editor_ShouldHaveMinHeight()
    {
        await NavigateToEditor();

        var minHeight = await _page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.querySelector('.md-editor')).minHeight)");

        Assert.True(minHeight >= 200, $"Editor should have a reasonable min-height. Actual: {minHeight}px");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Line index attributes
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderedLines_ShouldHaveDataLineIndex()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Line 1");
        await _page.Keyboard.PressAsync("Enter");
        await _page.Keyboard.TypeAsync("Line 2");
        await Task.Delay(500);

        var linesWithIndex = await _page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.md-overlay [data-line-index]').length");

        Assert.True(linesWithIndex >= 2, $"Should have at least 2 lines with data-line-index. Actual: {linesWithIndex}");
    }

    [Fact]
    public async Task LineIndexes_ShouldBeSequential()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Keyboard.TypeAsync("A");
        await _page.Keyboard.PressAsync("Enter");
        await _page.Keyboard.TypeAsync("B");
        await _page.Keyboard.PressAsync("Enter");
        await _page.Keyboard.TypeAsync("C");
        await Task.Delay(500);

        var indexes = await _page.EvaluateAsync<int[]>(
            @"() => {
                const lines = document.querySelectorAll('.md-overlay [data-line-index]');
                return Array.from(lines).map(el => parseInt(el.dataset.lineIndex));
            }");

        Assert.Equal(new[] { 0, 1, 2 }, indexes);
    }
}
