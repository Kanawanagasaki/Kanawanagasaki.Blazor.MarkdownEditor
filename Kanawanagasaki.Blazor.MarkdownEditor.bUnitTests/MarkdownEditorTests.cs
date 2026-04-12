using Bunit;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;
using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.bUnitTests;

// ═══════════════════════════════════════════════════════════════════
//  MarkdownRenderer tests (pure C# — no browser needed)
// ═══════════════════════════════════════════════════════════════════

public class MarkdownRendererTests
{
    [Fact]
    public void EmptyString_ReturnsEmptyResult()
    {
        var result = MarkdownRenderer.Render("");

        Assert.Equal("", result.Html);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void NullInput_ReturnsEmptyResult()
    {
        var result = MarkdownRenderer.Render(null!);

        Assert.Equal("", result.Html);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void PlainText_RendersAsParagraph()
    {
        var result = MarkdownRenderer.Render("Hello world");

        Assert.Contains("Hello world", result.Html);
        Assert.Contains("md-line", result.Html);
        Assert.Single(result.Lines);
        Assert.Equal(0, result.Lines[0].SourceStart);
    }

    [Fact]
    public void Heading1_RendersH1Element()
    {
        var result = MarkdownRenderer.Render("# Title");

        Assert.Contains("<h1", result.Html);
        Assert.Contains("md-h1", result.Html);
        Assert.Contains("Title", result.Html);
        Assert.Single(result.Lines);
    }

    [Fact]
    public void Heading2_RendersH2Element()
    {
        var result = MarkdownRenderer.Render("## Subtitle");

        Assert.Contains("<h2", result.Html);
        Assert.Contains("md-h2", result.Html);
        Assert.Contains("Subtitle", result.Html);
    }

    [Fact]
    public void Heading3_RendersH3Element()
    {
        var result = MarkdownRenderer.Render("### Section");

        Assert.Contains("<h3", result.Html);
        Assert.Contains("md-h3", result.Html);
        Assert.Contains("Section", result.Html);
    }

    [Fact]
    public void Bold_RendersStrongElement()
    {
        var result = MarkdownRenderer.Render("**bold text**");

        Assert.Contains("<strong>", result.Html);
        Assert.Contains("</strong>", result.Html);
        Assert.Contains("bold text", result.Html);
    }

    [Fact]
    public void Italic_RendersEmElement()
    {
        var result = MarkdownRenderer.Render("*italic text*");

        Assert.Contains("<em>", result.Html);
        Assert.Contains("</em>", result.Html);
        Assert.Contains("italic text", result.Html);
    }

    [Fact]
    public void Strikethrough_RendersDelElement()
    {
        var result = MarkdownRenderer.Render("~~deleted~~");

        Assert.Contains("<del>", result.Html);
        Assert.Contains("</del>", result.Html);
        Assert.Contains("deleted", result.Html);
    }

    [Fact]
    public void InlineCode_RendersCodeElement()
    {
        var result = MarkdownRenderer.Render("`code`");

        Assert.Contains("md-inline-code", result.Html);
        Assert.Contains("code", result.Html);
    }

    [Fact]
    public void FencedCodeBlock_RendersPreCode()
    {
        var result = MarkdownRenderer.Render("```\ncode line\n```");

        Assert.Contains("<pre", result.Html);
        Assert.Contains("md-codeblock", result.Html);
        Assert.Contains("code line", result.Html);
    }

    [Fact]
    public void UnorderedList_RendersBulletMarker()
    {
        var result = MarkdownRenderer.Render("- list item");

        Assert.Contains("md-li-marker", result.Html);
        Assert.Contains("list item", result.Html);
    }

    [Fact]
    public void OrderedList_RendersNumberMarker()
    {
        var result = MarkdownRenderer.Render("1. first item");

        Assert.Contains("md-oli-marker", result.Html);
        Assert.Contains("first item", result.Html);
    }

    [Fact]
    public void Blockquote_RendersBlockquoteElement()
    {
        var result = MarkdownRenderer.Render("> quoted text");

        Assert.Contains("<blockquote", result.Html);
        Assert.Contains("md-bq", result.Html);
        Assert.Contains("quoted text", result.Html);
    }

    [Fact]
    public void Link_RendersAnchorElement()
    {
        var result = MarkdownRenderer.Render("[click here](https://example.com)");

        Assert.Contains("<a", result.Html);
        Assert.Contains("href=\"https://example.com\"", result.Html);
        Assert.Contains("click here", result.Html);
    }

    [Fact]
    public void Image_RendersImgElement()
    {
        var result = MarkdownRenderer.Render("![alt text](https://example.com/img.png)");

        Assert.Contains("<img", result.Html);
        Assert.Contains("alt=\"alt text\"", result.Html);
        Assert.Contains("src=\"https://example.com/img.png\"", result.Html);
    }

    [Fact]
    public void HorizontalRule_RendersHrElement()
    {
        var result = MarkdownRenderer.Render("---");

        Assert.Contains("<hr", result.Html);
        Assert.Contains("md-hr", result.Html);
    }

    [Fact]
    public void MultipleLines_ProducesCorrectLineCount()
    {
        var markdown = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        var result = MarkdownRenderer.Render(markdown);

        Assert.Equal(5, result.Lines.Length);
    }

    [Fact]
    public void LineMappings_HaveCorrectSourceStarts()
    {
        var markdown = "aaa\nbbb\nccc";
        var result = MarkdownRenderer.Render(markdown);

        Assert.Equal(0, result.Lines[0].SourceStart);
        Assert.Equal(4, result.Lines[1].SourceStart);
        Assert.Equal(8, result.Lines[2].SourceStart);
    }

    [Fact]
    public void BoldSyntax_NotIncludedInVisibleToSource()
    {
        var result = MarkdownRenderer.Render("**bold**");

        // "bold" should map to source positions 2,3,4,5 (skipping the ** markers)
        var mapping = result.Lines[0].VisibleToSource;
        Assert.Equal(4, mapping.Length);
        Assert.Equal(2, mapping[0]);
        Assert.Equal(3, mapping[1]);
        Assert.Equal(4, mapping[2]);
        Assert.Equal(5, mapping[3]);
    }

    [Fact]
    public void EmptyLine_HasEmptyMapping()
    {
        var result = MarkdownRenderer.Render("hello\n\nworld");

        Assert.Equal(3, result.Lines.Length);
        Assert.Empty(result.Lines[1].VisibleToSource);
        Assert.Contains("md-empty", result.Html);
    }

    [Fact]
    public void SpecialCharacters_EscapedInOutput()
    {
        var result = MarkdownRenderer.Render("foo <bar> & baz");

        Assert.Contains("&lt;", result.Html);
        Assert.Contains("&gt;", result.Html);
        Assert.Contains("&amp;", result.Html);
        Assert.DoesNotContain("<bar>", result.Html);
    }

    [Fact]
    public void BoldItalic_ThreeStarSyntax()
    {
        var result = MarkdownRenderer.Render("***bolditalic***");

        Assert.Contains("<strong>", result.Html);
        Assert.Contains("<em>", result.Html);
        Assert.Contains("bolditalic", result.Html);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  MarkdownTextExtensions tests (pure C# — no browser needed)
// ═══════════════════════════════════════════════════════════════════

public class MarkdownTextExtensionsTests
{
    [Fact]
    public void ToggleBold_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.ToggleBold("bold text", 0, 4);

        Assert.Equal("**bold** text", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(6, result.SelectionEnd);
    }

    [Fact]
    public void ToggleBold_UnwrapsWhenAlreadyBold()
    {
        var result = MarkdownTextExtensions.ToggleBold("**bold** text", 2, 6);

        Assert.Equal("bold text", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(4, result.SelectionEnd);
    }

    [Fact]
    public void ToggleBold_NoSelection_InsertsEmptyMarkers()
    {
        var result = MarkdownTextExtensions.ToggleBold("hello", 2, 2);

        Assert.Equal("he****llo", result.Text);
        Assert.Equal(4, result.SelectionStart);
        Assert.Equal(4, result.SelectionEnd);
    }

    [Fact]
    public void ToggleItalic_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.ToggleItalic("italic text", 0, 6);

        Assert.Equal("*italic* text", result.Text);
        Assert.Equal(1, result.SelectionStart);
        Assert.Equal(7, result.SelectionEnd);
    }

    [Fact]
    public void ToggleItalic_UnwrapsWhenAlreadyItalic()
    {
        var result = MarkdownTextExtensions.ToggleItalic("*italic* text", 1, 7);

        Assert.Equal("italic text", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(6, result.SelectionEnd);
    }

    [Fact]
    public void ToggleItalic_DoesNotUnwrapBoldInnerStar()
    {
        // **text** — inner * should not be treated as italic markers
        var result = MarkdownTextExtensions.ToggleItalic("**text**", 2, 6);

        Assert.Equal("***text***", result.Text);
    }

    [Fact]
    public void ToggleBoldThenItalicTenTimes_ShouldNotAccumulateAsterisks()
    {
        // Simulate: "One two three" → select "two" → Bold → Italic × 10
        string text = "One two three";
        int start = 4; // start of "two"
        int end = 7;   // end of "two"

        // Step 1: Bold
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        Assert.Equal("One **two** three", result.Text);
        text = result.Text;
        start = result.SelectionStart;
        end = result.SelectionEnd;

        // Step 2: Italic × 10
        for (int i = 0; i < 10; i++)
        {
            result = MarkdownTextExtensions.ToggleItalic(text, start, end);
            text = result.Text;
            start = result.SelectionStart;
            end = result.SelectionEnd;
        }

        // After 10 italic toggles (even number), italic should be OFF.
        // Bold should still be ON.
        Assert.Equal("One **two** three", text);

        // Verify no excessive asterisks
        var asteriskCount = text.Count(c => c == '*');
        Assert.True(asteriskCount <= 4,
            $"Expected at most 4 asterisks, but found {asteriskCount} in: {text}");
    }

    [Fact]
    public void ToggleBoldThenItalicOddTimes_ShouldProduceBoldItalic()
    {
        string text = "One two three";
        int start = 4;
        int end = 7;

        // Bold
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text;
        start = result.SelectionStart;
        end = result.SelectionEnd;

        // Italic once (odd)
        result = MarkdownTextExtensions.ToggleItalic(text, start, end);
        Assert.Equal("One ***two*** three", result.Text);
    }

    [Fact]
    public void ToggleItalicOnBoldItalic_RemovesItalicKeepsBold()
    {
        // ***text*** → ToggleItalic → **text**
        var result = MarkdownTextExtensions.ToggleItalic("***text***", 3, 7);
        Assert.Equal("**text**", result.Text);
    }

    [Fact]
    public void ToggleBoldOnBoldItalic_RemovesBoldKeepsItalic()
    {
        // ***text*** → ToggleBold → *text*
        var result = MarkdownTextExtensions.ToggleBold("***text***", 3, 7);
        Assert.Equal("*text*", result.Text);
    }

    [Fact]
    public void ToggleStrikethrough_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.ToggleStrikethrough("strike text", 0, 6);

        Assert.Equal("~~strike~~ text", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(8, result.SelectionEnd);
    }

    [Fact]
    public void ToggleInlineCode_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.ToggleInlineCode("code text", 0, 4);

        Assert.Equal("`code` text", result.Text);
        Assert.Equal(1, result.SelectionStart);
        Assert.Equal(5, result.SelectionEnd);
    }

    [Fact]
    public void ToggleHeading_AddsPrefixToPlainLine()
    {
        var result = MarkdownTextExtensions.ToggleHeading("My Title", 0, 8, 1);

        Assert.Equal("# My Title", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(10, result.SelectionEnd);
    }

    [Fact]
    public void ToggleHeading_ReplacesExistingHeading()
    {
        var result = MarkdownTextExtensions.ToggleHeading("# My Title", 0, 10, 2);

        Assert.Equal("## My Title", result.Text);
        Assert.Equal(3, result.SelectionStart);
        Assert.Equal(11, result.SelectionEnd);
    }

    [Fact]
    public void ToggleUnorderedList_AddsBulletPrefix()
    {
        var result = MarkdownTextExtensions.ToggleUnorderedList("list item", 0, 9);

        Assert.Equal("- list item", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(11, result.SelectionEnd);
    }

    [Fact]
    public void ToggleUnorderedList_RemovesWhenAlreadyPresent()
    {
        var result = MarkdownTextExtensions.ToggleUnorderedList("- list item", 0, 11);

        Assert.Equal("list item", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(9, result.SelectionEnd);
    }

    [Fact]
    public void ToggleOrderedList_AddsNumberPrefix()
    {
        var result = MarkdownTextExtensions.ToggleOrderedList("first item", 0, 10);

        Assert.Equal("1. first item", result.Text);
        Assert.Equal(3, result.SelectionStart);
        Assert.Equal(13, result.SelectionEnd);
    }

    [Fact]
    public void ToggleOrderedList_RemovesWhenAlreadyPresent()
    {
        var result = MarkdownTextExtensions.ToggleOrderedList("1. first item", 0, 12);

        Assert.Equal("first item", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(10, result.SelectionEnd);
    }

    [Fact]
    public void ToggleOrderedList_SwapsFromUnorderedList()
    {
        var result = MarkdownTextExtensions.ToggleOrderedList("- list item", 0, 11);

        Assert.Equal("1. list item", result.Text);
    }

    [Fact]
    public void ToggleBlockquote_AddsQuotePrefix()
    {
        var result = MarkdownTextExtensions.ToggleBlockquote("quoted text", 0, 11);

        Assert.Equal("> quoted text", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(13, result.SelectionEnd);
    }

    [Fact]
    public void ToggleBlockquote_RemovesWhenAlreadyPresent()
    {
        var result = MarkdownTextExtensions.ToggleBlockquote("> quoted text", 0, 13);

        Assert.Equal("quoted text", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(11, result.SelectionEnd);
    }

    [Fact]
    public void InsertLink_WithSelection_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.InsertLink("click here", 0, 10);

        Assert.Equal("[click here](url)", result.Text);
        Assert.Equal(13, result.SelectionStart);
        Assert.Equal(16, result.SelectionEnd);
    }

    [Fact]
    public void InsertLink_NoSelection_UsesDefaultText()
    {
        var result = MarkdownTextExtensions.InsertLink("some text", 5, 5);

        Assert.Equal("some [link text](url)text", result.Text);
    }

    [Fact]
    public void InsertImage_WithSelection_WrapsSelectedText()
    {
        var result = MarkdownTextExtensions.InsertImage("photo", 0, 5);

        Assert.Equal("![photo](url)", result.Text);
        Assert.Equal(9, result.SelectionStart);
        Assert.Equal(12, result.SelectionEnd);
    }

    [Fact]
    public void InsertImage_NoSelection_UsesDefaultAlt()
    {
        var result = MarkdownTextExtensions.InsertImage("some text", 5, 5);

        Assert.Equal("some ![alt text](url)text", result.Text);
    }

    [Fact]
    public void InsertCodeBlock_InsertsFencedBlock()
    {
        var result = MarkdownTextExtensions.InsertCodeBlock("", 0, 0);

        Assert.StartsWith("```", result.Text);
        Assert.EndsWith("```", result.Text.TrimEnd('\n', '\r'));
    }

    [Fact]
    public void InsertCodeBlock_WithSelection_WrapsContent()
    {
        var result = MarkdownTextExtensions.InsertCodeBlock("selected code", 0, 13);

        Assert.Equal("```\nselected code\n```", result.Text);
    }

    [Fact]
    public void InsertHorizontalRule_OnEmptyLine_InsertsHR()
    {
        var result = MarkdownTextExtensions.InsertHorizontalRule("\n", 1, 1);

        Assert.Contains("---", result.Text);
    }

    [Fact]
    public void InsertHorizontalRule_OnOccupiedLine_InsertsAfter()
    {
        var result = MarkdownTextExtensions.InsertHorizontalRule("some text", 0, 9);

        Assert.Contains("some text\n---", result.Text);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Component rendering tests (bUnit — no browser needed)
// ═══════════════════════════════════════════════════════════════════

public class MarkdownEditorComponentTests : BunitContext
{
    private const string JsModulePath = "./_content/Kanawanagasaki.Blazor.MarkdownEditor/js/markdownEditor.js";

    private void SetupJsModule()
    {
        var module = JSInterop.SetupModule(JsModulePath);
        module.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Editor_ShouldRenderTextareaAndOverlay()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>();

        cut.Find(".md-textarea");
        cut.Find(".md-overlay");
    }

    [Fact]
    public void Editor_ShouldRenderToolbar_WhenShowToolbarIsTrue()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.ShowToolbar, true));

        Assert.Contains("md-toolbar", cut.Markup);
    }

    [Fact]
    public void Editor_ShouldNotRenderToolbar_WhenShowToolbarIsFalse()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.ShowToolbar, false));

        Assert.DoesNotContain("md-toolbar", cut.Markup);
    }

    [Fact]
    public void Editor_ShouldShowPlaceholder_OnTextarea()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Placeholder, "Custom placeholder"));

        var textarea = cut.Find(".md-textarea");
        Assert.Equal("Custom placeholder", textarea.GetAttribute("placeholder"));
    }

    [Fact]
    public void Toolbar_ShouldContainExpectedButtons()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>();

        cut.Find("button[title='Bold (Ctrl+B)']");
        cut.Find("button[title='Italic (Ctrl+I)']");
        cut.Find("button[title='Heading 1']");
        cut.Find("button[title='Heading 2']");
        cut.Find("button[title='Heading 3']");
        cut.Find("button[title='Strikethrough']");
        cut.Find("button[title='Inline Code']");
        cut.Find("button[title='Code Block']");
        cut.Find("button[title='Unordered List']");
        cut.Find("button[title='Ordered List']");
        cut.Find("button[title='Blockquote']");
        cut.Find("button[title='Insert Link']");
        cut.Find("button[title='Insert Image']");
        cut.Find("button[title='Horizontal Rule']");
        cut.Find("button[title='Undo (Ctrl+Z)']");
        cut.Find("button[title='Redo (Ctrl+Y)']");
    }

    [Fact]
    public void ToolbarButtons_ShouldBeDisabled_WhenDisabledIsTrue()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Disabled, true));

        var buttons = cut.FindAll(".md-toolbar button");
        Assert.All(buttons, btn =>
        {
            Assert.True(btn.HasAttribute("disabled"),
                $"Button '{btn.GetAttribute("title")}' should be disabled");
        });
    }

    [Fact]
    public void ToolbarButtons_ShouldBeEnabled_WhenDisabledIsFalse()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Disabled, false));

        var undoBtn = cut.Find("button[title='Undo (Ctrl+Z)']");
        Assert.False(undoBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void ValueChanged_ShouldFire_WhenOnInputFromJsIsCalled()
    {
        SetupJsModule();
        string? receivedValue = null;
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.ValueChanged, v => receivedValue = v));

        // Get the component instance and invoke the JS callback directly
        var editor = cut.Instance;
        editor.OnInputFromJs("new value");

        Assert.Equal("new value", receivedValue);
    }

    [Fact]
    public void Value_ShouldUpdate_WhenSetAfterRender()
    {
        SetupJsModule();
        var cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Value, "initial"));

        cut = Render<MarkdownEditor>(parameters => parameters
            .Add(p => p.Value, "updated"));

        // The overlay should reflect the new value
        Assert.Contains("updated", cut.Find(".md-overlay").InnerHtml);
    }
}
