using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Keyboard shortcut tests: Ctrl+B, Ctrl+I, Tab handling, and
/// keyboard-based selection/manipulation.
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorShortcutTests : IAsyncLifetime
{
    private readonly TestAppFixture _fixture;
    private IPage _page = null!;

    public EditorShortcutTests(TestAppFixture fixture)
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

    private async Task SelectAll()
    {
        // Re-focus textarea via overlay click first
        await _page.Locator(".md-overlay").ClickAsync();
        await Task.Delay(100);
        await _page.Keyboard.PressAsync("Control+a");
        await Task.Delay(200);
    }

    private async Task<string> GetRawValue()
    {
        return await _page.Locator("#raw-value").InnerTextAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Ctrl+B (Bold shortcut)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CtrlB_ShouldToggleBold()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("test");
        await SelectAll();

        await _page.Keyboard.PressAsync("Control+b");
        await Task.Delay(500);

        var hasBold = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay strong') !== null");

        Assert.True(hasBold, "Ctrl+B should toggle bold on selected text");
    }

    [Fact]
    public async Task CtrlB_ShouldWrapMarkdown()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("bold");
        await SelectAll();

        await _page.Keyboard.PressAsync("Control+b");
        await Task.Delay(500);

        var rawValue = await GetRawValue();
        Assert.Contains("**bold**", rawValue);
    }

    [Fact]
    public async Task CtrlB_ShouldToggleOffWhenAlreadyBold()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("text");
        await SelectAll();

        await _page.Keyboard.PressAsync("Control+b");
        await Task.Delay(300);

        await _page.Keyboard.PressAsync("Control+b");
        await Task.Delay(500);

        var hasBold = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay strong') !== null");

        Assert.False(hasBold, "Ctrl+B should toggle bold off when already applied");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Ctrl+I (Italic shortcut)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CtrlI_ShouldToggleItalic()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("test");
        await SelectAll();

        await _page.Keyboard.PressAsync("Control+i");
        await Task.Delay(500);

        var hasItalic = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay em') !== null");

        Assert.True(hasItalic, "Ctrl+I should toggle italic on selected text");
    }

    [Fact]
    public async Task CtrlI_ShouldWrapMarkdown()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("italic");
        await SelectAll();

        await _page.Keyboard.PressAsync("Control+i");
        await Task.Delay(500);

        var rawValue = await GetRawValue();
        Assert.Contains("*italic*", rawValue);
    }

    [Fact]
    public async Task CtrlI_ShouldToggleOffWhenAlreadyItalic()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("text");
        await SelectAll();

        await _page.Keyboard.PressAsync("Control+i");
        await Task.Delay(300);

        await _page.Keyboard.PressAsync("Control+i");
        await Task.Delay(500);

        var hasItalic = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay em') !== null");

        Assert.False(hasItalic, "Ctrl+I should toggle italic off when already applied");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Ctrl+Z (Undo shortcut)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CtrlZ_ShouldUndo()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");
        await Task.Delay(200);

        await _page.Keyboard.PressAsync("Control+z");
        await Task.Delay(500);

        var overlayText = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

        Assert.DoesNotContain("Hello", overlayText);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Tab handling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Tab_ShouldInsertSpacesNotChangeFocus()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Keyboard.PressAsync("Tab");
        await Task.Delay(500);

        var isTextareaFocused = await _page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector('.md-textarea')");

        Assert.True(isTextareaFocused, "Textarea should still be focused after pressing Tab");

        var rawValue = await GetRawValue();
        Assert.Contains("  ", rawValue);
    }

    [Fact]
    public async Task Tab_ShouldInsertTwoSpaces()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");
        await Task.Delay(200);

        await _page.Keyboard.PressAsync("Tab");
        await Task.Delay(500);

        var rawValue = await GetRawValue();
        Assert.Equal("Hello  ", rawValue.TrimEnd('\n', '\r'));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Home/End key navigation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task HomeAndEnd_ShouldNavigateWithinEditor()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello World");
        await Task.Delay(200);

        await _page.Keyboard.PressAsync("End");
        await Task.Delay(100);
        await _page.Keyboard.TypeAsync("!");
        await Task.Delay(500);

        var rawValue = await GetRawValue();
        Assert.EndsWith("!", rawValue.TrimEnd('\n', '\r'));
    }

    [Fact]
    public async Task ArrowKeys_ShouldNavigate()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("ABCD");
        await Task.Delay(200);

        await _page.Keyboard.PressAsync("ArrowLeft");
        await Task.Delay(50);
        await _page.Keyboard.PressAsync("ArrowLeft");
        await Task.Delay(50);

        await _page.Keyboard.TypeAsync("X");
        await Task.Delay(500);

        var rawValue = await GetRawValue();
        Assert.Contains("ABXCD", rawValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Combined shortcuts
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CtrlB_Then_CtrlI_ShouldApplyBoth()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("text");
        await SelectAll();

        await _page.Keyboard.PressAsync("Control+b");
        await Task.Delay(300);

        await _page.Keyboard.PressAsync("Control+i");
        await Task.Delay(500);

        var hasBold = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay strong') !== null");
        var hasItalic = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay em') !== null");

        Assert.True(hasBold, "Bold should be applied");
        Assert.True(hasItalic, "Italic should be applied");
    }

    [Fact]
    public async Task Backspace_ShouldDeleteCharacters()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");
        await Task.Delay(200);

        await _page.Keyboard.PressAsync("Backspace");
        await Task.Delay(50);
        await _page.Keyboard.PressAsync("Backspace");
        await Task.Delay(50);
        await _page.Keyboard.PressAsync("Backspace");
        await Task.Delay(500);

        var overlayText = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

        Assert.Contains("He", overlayText);
    }

    [Fact]
    public async Task Delete_ShouldDeleteForwardCharacters()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");
        await Task.Delay(200);

        await _page.Keyboard.PressAsync("Home");
        await Task.Delay(100);

        await _page.Keyboard.PressAsync("Delete");
        await Task.Delay(50);
        await _page.Keyboard.PressAsync("Delete");
        await Task.Delay(50);
        await _page.Keyboard.PressAsync("Delete");
        await Task.Delay(500);

        var overlayText = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

        Assert.Contains("lo", overlayText);
    }
}
