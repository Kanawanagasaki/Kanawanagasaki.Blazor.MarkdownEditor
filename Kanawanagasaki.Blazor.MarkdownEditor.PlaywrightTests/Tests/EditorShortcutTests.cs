using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Keyboard shortcut tests: Ctrl+B, Ctrl+I, Ctrl+Z, Tab,
/// arrow keys, Home/End, Backspace, Delete.
///
/// These tests intentionally use real keyboard input to exercise
/// the actual keydown/keyup → JS interop → Blazor → overlay pipeline.
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorShortcutTests : EditorTestBase
{
    public EditorShortcutTests(TestAppFixture fixture)
        : base(fixture) { }

    // ═══════════════════════════════════════════════════════════════
    //  Ctrl+B  (Bold)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CtrlB_ShouldToggleBold()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("test");
        await SelectAll();

        await Page.Keyboard.PressAsync("Control+b");
        await WaitForOverlayUpdate();

        var hasBold = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay strong') !== null");
        Assert.True(hasBold, "Ctrl+B should produce <strong>");
    }

    [Fact]
    public async Task CtrlB_ShouldWrapMarkdown()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("bold");
        await SelectAll();

        await Page.Keyboard.PressAsync("Control+b");
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("**bold**", rawValue);
    }

    [Fact]
    public async Task CtrlB_ShouldToggleOffWhenAlreadyBold()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("text");
        await SelectAll();

        await Page.Keyboard.PressAsync("Control+b");
        await WaitForOverlayUpdate();

        // Verify bold is on
        var rawOn = await GetRawValue();
        Assert.Contains("**", rawOn);

        // Toggle off (same selection, just press Ctrl+B again)
        await Page.Keyboard.PressAsync("Control+b");
        await WaitForOverlayUpdate();

        var hasBold = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay strong') !== null");
        Assert.False(hasBold, "Ctrl+B should remove bold on second press");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Ctrl+I  (Italic)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CtrlI_ShouldToggleItalic()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("test");
        await SelectAll();

        await Page.Keyboard.PressAsync("Control+i");
        await WaitForOverlayUpdate();

        var hasItalic = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay em') !== null");
        Assert.True(hasItalic, "Ctrl+I should produce <em>");
    }

    [Fact]
    public async Task CtrlI_ShouldWrapMarkdown()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("italic");
        await SelectAll();

        await Page.Keyboard.PressAsync("Control+i");
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("*italic*", rawValue);
    }

    [Fact]
    public async Task CtrlI_ShouldToggleOffWhenAlreadyItalic()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("text");
        await SelectAll();

        await Page.Keyboard.PressAsync("Control+i");
        await WaitForOverlayUpdate();

        // Toggle off (same selection, just press Ctrl+I again)
        await Page.Keyboard.PressAsync("Control+i");
        await WaitForOverlayUpdate();

        var hasItalic = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay em') !== null");
        Assert.False(hasItalic, "Ctrl+I should remove italic on second press");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Ctrl+Z  (Undo)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CtrlZ_ShouldUndo()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");
        await Page.Keyboard.PressAsync("Control+z");
        await WaitForOverlayUpdate();

        var overlayText = await GetOverlayText();
        Assert.DoesNotContain("Hello", overlayText);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Tab handling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Tab_ShouldInsertSpacesNotChangeFocus()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await Page.Keyboard.PressAsync("Tab");
        await WaitForTextareaFocus();

        var isFocused = await EvalAsync<bool>(
            "() => document.activeElement === document.querySelector('.md-textarea')");
        Assert.True(isFocused, "Focus must stay on textarea after Tab");

        var rawValue = await GetRawValue();
        Assert.Contains("  ", rawValue);
    }

    [Fact]
    public async Task Tab_ShouldInsertTwoSpaces()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("Hello");

        await Page.Keyboard.PressAsync("Tab");
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Equal("Hello  ", rawValue.TrimEnd('\n', '\r'));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Home / End
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EndKey_ShouldMoveCursorToEndOfLine()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("Hello World");

        await Page.Keyboard.PressAsync("Home");
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.TypeAsync("!");
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.EndsWith("!", rawValue.TrimEnd('\n', '\r'));
    }

    [Fact]
    public async Task ArrowKeys_ShouldNavigateWithinText()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("ABCD");

        // Move cursor left by 2: AB|CD
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Page.Keyboard.PressAsync("ArrowLeft");
        // Insert X: ABXCD
        await Page.Keyboard.TypeAsync("X");
        await WaitForOverlayUpdate();

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

        await Page.Keyboard.PressAsync("Control+b");
        await WaitForOverlayUpdate();
        await Page.Keyboard.PressAsync("Control+i");
        await WaitForOverlayUpdate();

        var hasBold   = await EvalAsync<bool>("() => document.querySelector('.md-overlay strong') !== null");
        var hasItalic = await EvalAsync<bool>("() => document.querySelector('.md-overlay em') !== null");

        Assert.True(hasBold,   "Bold should be applied");
        Assert.True(hasItalic, "Italic should be applied");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Backspace / Delete
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Backspace_ShouldDeleteCharactersBehindCursor()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("Hello");

        await Page.Keyboard.PressAsync("Backspace");
        await Page.Keyboard.PressAsync("Backspace");
        await Page.Keyboard.PressAsync("Backspace");
        await WaitForOverlayUpdate();

        var overlayText = await GetOverlayText();
        Assert.Contains("He", overlayText);
        Assert.DoesNotContain("Hello", overlayText);
    }

    [Fact]
    public async Task Delete_ShouldDeleteCharactersAheadOfCursor()
    {
        await NavigateToEditor();
        await ClickOverlayAndType("Hello");

        await Page.Keyboard.PressAsync("Home");

        await Page.Keyboard.PressAsync("Delete");
        await Page.Keyboard.PressAsync("Delete");
        await Page.Keyboard.PressAsync("Delete");
        await WaitForOverlayUpdate();

        var overlayText = await GetOverlayText();
        Assert.Contains("lo", overlayText);
        Assert.DoesNotContain("Hello", overlayText);
    }
}
