using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Basic interaction tests: focus, typing, click-to-position, cursor visibility,
/// placeholder, and raw value verification.
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorBasicTests : IAsyncLifetime
{
    private readonly TestAppFixture _fixture;
    private IPage _page = null!;

    public EditorBasicTests(TestAppFixture fixture)
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

        // Wait for Blazor WASM to finish loading (the "Loading..." text disappears)
        await _page.WaitForFunctionAsync(
            "() => !document.getElementById('app')?.textContent?.includes('Loading...')",
            new PageWaitForFunctionOptions { Timeout = 60000 });

        // Wait for the editor component to render
        await _page.WaitForFunctionAsync(
            "() => document.querySelector('.md-editor') !== null",
            new PageWaitForFunctionOptions { Timeout = 15000 });
        await _page.WaitForFunctionAsync(
            "() => document.querySelector('.md-textarea') !== null && document.querySelector('.md-overlay') !== null",
            new PageWaitForFunctionOptions { Timeout = 15000 });

        // Wait for the editor body and JS module to initialize
        await _page.WaitForFunctionAsync(
            "() => document.querySelector('.md-textarea') !== null " +
            "&& document.querySelector('.md-editor-body') !== null",
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await Task.Delay(1000);
    }

    /// <summary>
    /// Helper: type text after clicking the overlay (which focuses the textarea).
    /// </summary>
    private async Task ClickOverlayAndType(string text)
    {
        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);
        await _page.Keyboard.TypeAsync(text);
        await Task.Delay(500);
    }

    // ── Focus tests ─────────────────────────────────────────────

    [Fact]
    public async Task ClickOverlay_ShouldFocusTextarea()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        var isFocused = await _page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector('.md-textarea')");

        Assert.True(isFocused, "Textarea should be focused after clicking the overlay");
    }

    [Fact]
    public async Task ClickOverlay_ShouldShowCursor()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(300);

        var cursorExists = await _page.Locator(".md-cursor").CountAsync();
        Assert.True(cursorExists > 0,
            "Simulated cursor element should exist after clicking the overlay");
    }

    // ── Typing tests ────────────────────────────────────────────

    [Fact]
    public async Task TypeText_ShouldUpdateOverlay()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");

        var overlayText = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

        Assert.Contains("Hello", overlayText);
    }

    [Fact]
    public async Task TypeText_ShouldUpdateRawValue()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello World");

        var rawValue = await _page.Locator("#raw-value").InnerTextAsync();

        Assert.Contains("Hello World", rawValue);
    }

    [Fact]
    public async Task TypeMultipleWords_ShouldRenderAll()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello World");

        var overlayText = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

        Assert.Contains("Hello World", overlayText);
    }

    [Fact]
    public async Task TypeNewline_ShouldCreateMultipleLines()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Keyboard.TypeAsync("Line 1");
        await _page.Keyboard.PressAsync("Enter");
        await _page.Keyboard.TypeAsync("Line 2");
        await Task.Delay(500);

        var lineCount = await _page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.md-overlay [data-line-index]').length");

        Assert.True(lineCount >= 2,
            $"Should have at least 2 lines after pressing Enter. Actual: {lineCount}");
    }

    // ── Click-to-position tests ─────────────────────────────────

    [Fact]
    public async Task ClickOnOverlay_WhenTextExists_ShouldMoveCursor()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello World");

        var overlay = _page.Locator(".md-overlay");
        var box = await overlay.BoundingBoxAsync();
        if (box != null)
        {
            await overlay.ClickAsync(new LocatorClickOptions
            {
                Position = new Position { X = box.Width / 4, Y = box.Height / 4 }
            });
        }
        await Task.Delay(300);

        await _page.Keyboard.TypeAsync("X");
        await Task.Delay(500);

        var overlayText = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

        Assert.Contains("X", overlayText);
    }

    [Fact]
    public async Task ClickOnEmptyOverlay_ShouldFocusEditor()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Keyboard.TypeAsync("A");
        await Task.Delay(500);

        var overlayText = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

        Assert.Contains("A", overlayText);
    }

    // ── Placeholder test ────────────────────────────────────────

    [Fact]
    public async Task EmptyEditor_ShouldShowPlaceholder()
    {
        await NavigateToEditor();

        var placeholder = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-textarea')?.getAttribute('placeholder') || ''");

        Assert.Equal("Start typing...", placeholder);
    }

    // ── Editor structure test ───────────────────────────────────

    [Fact]
    public async Task Editor_ShouldHaveExpectedStructure()
    {
        await NavigateToEditor();

        Assert.True(await _page.Locator(".md-editor").CountAsync() > 0, "Editor root (.md-editor) should exist");
        Assert.True(await _page.Locator(".md-toolbar").CountAsync() > 0, "Toolbar (.md-toolbar) should exist");
        Assert.True(await _page.Locator(".md-textarea").CountAsync() > 0, "Textarea (.md-textarea) should exist");
        Assert.True(await _page.Locator(".md-overlay").CountAsync() > 0, "Overlay (.md-overlay) should exist");
        Assert.True(await _page.Locator(".md-cursor").CountAsync() > 0, "Cursor (.md-cursor) should exist");
    }

    [Fact]
    public async Task Toolbar_ShouldHaveAllExpectedButtons()
    {
        await NavigateToEditor();

        var btnCount = await _page.Locator(".md-toolbar .md-btn").CountAsync();
        Assert.True(btnCount >= 16, $"Toolbar should have at least 16 buttons. Actual: {btnCount}");

        Assert.True(await _page.Locator("button[title='Undo (Ctrl+Z)']").CountAsync() > 0, "Undo button should exist");
        Assert.True(await _page.Locator("button[title='Redo (Ctrl+Y)']").CountAsync() > 0, "Redo button should exist");
        Assert.True(await _page.Locator("button[title='Heading 1']").CountAsync() > 0, "H1 button should exist");
        Assert.True(await _page.Locator("button[title='Heading 2']").CountAsync() > 0, "H2 button should exist");
        Assert.True(await _page.Locator("button[title='Heading 3']").CountAsync() > 0, "H3 button should exist");
        Assert.True(await _page.Locator("button[title='Bold (Ctrl+B)']").CountAsync() > 0, "Bold button should exist");
        Assert.True(await _page.Locator("button[title='Italic (Ctrl+I)']").CountAsync() > 0, "Italic button should exist");
        Assert.True(await _page.Locator("button[title='Strikethrough']").CountAsync() > 0, "Strikethrough button should exist");
        Assert.True(await _page.Locator("button[title='Inline Code']").CountAsync() > 0, "Inline Code button should exist");
        Assert.True(await _page.Locator("button[title='Code Block']").CountAsync() > 0, "Code Block button should exist");
        Assert.True(await _page.Locator("button[title='Unordered List']").CountAsync() > 0, "UL button should exist");
        Assert.True(await _page.Locator("button[title='Ordered List']").CountAsync() > 0, "OL button should exist");
        Assert.True(await _page.Locator("button[title='Blockquote']").CountAsync() > 0, "Blockquote button should exist");
        Assert.True(await _page.Locator("button[title='Insert Link']").CountAsync() > 0, "Link button should exist");
        Assert.True(await _page.Locator("button[title='Insert Image']").CountAsync() > 0, "Image button should exist");
        Assert.True(await _page.Locator("button[title='Horizontal Rule']").CountAsync() > 0, "HR button should exist");
    }

    // ── Raw value display test ──────────────────────────────────

    [Fact]
    public async Task RawValueDisplay_ShouldExist()
    {
        await NavigateToEditor();

        Assert.True(await _page.Locator("#raw-value").CountAsync() > 0,
            "Raw value display element (#raw-value) should exist");
    }
}
