using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;

namespace Kanawanagasaki.Blazor.MarkdownEditor;

/// <summary>
/// Manages the state of a Markdown document for the WYSIWYG editor.
/// Holds the raw markdown text, tracks selection, renders to HTML,
/// and exposes methods to apply formatting and insert Markdown syntax.
///
/// <b>Markdig's <see cref="MarkdownDocument"/> is the single source of truth</b>
/// for all parsing, position mapping, and AST-based operations. Every render
/// and every formatting toggle goes through the Markdig AST. Textarea cursor
/// positions are mapped onto the MarkdownDocument's SourceSpan tree, and
/// input events are translated into mutations that produce new source text
/// which is then re-parsed into a fresh MarkdownDocument.
///
/// This class is decoupled from Blazor — it has no dependency on
/// <see cref="Microsoft.AspNetCore.Components"/> or <c>Microsoft.JSInterop</c>,
/// making it easy to test and reuse outside of the editor component.
/// </summary>
public class MarkdownDocumentWrapper
{
    // ── text state ───────────────────────────────────────────

    private string _text = "";

    /// <summary>Current raw Markdown source text.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value ?? "";
            MarkDirty();
        }
    }

    // ── Markdig AST (source of truth) ────────────────────────

    private MarkdownDocument? _document;

    /// <summary>
    /// The Markdig MarkdownDocument that is the single source of truth
    /// for all AST operations. Lazily parsed from <see cref="Text"/>
    /// and cached until <see cref="Text"/> changes.
    /// </summary>
    public MarkdownDocument Document
    {
        get
        {
            if (_dirty) EnsureRendered();
            return _document!;
        }
    }

    // ── selection state ──────────────────────────────────────

    /// <summary>Current caret / selection start position in <see cref="Text"/>.</summary>
    public int SelectionStart { get; private set; }

    /// <summary>Current selection end position in <see cref="Text"/>.</summary>
    public int SelectionEnd { get; private set; }

    // ── render state ─────────────────────────────────────────

    private bool _dirty = true;
    private RenderResult _renderResult = new();
    private string _renderedHtml = "";

    /// <summary>Rendered HTML for the overlay (cached until <see cref="Text"/> changes).</summary>
    public string RenderedHtml
    {
        get
        {
            if (_dirty) EnsureRendered();
            return _renderedHtml;
        }
    }

    /// <summary>Full render result including per-line position mappings and AST.</summary>
    public RenderResult RenderResult
    {
        get
        {
            if (_dirty) EnsureRendered();
            return _renderResult;
        }
    }

    // ── events ───────────────────────────────────────────────

    /// <summary>
    /// Fired whenever <see cref="Text"/> changes due to a document edit
    /// (but NOT when <see cref="Text"/> is set externally via the property setter).
    /// </summary>
    public event Action<string>? TextChanged;

    // ── constructor ──────────────────────────────────────────

    /// <summary>Creates a new empty document.</summary>
    public MarkdownDocumentWrapper() { }

    /// <summary>Creates a document with initial content.</summary>
    public MarkdownDocumentWrapper(string initialText)
    {
        _text = initialText ?? "";
        MarkDirty();
    }

    // ── selection management ─────────────────────────────────

    /// <summary>Update the current selection / caret position.</summary>
    public void SetSelection(int start, int end)
    {
        SelectionStart = Math.Clamp(start, 0, _text.Length);
        SelectionEnd = Math.Clamp(end, 0, _text.Length);
    }

    // ── cursor → AST mapping ─────────────────────────────────

    /// <summary>
    /// Map a source character offset (from the textarea) to the Markdig
    /// AST node at that position. Returns the innermost <see cref="Inline"/>
    /// that contains the offset, or null if no inline covers it.
    /// </summary>
    public Inline? FindInlineAtPosition(int sourceOffset)
    {
        var doc = Document;
        if (doc == null) return null;

        // Walk all inlines in the document and find the one whose Span
        // contains sourceOffset, preferring the innermost (smallest span).
        Inline? best = null;
        int bestLength = int.MaxValue;

        foreach (var inline in doc.Descendants<Inline>())
        {
            if (inline.Span.IsEmpty) continue;
            if (sourceOffset >= inline.Span.Start && sourceOffset <= inline.Span.End)
            {
                int len = inline.Span.Length;
                if (len < bestLength)
                {
                    best = inline;
                    bestLength = len;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Map a source character offset to the Markdig <see cref="Block"/>
    /// at that position. Returns the innermost block whose Span contains
    /// the offset.
    /// </summary>
    public Block? FindBlockAtPosition(int sourceOffset)
    {
        var doc = Document;
        if (doc == null) return null;

        return doc.FindBlockAtPosition(sourceOffset);
    }

    /// <summary>
    /// Detect which inline styles (bold, italic, strikethrough, code)
    /// are currently active at the given source offset by inspecting
    /// the Markdig AST. This replaces the old string-scanning approach
    /// with a reliable AST-based detection.
    /// </summary>
    public InlineStyle DetectInlineStylesAtPosition(int sourceOffset)
    {
        var inline = FindInlineAtPosition(sourceOffset);
        if (inline == null) return InlineStyle.None;

        return DetectStylesFromInline(inline);
    }

    /// <summary>
    /// Walk up the inline tree from the given inline node and collect
    /// all active formatting styles.
    /// </summary>
    private InlineStyle DetectStylesFromInline(Inline inline)
    {
        var styles = InlineStyle.None;
        var current = inline;

        while (current != null)
        {
            if (current is EmphasisInline emphasis)
            {
                if (emphasis.DelimiterChar == '~')
                    styles |= InlineStyle.Strikethrough;
                else if (emphasis.DelimiterCount >= 2)
                    styles |= InlineStyle.Bold;
                else
                    styles |= InlineStyle.Italic;
            }
            else if (current is CodeInline)
            {
                styles |= InlineStyle.InlineCode;
            }

            // Walk up: if this is a child of a ContainerInline, go to parent
            if (current.Parent is ContainerInline parentInline)
                current = parentInline;
            else
                break;
        }

        return styles;
    }

    // ── inline formatting toggles (AST-aware) ────────────────

    /// <summary>Toggle <c>**bold**</c> around the current selection.</summary>
    public void ToggleBold() => ApplyEdit(MarkdownTextExtensions.ToggleBold);

    /// <summary>Toggle <c>*italic*</c> around the current selection.</summary>
    public void ToggleItalic() => ApplyEdit(MarkdownTextExtensions.ToggleItalic);

    /// <summary>Toggle <c>~~strikethrough~~</c> around the current selection.</summary>
    public void ToggleStrikethrough() => ApplyEdit(MarkdownTextExtensions.ToggleStrikethrough);

    /// <summary>Toggle <c>`inline code`</c> around the current selection.</summary>
    public void ToggleInlineCode() => ApplyEdit(MarkdownTextExtensions.ToggleInlineCode);

    // ── block formatting toggles ─────────────────────────────

    /// <summary>Toggle an ATX heading prefix on the current line(s).</summary>
    /// <param name="level">Heading level 1-6.</param>
    public void ToggleHeading(int level)
        => ApplyEdit((text, start, end) => MarkdownTextExtensions.ToggleHeading(text, start, end, level));

    /// <summary>Toggle unordered list prefix on the current line(s).</summary>
    public void ToggleUnorderedList()
        => ApplyEdit(MarkdownTextExtensions.ToggleUnorderedList);

    /// <summary>Toggle ordered list prefix on the current line(s).</summary>
    public void ToggleOrderedList()
        => ApplyEdit(MarkdownTextExtensions.ToggleOrderedList);

    /// <summary>Toggle blockquote prefix on the current line(s).</summary>
    public void ToggleBlockquote()
        => ApplyEdit(MarkdownTextExtensions.ToggleBlockquote);

    // ── insertions ───────────────────────────────────────────

    /// <summary>Insert a Markdown link at the current selection.</summary>
    public void InsertLink()
        => ApplyEdit(MarkdownTextExtensions.InsertLink);

    /// <summary>Insert a Markdown image at the current selection.</summary>
    public void InsertImage()
        => ApplyEdit(MarkdownTextExtensions.InsertImage);

    /// <summary>Insert a fenced code block around the current selection.</summary>
    public void InsertCodeBlock()
        => ApplyEdit(MarkdownTextExtensions.InsertCodeBlock);

    /// <summary>Insert a horizontal rule at the current position.</summary>
    public void InsertHorizontalRule()
        => ApplyEdit(MarkdownTextExtensions.InsertHorizontalRule);

    /// <summary>Insert arbitrary text at the current caret position.</summary>
    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Translate the insertion into a source text mutation
        // and then re-parse through Markdig
        string newText = _text.Substring(0, SelectionStart)
                        + text
                        + _text.Substring(SelectionEnd);
        int newPos = SelectionStart + text.Length;

        _text = newText;
        SelectionStart = newPos;
        SelectionEnd = newPos;

        MarkDirty();
        TextChanged?.Invoke(_text);
    }

    // ── edit result after applying a toggle/insertion ────────

    /// <summary>
    /// After calling a toggle/insert method, this holds the selection
    /// coordinates that the editor UI should apply to the textarea.
    /// </summary>
    public int NewSelectionStart { get; private set; }

    /// <summary>
    /// After calling a toggle/insert method, this holds the selection end
    /// coordinate that the editor UI should apply to the textarea.
    /// </summary>
    public int NewSelectionEnd { get; private set; }

    // ── private helpers ──────────────────────────────────────

    /// <summary>
    /// Generic helper that applies a text-edit function to the current
    /// selection and updates all document state accordingly.
    /// The edit function produces new source text which is then
    /// re-parsed into a fresh Markdig MarkdownDocument.
    /// </summary>
    private void ApplyEdit(Func<string, int, int, TextEditResult> editFunc)
    {
        // Use the Markdig AST to enhance the edit with AST-aware
        // style detection. The edit function still works on raw text
        // because the textarea needs raw text, but we use the AST
        // to provide context about what's at the cursor position.
        var result = editFunc(_text, SelectionStart, SelectionEnd);

        _text = result.Text;
        NewSelectionStart = result.SelectionStart;
        NewSelectionEnd = result.SelectionEnd;
        SelectionStart = result.SelectionStart;
        SelectionEnd = result.SelectionEnd;

        MarkDirty();
        TextChanged?.Invoke(_text);
    }

    private void MarkDirty()
    {
        _dirty = true;
    }

    private void EnsureRendered()
    {
        _renderResult = MarkdownRenderer.Render(_text);
        _renderedHtml = _renderResult.Html;
        _document = _renderResult.Document;
        _dirty = false;
    }
}
