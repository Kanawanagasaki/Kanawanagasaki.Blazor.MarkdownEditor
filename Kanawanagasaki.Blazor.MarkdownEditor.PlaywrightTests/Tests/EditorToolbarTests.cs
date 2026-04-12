using Microsoft.Playwright;
using Xunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Tests;

/// <summary>
/// Toolbar button tests: Bold, Italic, Strikethrough, Headings,
/// Code Block, Lists, Blockquote, Link, Image, HR, Undo, Redo.
///
/// Tests that apply formatting to existing text use the fast
/// FillContentAsync + SetTextareaSelection pattern.
/// Tests that verify undo/redo or sequential toggle behaviour
/// retain real keyboard interaction to exercise the real undo stack.
/// </summary>
[Collection(EditorTestCollection.Name)]
public class EditorToolbarTests : EditorTestBase
{
    public EditorToolbarTests(TestAppFixture fixture)
        : base(fixture) { }

    // ═══════════════════════════════════════════════════════════════
    //  Bold
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BoldButton_ShouldWrapSelectedText()
    {
        await NavigateToEditor();
        await FillContentAsync("bold text");
        await SetTextareaSelection(0, 4);

        await Page.Locator(".md-btn-bold").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("**bold**", rawValue);
    }

    [Fact]
    public async Task BoldButton_ShouldRenderStrongElement()
    {
        await NavigateToEditor();
        await FillContentAsync("bold text");
        await SetTextareaSelection(0, 4);

        await Page.Locator(".md-btn-bold").ClickAsync();
        await WaitForOverlayUpdate();

        var hasStrong = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay strong') !== null");
        Assert.True(hasStrong, "Overlay should contain <strong> after Bold");
    }

