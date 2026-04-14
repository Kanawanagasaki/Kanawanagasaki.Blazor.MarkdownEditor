using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;
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
        return MarkdownTextExtensions.DetectInlineStyles(_text, sourceOffset);
    }

    // ── inline formatting toggles (AST-first) ────────────────

    /// <summary>
    /// Toggle <c>**bold**</c> around the current selection.
    /// Uses Markdig's AST as the primary source of truth for style detection.
    /// </summary>
    public void ToggleBold() => ApplyAstEdit(MarkdownTextExtensions.ToggleBold);

    /// <summary>
    /// Toggle <c>*italic*</c> around the current selection.
    /// Uses Markdig's AST as the primary source of truth for style detection.
    /// </summary>
    public void ToggleItalic() => ApplyAstEdit(MarkdownTextExtensions.ToggleItalic);

    /// <summary>
    /// Toggle <c>~~strikethrough~~</c> around the current selection.
    /// Uses Markdig's AST as the primary source of truth for style detection.
    /// </summary>
    public void ToggleStrikethrough() => ApplyAstEdit(MarkdownTextExtensions.ToggleStrikethrough);

    /// <summary>
    /// Toggle <c>`inline code`</c> around the current selection.
    /// Uses Markdig's AST as the primary source of truth for style detection.
    /// </summary>
    public void ToggleInlineCode() => ApplyAstEdit(MarkdownTextExtensions.ToggleInlineCode);

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
    /// AST-first inline edit: uses Markdig's MarkdownDocument as the
    /// primary source of truth for detecting existing styles, then applies the
    /// toggle. After the edit, the text is immediately re-parsed through
    /// Markdig to produce a fresh MarkdownDocument.
    ///
    /// Flow:
    /// 1. Selection → map to Markdig AST range (detect styles from AST)
    /// 2. Apply style change (add/remove markers based on AST detection)
    /// 3. Extract markdown from the result (text is derived from AST-driven edit)
    /// 4. Immediately re-parse to keep AST as source of truth
    /// 5. Fire TextChanged → ValueChanged on the component
    /// </summary>
    private void ApplyAstEdit(Func<string, int, int, TextEditResult> editFunc)
    {
        // Step 1: Use the current Document (AST) for detection
        // The editFunc (ToggleBold/Italic/etc.) now uses AST-first detection
        var result = editFunc(_text, SelectionStart, SelectionEnd);

        // Step 2: Update text and selection
        _text = result.Text;
        NewSelectionStart = result.SelectionStart;
        NewSelectionEnd = result.SelectionEnd;
        SelectionStart = result.SelectionStart;
        SelectionEnd = result.SelectionEnd;

        // Step 3: Immediately re-parse to keep AST as source of truth
        MarkDirty();
        _ = Document; // Force immediate re-parse

        // Step 4: Notify listeners
        TextChanged?.Invoke(_text);
    }

    /// <summary>
    /// Generic helper that applies a text-edit function to the current
    /// selection and updates all document state accordingly.
    /// Used for block-level operations (headings, lists, blockquotes)
    /// and insertions where AST-based inline detection isn't needed.
    /// After the edit, immediately re-parses through Markdig.
    /// </summary>
    private void ApplyEdit(Func<string, int, int, TextEditResult> editFunc)
    {
        var result = editFunc(_text, SelectionStart, SelectionEnd);

        _text = result.Text;
        NewSelectionStart = result.SelectionStart;
        NewSelectionEnd = result.SelectionEnd;
        SelectionStart = result.SelectionStart;
        SelectionEnd = result.SelectionEnd;

        // Immediately re-parse to keep AST as source of truth
        MarkDirty();
        _ = Document; // Force immediate re-parse

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
