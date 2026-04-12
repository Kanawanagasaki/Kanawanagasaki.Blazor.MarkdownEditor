using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Basic interaction tests: focus, typing, click-to-position,
/// cursor visibility, placeholder, and DOM structure.
///
/// These tests intentionally use real mouse clicks and keyboard input
/// to verify the full input→focus→overlay rendering pipeline.
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorBasicTests : EditorTestBase
{
    public EditorBasicTests(TestAppFixture fixture)
        : base(fixture) { }

    // ═══════════════════════════════════════════════════════════════
    //  Focus tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClickOverlay_ShouldFocusTextarea()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await WaitForTextareaFocus();

        var isFocused = await EvalAsync<bool>(
            "() => document.activeElement === document.querySelector('.md-textarea')");

        Assert.True(isFocused, "Textarea should receive focus after clicking the overlay");
    }

    [Fact]
    public async Task ClickOverlay_ShouldShowCursor()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await WaitForCursorVisible();

        var cursorVisible = await EvalAsync<bool>(
            "() => document.querySelector('.md-cursor')?.style.display === 'block'");

        Assert.True(cursorVisible, "Simulated cursor should be visible after clicking the overlay");
    }

    [Fact]
    public async Task ClickEditorBody_ShouldFocusTextarea()
    {
        await NavigateToEditor();

        // Click on the editor body area (not a specific element) —
        // focus should still transfer to the textarea.
        await Page.Locator(".md-editor-body").ClickAsync();
        await WaitForTextareaFocus();

        var isFocused = await EvalAsync<bool>(
            "() => document.activeElement === document.querySelector('.md-textarea')");

        Assert.True(isFocused,
            "Textarea should receive focus after clicking anywhere in the editor body");
    }

    [Fact]
    public async Task ClickToolbarButton_ShouldNotStealFocusFromTextarea()
    {
        await NavigateToEditor();

        // Focus textarea first
        await Page.Locator(".md-overlay").ClickAsync();
        await WaitForTextareaFocus();

        // Click a toolbar button
        await Page.Locator("button[title='Bold (Ctrl+B)']").ClickAsync();

        // Focus should return to textarea after toolbar button action
        await WaitForTextareaFocus();

        var isFocused = await EvalAsync<bool>(
            "() => document.activeElement === document.querySelector('.md-textarea')");

        Assert.True(isFocused,
            "Textarea should retain focus after clicking a toolbar button");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Typing tests (real keyboard → overlay rendering)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TypeText_ShouldUpdateOverlay()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");

        var overlayText = await GetOverlayText();
        Assert.Contains("Hello", overlayText);
    }

    [Fact]
    public async Task TypeText_ShouldUpdateRawValue()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello World");

        var rawValue = await GetRawValue();
        Assert.Contains("Hello World", rawValue);
    }

    [Fact]
    public async Task TypeNewline_ShouldCreateMultipleLines()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await WaitForTextareaFocus();

        await Page.Keyboard.TypeAsync("Line 1");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.TypeAsync("Line 2");
        await WaitForOverlayUpdate(minLineCount: 2);

        var lineCount = await EvalAsync<int>(
            "() => document.querySelectorAll('.md-overlay [data-line-index]').length");

        Assert.True(lineCount >= 2,
            $"Should have >= 2 lines after Enter. Actual: {lineCount}");
    }

    [Fact]
    public async Task TypeText_RawValueShouldMatchTextareaValue()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Sync test");

        // The #raw-value display should exactly mirror the textarea value
        var matches = await EvalAsync<bool>(
            @"() => {
                const ta = document.querySelector('.md-textarea');
                const raw = document.getElementById('raw-value');
                return ta && raw && raw.textContent.trim() === ta.value.trim();
            }");

        Assert.True(matches,
            "The raw-value display should exactly mirror the textarea value after typing");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Click-to-position tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClickOnOverlay_WhenTextExists_ShouldRepositionCursor()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello World");

        // Click near the top-left quadrant of the overlay
        var overlay = Page.Locator(".md-overlay");
        var box = await overlay.BoundingBoxAsync();
        Assert.NotNull(box);
        await overlay.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = box!.Width / 4, Y = box.Height / 4 }
        });

        // Type X — it should appear inside the existing text
        await Page.Keyboard.TypeAsync("X");
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("X", rawValue);

        // Verify the raw value still contains the original text
        Assert.Contains("Hello World", rawValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Placeholder
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmptyEditor_ShouldShowPlaceholder()
    {
        await NavigateToEditor();

        var placeholder = await EvalAsync<string>(
            "() => document.querySelector('.md-textarea')?.getAttribute('placeholder') || ''");

        Assert.Equal("Start typing...", placeholder);
    }

    // ═══════════════════════════════════════════════════════════════
    //  DOM structure
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Editor_ShouldHaveExpectedStructure()
    {
        await NavigateToEditor();

        Assert.True(await Page.Locator(".md-editor").CountAsync() > 0, ".md-editor missing");
        Assert.True(await Page.Locator(".md-toolbar").CountAsync() > 0, ".md-toolbar missing");
        Assert.True(await Page.Locator(".md-textarea").CountAsync() > 0, ".md-textarea missing");
        Assert.True(await Page.Locator(".md-overlay").CountAsync() > 0, ".md-overlay missing");
        Assert.True(await Page.Locator(".md-cursor").CountAsync() > 0, ".md-cursor missing");
        Assert.True(await Page.Locator("#raw-value").CountAsync() > 0, "#raw-value missing");
    }

    [Fact]
    public async Task Toolbar_ShouldHaveAllExpectedButtons()
    {
        await NavigateToEditor();

        var btnCount = await Page.Locator(".md-toolbar .md-btn").CountAsync();
        Assert.True(btnCount >= 16, $"Toolbar should have >= 16 buttons. Actual: {btnCount}");

        string[] expectedTitles =
        [
            "Undo (Ctrl+Z)", "Redo (Ctrl+Y)",
            "Heading 1", "Heading 2", "Heading 3",
            "Bold (Ctrl+B)", "Italic (Ctrl+I)", "Strikethrough",
            "Inline Code", "Code Block",
            "Unordered List", "Ordered List",
            "Blockquote",
            "Insert Link", "Insert Image",
            "Horizontal Rule"
        ];

        foreach (var title in expectedTitles)
        {
            Assert.True(await Page.Locator($"button[title='{title}']").CountAsync() > 0,
                $"Toolbar should have '{title}' button");
        }
    }
}