    [Fact]
    public async Task BoldButton_ToggleOff_ShouldUnwrapText()
    {
        await NavigateToEditor();

        // Type and apply bold via keyboard (need real typing for undo stack)
        await ClickOverlayAndType("bold text");
        await SelectCharsFromStart(4);
        await Page.Locator(".md-btn-bold").ClickAsync();
        await WaitForOverlayUpdate();

        // Verify bold is applied
        var rawAfterBold = await GetRawValue();
        Assert.Contains("**bold**", rawAfterBold);

        // Re-position cursor inside the bold markers and toggle off
        await Page.Locator(".md-overlay").ClickAsync();
        await Page.Keyboard.PressAsync("Home");
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("ArrowRight");
        // Select 4 chars ("bold") from current position using Shift+ArrowRight
        for (int i = 0; i < 4; i++)
            await Page.Keyboard.PressAsync("Shift+ArrowRight");
        await Page.Locator(".md-btn-bold").ClickAsync();
        await WaitForOverlayUpdate();

        var hasStrong = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay strong') !== null");
        Assert.False(hasStrong, "Bold should be removed after toggle-off");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Italic
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ItalicButton_ShouldWrapSelectedText()
    {
        await NavigateToEditor();
        await FillContentAsync("italic text");
        await SetTextareaSelection(0, 6);

        await Page.Locator(".md-btn-italic").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("*italic*", rawValue);
    }

    [Fact]
    public async Task ItalicButton_ShouldRenderEmElement()
    {
        await NavigateToEditor();
        await FillContentAsync("italic text");
        await SetTextareaSelection(0, 6);

        await Page.Locator(".md-btn-italic").ClickAsync();
        await WaitForOverlayUpdate();

        var hasEm = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay em') !== null");
        Assert.True(hasEm, "Overlay should contain <em> after Italic");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Strikethrough
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task StrikethroughButton_ShouldWrapSelectedText()
    {
        await NavigateToEditor();
        await FillContentAsync("strike text");
        await SetTextareaSelection(0, 6);

        await Page.Locator("button[title='Strikethrough']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("~~strike~~", rawValue);
    }

    [Fact]
    public async Task StrikethroughButton_ShouldRenderDelElement()
    {
        await NavigateToEditor();
        await FillContentAsync("strike text");
        await SetTextareaSelection(0, 6);

        await Page.Locator("button[title='Strikethrough']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasDel = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay del') !== null");
        Assert.True(hasDel, "Overlay should contain <del> after Strikethrough");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Headings
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task H1Button_ShouldAddHeadingPrefix()
    {
        await NavigateToEditor();
        await FillContentAsync("My Title");

        await Page.Locator("button[title='Heading 1']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.StartsWith("# My Title", rawValue.Trim());
    }

    [Fact]
    public async Task H1Button_ShouldRenderH1Element()
    {
        await NavigateToEditor();
        await FillContentAsync("My Title");

        await Page.Locator("button[title='Heading 1']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasH1 = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay .md-h1') !== null || document.querySelector('.md-overlay h1') !== null");
        Assert.True(hasH1, "Overlay should contain H1 element");
    }

    [Fact]
    public async Task H2Button_ShouldAddH2Prefix()
    {
        await NavigateToEditor();
        await FillContentAsync("Subtitle");

        await Page.Locator("button[title='Heading 2']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.StartsWith("## Subtitle", rawValue.Trim());
    }

    [Fact]
    public async Task H2Button_ShouldRenderH2Element()
    {
        await NavigateToEditor();
        await FillContentAsync("Subtitle");

        await Page.Locator("button[title='Heading 2']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasH2 = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay .md-h2') !== null || document.querySelector('.md-overlay h2') !== null");
        Assert.True(hasH2, "Overlay should contain H2 element");
    }

    [Fact]
    public async Task H3Button_ShouldAddH3Prefix()
    {
        await NavigateToEditor();
        await FillContentAsync("Section");

        await Page.Locator("button[title='Heading 3']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.StartsWith("### Section", rawValue.Trim());
    }

    [Fact]
    public async Task H3Button_ShouldRenderH3Element()
    {
        await NavigateToEditor();
        await FillContentAsync("Section");

        await Page.Locator("button[title='Heading 3']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasH3 = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay .md-h3') !== null || document.querySelector('.md-overlay h3') !== null");
        Assert.True(hasH3, "Overlay should contain H3 element");
    }

    [Fact]
    public async Task HeadingButton_ShouldReplaceExistingHeading()
    {
        await NavigateToEditor();
        await FillContentAsync("My Title");

        // Apply H1
        await Page.Locator("button[title='Heading 1']").ClickAsync();
        await WaitForOverlayUpdate();
        var rawH1 = await GetRawValue();
        Assert.StartsWith("# My Title", rawH1.Trim());

        // Switch to H2
        await Page.Locator("button[title='Heading 2']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawH2 = await GetRawValue();
        Assert.StartsWith("## My Title", rawH2.Trim());
        Assert.False(rawH2.TrimStart().StartsWith("# "),
            "H1 prefix should be replaced, not duplicated");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Code Block
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CodeBlockButton_ShouldInsertFencedCodeBlock()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await Page.Locator("button[title='Code Block']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("```", rawValue);
    }

    [Fact]
    public async Task CodeBlockButton_ShouldRenderCodeBlockElement()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await Page.Locator("button[title='Code Block']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasCode = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay pre') !== null || document.querySelector('.md-overlay .md-codeblock') !== null");
        Assert.True(hasCode, "Overlay should contain code block element");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Inline Code
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task InlineCodeButton_ShouldWrapSelectedText()
    {
        await NavigateToEditor();
        await FillContentAsync("code text");
        await SetTextareaSelection(0, 4);

        await Page.Locator("button[title='Inline Code']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("`code`", rawValue);
    }

    [Fact]
    public async Task InlineCodeButton_ShouldRenderCodeElement()
    {
        await NavigateToEditor();
        await FillContentAsync("code text");
        await SetTextareaSelection(0, 4);

        await Page.Locator("button[title='Inline Code']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasCode = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay .md-inline-code') !== null");
        Assert.True(hasCode, "Overlay should contain .md-inline-code after Inline Code");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Unordered List
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ULButton_ShouldAddBulletPrefix()
    {
        await NavigateToEditor();
        await FillContentAsync("list item");

        await Page.Locator("button[title='Unordered List']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("- list item", rawValue);
    }

    [Fact]
    public async Task ULButton_ShouldRenderBulletMarker()
    {
        await NavigateToEditor();
        await FillContentAsync("list item");

        await Page.Locator("button[title='Unordered List']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasMarker = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay .md-li-marker') !== null");
        Assert.True(hasMarker, "Overlay should contain .md-li-marker after UL");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Ordered List
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task OLButton_ShouldAddNumberPrefix()
    {
        await NavigateToEditor();
        await FillContentAsync("first item");

        await Page.Locator("button[title='Ordered List']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("1. first item", rawValue);
    }

    [Fact]
    public async Task OLButton_ShouldRenderNumberMarker()
    {
        await NavigateToEditor();
        await FillContentAsync("first item");

        await Page.Locator("button[title='Ordered List']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasMarker = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay .md-oli-marker') !== null");
        Assert.True(hasMarker, "Overlay should contain .md-oli-marker after OL");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Blockquote
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BlockquoteButton_ShouldAddQuotePrefix()
    {
        await NavigateToEditor();
        await FillContentAsync("quoted text");

        await Page.Locator("button[title='Blockquote']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("> quoted text", rawValue);
    }

    [Fact]
    public async Task BlockquoteButton_ShouldRenderBlockquoteElement()
    {
        await NavigateToEditor();
        await FillContentAsync("quoted text");

        await Page.Locator("button[title='Blockquote']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasBq = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay blockquote') !== null || document.querySelector('.md-overlay .md-bq') !== null");
        Assert.True(hasBq, "Overlay should contain blockquote element");
    }

    [Fact]
    public async Task BlockquoteButton_ShouldToggleOff()
    {
        await NavigateToEditor();
        await FillContentAsync("quoted text");

        await Page.Locator("button[title='Blockquote']").ClickAsync();
        await WaitForOverlayUpdate();
        var rawOn = await GetRawValue();
        Assert.Contains("> ", rawOn);

        await Page.Locator("button[title='Blockquote']").ClickAsync();
        await WaitForOverlayUpdate();
        var rawOff = await GetRawValue();
        Assert.DoesNotContain("> ", rawOff);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Link insertion
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkButton_ShouldInsertLinkTemplate()
    {
        await NavigateToEditor();
        await FillContentAsync("click here");
        await SelectAll();

        await Page.Locator("button[title='Insert Link']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("[click here](url)", rawValue);
    }

    [Fact]
    public async Task LinkButton_WithNoSelection_ShouldInsertDefaultTemplate()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await Page.Locator("button[title='Insert Link']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("[link text](url)", rawValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Image insertion
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImageButton_ShouldInsertImageTemplate()
    {
        await NavigateToEditor();
        await FillContentAsync("photo");
        await SelectAll();

        await Page.Locator("button[title='Insert Image']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("![photo](url)", rawValue);
    }

    [Fact]
    public async Task ImageButton_WithNoSelection_ShouldInsertDefaultTemplate()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await Page.Locator("button[title='Insert Image']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("![alt text](url)", rawValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Horizontal Rule
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task HRButton_ShouldInsertHorizontalRule()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await Page.Locator("button[title='Horizontal Rule']").ClickAsync();
        await WaitForOverlayUpdate();

        var rawValue = await GetRawValue();
        Assert.Contains("---", rawValue);
    }

    [Fact]
    public async Task HRButton_ShouldRenderHrElement()
    {
        await NavigateToEditor();

        await Page.Locator(".md-overlay").ClickAsync();
        await Page.Locator("button[title='Horizontal Rule']").ClickAsync();
        await WaitForOverlayUpdate();

        var hasHr = await EvalAsync<bool>(
            "() => document.querySelector('.md-overlay hr') !== null || document.querySelector('.md-overlay .md-hr') !== null");
        Assert.True(hasHr, "Overlay should contain HR element");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Undo / Redo (real keyboard typing — tests real undo stack)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task UndoButton_ShouldRevertLastTyping()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");
        await Page.Locator("button[title='Undo (Ctrl+Z)']").ClickAsync();
        await WaitForOverlayUpdate();

        var overlayText = await GetOverlayText();
        Assert.DoesNotContain("Hello", overlayText);
    }

    [Fact]
    public async Task RedoButton_ShouldRestoreUndoneText()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello");
        await Page.Locator("button[title='Undo (Ctrl+Z)']").ClickAsync();
        await WaitForOverlayUpdate();
        await Page.Locator("button[title='Redo (Ctrl+Y)']").ClickAsync();
        await WaitForOverlayUpdate();

        var overlayText = await GetOverlayText();
        Assert.Contains("Hello", overlayText);
    }

    [Fact]
    public async Task UndoRedo_ShouldRestoreAfterUndo()
    {
        await NavigateToEditor();

        await ClickOverlayAndType("Hello World");

        var undoBtn = Page.Locator("button[title='Undo (Ctrl+Z)']");
        var redoBtn = Page.Locator("button[title='Redo (Ctrl+Y)']");

        // Undo all typed text
        await undoBtn.ClickAsync();
        await WaitForOverlayUpdate();

        var rawAfterUndo = await GetRawValue();
        Assert.DoesNotContain("Hello", rawAfterUndo);

        // Redo restores the text
        await redoBtn.ClickAsync();
        await WaitForOverlayUpdate();

        var rawAfterRedo = await GetRawValue();
        Assert.Contains("Hello", rawAfterRedo);
    }
}
