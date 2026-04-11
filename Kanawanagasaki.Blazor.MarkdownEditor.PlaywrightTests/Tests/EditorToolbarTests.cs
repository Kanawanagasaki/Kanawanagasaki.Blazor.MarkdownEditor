using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Toolbar button tests: Bold, Italic, Strikethrough, Headings, Code Block,
/// Lists, Blockquote, Link, Image, Horizontal Rule, Undo, Redo.
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorToolbarTests : IAsyncLifetime
{
    private readonly TestAppFixture _fixture;
    private IPage _page = null!;

    public EditorToolbarTests(TestAppFixture fixture)
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

    private async Task SelectCharsFromStart(int charCount)
    {
        await _page.Keyboard.PressAsync("Home");
        await Task.Delay(50);
        for (int i = 0; i < charCount; i++)
        {
            await _page.Keyboard.PressAsync("Shift+ArrowRight");
            await Task.Delay(30);
        }
        await Task.Delay(100);
    }

    private async Task SelectAll()
    {
        // Re-focus textarea via overlay click first (toolbar buttons steal focus)
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
    //  Bold tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BoldButton_ShouldWrapSelectedText()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("bold text");
        await SelectCharsFromStart(4);

        await _page.Locator(".md-btn-bold").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("**bold**", rawValue);
    }

    [Fact]
    public async Task BoldButton_ShouldRenderStrongElement()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("bold text");
        await SelectCharsFromStart(4);

        await _page.Locator(".md-btn-bold").ClickAsync();
        await Task.Delay(300);

        var hasStrong = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay strong') !== null");

        Assert.True(hasStrong, "Overlay should contain a <strong> element after clicking Bold");
    }

    [Fact]
    public async Task BoldButton_ToggleOff_ShouldUnwrapText()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("bold text");
        await SelectCharsFromStart(4);

        // Toggle bold on
        await _page.Locator(".md-btn-bold").ClickAsync();
        await Task.Delay(500);

        // Re-focus the textarea by clicking the overlay, then select all
        await _page.Locator(".md-overlay").ClickAsync();
        await Task.Delay(200);
        await _page.Keyboard.PressAsync("Control+a");
        await Task.Delay(300);

        // Toggle bold off
        await _page.Locator(".md-btn-bold").ClickAsync();
        await Task.Delay(500);

        var hasStrong = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay strong') !== null");

        Assert.False(hasStrong, "Bold should be toggled off after second click");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Italic tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ItalicButton_ShouldWrapSelectedText()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("italic text");
        await SelectCharsFromStart(6);

        await _page.Locator(".md-btn-italic").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("*italic*", rawValue);
    }

    [Fact]
    public async Task ItalicButton_ShouldRenderEmElement()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("italic text");
        await SelectCharsFromStart(6);

        await _page.Locator(".md-btn-italic").ClickAsync();
        await Task.Delay(300);

        var hasItalic = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay em') !== null");

        Assert.True(hasItalic, "Overlay should contain an <em> element after clicking Italic");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Strikethrough tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task StrikethroughButton_ShouldWrapSelectedText()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("strike text");
        await SelectCharsFromStart(6);

        await _page.Locator("button[title='Strikethrough']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("~~strike~~", rawValue);
    }

    [Fact]
    public async Task StrikethroughButton_ShouldRenderDelElement()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("strike text");
        await SelectCharsFromStart(6);

        await _page.Locator("button[title='Strikethrough']").ClickAsync();
        await Task.Delay(300);

        var hasDel = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay del') !== null");

        Assert.True(hasDel, "Overlay should contain a <del> element after clicking Strikethrough");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Heading tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task H1Button_ShouldAddHeadingPrefix()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("My Title");

        await _page.Locator("button[title='Heading 1']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.StartsWith("# My Title", rawValue.Trim());
    }

    [Fact]
    public async Task H1Button_ShouldRenderH1Element()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("My Title");

        await _page.Locator("button[title='Heading 1']").ClickAsync();
        await Task.Delay(300);

        var hasH1 = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay h1') !== null");

        Assert.True(hasH1, "Overlay should contain an <h1> element after clicking H1");
    }

    [Fact]
    public async Task H2Button_ShouldAddH2Prefix()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Subtitle");

        await _page.Locator("button[title='Heading 2']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.StartsWith("## Subtitle", rawValue.Trim());
    }

    [Fact]
    public async Task H2Button_ShouldRenderH2Element()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Subtitle");

        await _page.Locator("button[title='Heading 2']").ClickAsync();
        await Task.Delay(300);

        var hasH2 = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay h2') !== null");

        Assert.True(hasH2, "Overlay should contain an <h2> element after clicking H2");
    }

    [Fact]
    public async Task H3Button_ShouldAddH3Prefix()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Section");

        await _page.Locator("button[title='Heading 3']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.StartsWith("### Section", rawValue.Trim());
    }

    [Fact]
    public async Task H3Button_ShouldRenderH3Element()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Section");

        await _page.Locator("button[title='Heading 3']").ClickAsync();
        await Task.Delay(300);

        var hasH3 = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay h3') !== null");

        Assert.True(hasH3, "Overlay should contain an <h3> element after clicking H3");
    }

    [Fact]
    public async Task HeadingButton_ShouldReplaceExistingHeading()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("My Title");
        await _page.Locator("button[title='Heading 1']").ClickAsync();
        await Task.Delay(300);

        // Now switch to H2
        await _page.Locator("button[title='Heading 2']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.StartsWith("## My Title", rawValue.Trim());
    }

    // ═══════════════════════════════════════════════════════════════
    //  Code Block tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CodeBlockButton_ShouldInsertFencedCodeBlock()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Locator("button[title='Code Block']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("```", rawValue);
    }

    [Fact]
    public async Task CodeBlockButton_ShouldRenderPreElement()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Locator("button[title='Code Block']").ClickAsync();
        await Task.Delay(300);

        var hasPre = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay pre') !== null || document.querySelector('.md-overlay .md-codeblock') !== null");

        Assert.True(hasPre, "Overlay should contain a <pre> or .md-codeblock element after inserting code block");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Inline Code tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task InlineCodeButton_ShouldWrapSelectedText()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("code text");
        await SelectCharsFromStart(4);

        await _page.Locator("button[title='Inline Code']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("`code`", rawValue);
    }

    [Fact]
    public async Task InlineCodeButton_ShouldRenderCodeElement()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("code text");
        await SelectCharsFromStart(4);

        await _page.Locator("button[title='Inline Code']").ClickAsync();
        await Task.Delay(300);

        var hasCode = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay .md-inline-code') !== null");

        Assert.True(hasCode, "Overlay should contain an inline code element after clicking Inline Code");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Unordered List tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ULButton_ShouldAddBulletPrefix()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("list item");

        await _page.Locator("button[title='Unordered List']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("- list item", rawValue);
    }

    [Fact]
    public async Task ULButton_ShouldRenderBulletMarker()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("list item");

        await _page.Locator("button[title='Unordered List']").ClickAsync();
        await Task.Delay(300);

        var hasMarker = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay .md-li-marker') !== null");

        Assert.True(hasMarker, "Overlay should contain a bullet marker after clicking UL");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Ordered List tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task OLButton_ShouldAddNumberPrefix()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("first item");

        await _page.Locator("button[title='Ordered List']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("1. first item", rawValue);
    }

    [Fact]
    public async Task OLButton_ShouldRenderNumberMarker()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("first item");

        await _page.Locator("button[title='Ordered List']").ClickAsync();
        await Task.Delay(300);

        var hasMarker = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay .md-oli-marker') !== null");

        Assert.True(hasMarker, "Overlay should contain an ordered list marker after clicking OL");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Blockquote tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BlockquoteButton_ShouldAddQuotePrefix()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("quoted text");

        await _page.Locator("button[title='Blockquote']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("> quoted text", rawValue);
    }

    [Fact]
    public async Task BlockquoteButton_ShouldRenderBlockquoteElement()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("quoted text");

        await _page.Locator("button[title='Blockquote']").ClickAsync();
        await Task.Delay(300);

        var hasBq = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay blockquote') !== null || document.querySelector('.md-overlay .md-bq') !== null");

        Assert.True(hasBq, "Overlay should contain a blockquote element after clicking Blockquote");
    }

    [Fact]
    public async Task BlockquoteButton_ShouldToggleOff()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("quoted text");

        // Toggle on
        await _page.Locator("button[title='Blockquote']").ClickAsync();
        await Task.Delay(300);

        // Toggle off
        await _page.Locator("button[title='Blockquote']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.DoesNotContain("> ", rawValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Link insertion tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkButton_ShouldInsertLinkTemplate()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("click here");

        await _page.Locator("button[title='Insert Link']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("[click here](url)", rawValue);
    }

    [Fact]
    public async Task LinkButton_WithNoSelection_ShouldInsertDefaultLinkTemplate()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Locator("button[title='Insert Link']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("[link text](url)", rawValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Image insertion tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImageButton_ShouldInsertImageTemplate()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("photo");

        await _page.Locator("button[title='Insert Image']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("![photo](url)", rawValue);
    }

    [Fact]
    public async Task ImageButton_WithNoSelection_ShouldInsertDefaultImageTemplate()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Locator("button[title='Insert Image']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("![alt text](url)", rawValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Horizontal Rule tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task HRButton_ShouldInsertHorizontalRule()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Locator("button[title='Horizontal Rule']").ClickAsync();
        await Task.Delay(300);

        var rawValue = await GetRawValue();
        Assert.Contains("---", rawValue);
    }

    [Fact]
    public async Task HRButton_ShouldRenderHrElement()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Locator("button[title='Horizontal Rule']").ClickAsync();
        await Task.Delay(300);

        var hasHr = await _page.EvaluateAsync<bool>(
            "() => document.querySelector('.md-overlay hr') !== null || document.querySelector('.md-overlay .md-hr') !== null");

        Assert.True(hasHr, "Overlay should contain an <hr> element after inserting HR");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Undo / Redo tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task UndoButton_ShouldRevertLastTyping()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");
        await Task.Delay(200);

        var undoBtn = _page.Locator("button[title='Undo (Ctrl+Z)']");
        await undoBtn.ClickAsync();
        await Task.Delay(300);

        var overlayText = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

        Assert.DoesNotContain("Hello", overlayText);
    }

    [Fact]
    public async Task RedoButton_ShouldRestoreUndoneText()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");
        await Task.Delay(200);

        var undoBtn = _page.Locator("button[title='Undo (Ctrl+Z)']");
        await undoBtn.ClickAsync();
        await Task.Delay(300);

        var redoBtn = _page.Locator("button[title='Redo (Ctrl+Y)']");
        await redoBtn.ClickAsync();
        await Task.Delay(300);

        var overlayText = await _page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

        Assert.Contains("Hello", overlayText);
    }

    [Fact]
    public async Task UndoRedo_ShouldRestoreMultipleChanges()
    {
        await NavigateToEditor();

        var overlay = _page.Locator(".md-overlay");
        await overlay.ClickAsync();
        await Task.Delay(200);

        await _page.Keyboard.TypeAsync("One ");
        await Task.Delay(200);
        await _page.Keyboard.TypeAsync("Two ");
        await Task.Delay(200);
        await _page.Keyboard.TypeAsync("Three");
        await Task.Delay(500);

        var undoBtn = _page.Locator("button[title='Undo (Ctrl+Z)']");
        var redoBtn = _page.Locator("button[title='Redo (Ctrl+Y)']");

        // Undo twice
        await undoBtn.ClickAsync();
        await Task.Delay(200);
        await undoBtn.ClickAsync();
        await Task.Delay(300);

        var rawAfterUndo = await GetRawValue();
        Assert.DoesNotContain("Three", rawAfterUndo);

        // Redo once
        await redoBtn.ClickAsync();
        await Task.Delay(300);

        var rawAfterRedo = await GetRawValue();
        Assert.Contains("Three", rawAfterRedo);
    }
}
