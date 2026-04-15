using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;

namespace Kanawanagasaki.Blazor.MarkdownEditor;

/// <summary>
/// Manages the state of a Markdown document for the WYSIWYG editor.
///
/// <b>The <see cref="EditorDocument"/> AST is the SINGLE SOURCE OF TRUTH.</b>
/// All editing operations (toggle bold, change block type, etc.) mutate the
/// AST directly. Markdown text is ONLY DERIVED from the AST as the final step
/// via <see cref="EditorDocumentRenderer.Render"/>. The textarea is then
/// populated with the derived text.
///
/// Flow:
/// 1. Textarea selection maps to AST range (EditorDocument positions)
/// 2. Mutate AST directly (toggle style flags on EditorInlineSegment,
///    change block types, etc.)
/// 3. Extract markdown from AST via EditorDocumentRenderer.Render()
/// 4. Populate textarea with the derived text
/// 5. Adjust cursor
/// 6. Raise ValueChanged callback
///
/// When <see cref="Text"/> is set externally (from textarea input), the text
/// is parsed into the AST via <see cref="EditorDocumentParser.Parse"/> so that
/// the AST remains the authoritative state for subsequent operations.
///
/// This class is decoupled from Blazor — it has no dependency on
/// <see cref="Microsoft.AspNetCore.Components"/> or <c>Microsoft.JSInterop</c>,
/// making it easy to test and reuse outside of the editor component.
/// </summary>
public class MarkdownDocumentWrapper
{
    // ── AST state (SINGLE SOURCE OF TRUTH) ────────────────────

    private EditorDocument _editorDoc = new();

    // ── text state (derived from AST) ────────────────────────

    private string _text = "";

    /// <summary>Current raw Markdown source text (derived from the AST).</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value ?? "";
            // Parse external text into the AST so it becomes the source of truth
            _editorDoc = EditorDocumentParser.Parse(_text);
            MarkDirty();
        }
    }

    // ── Markdig AST (derived from render for backward compat) ─

    private MarkdownDocument? _document;

    /// <summary>
    /// The Markdig MarkdownDocument derived from the render result.
    /// Kept for backward compatibility with <see cref="FindInlineAtPosition"/>
    /// and <see cref="FindBlockAtPosition"/>.
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

    /// <summary>Rendered HTML for the overlay (cached until the AST changes).</summary>
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
        _editorDoc = EditorDocumentParser.Parse(_text);
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
    /// the <see cref="EditorDocument"/> AST directly — no re-parsing
    /// through Markdig.
    /// </summary>
    public InlineStyle DetectInlineStylesAtPosition(int sourceOffset)
    {
        if (_editorDoc.Blocks.Count == 0) return InlineStyle.None;

        var (blockIdx, contentOffset) = EditorDocumentEditor.MapPositionToContent(_editorDoc, sourceOffset);
        if (blockIdx >= _editorDoc.Blocks.Count) return InlineStyle.None;

        var block = _editorDoc.Blocks[blockIdx];
        if (!block.HasInlineContent || block.Segments.Count == 0) return InlineStyle.None;

        // Find the segment that contains the content offset
        int pos = 0;
        for (int i = 0; i < block.Segments.Count; i++)
        {
            var seg = block.Segments[i];
            if (contentOffset < pos + seg.Text.Length)
                return seg.Styles;
            pos += seg.Text.Length;
        }

        // Offset at or past the end of content
        return InlineStyle.None;
    }

    // ── inline formatting toggles (AST-first) ────────────────

    /// <summary>
    /// Toggle <c>**bold**</c> around the current selection.
    /// Mutates the <see cref="EditorDocument"/> AST directly.
    /// </summary>
    public void ToggleBold() => ApplyAstEdit(EditorDocumentEditor.ToggleBold);

    /// <summary>
    /// Toggle <c>*italic*</c> around the current selection.
    /// Mutates the <see cref="EditorDocument"/> AST directly.
    /// </summary>
    public void ToggleItalic() => ApplyAstEdit(EditorDocumentEditor.ToggleItalic);

    /// <summary>
    /// Toggle <c>~~strikethrough~~</c> around the current selection.
    /// Mutates the <see cref="EditorDocument"/> AST directly.
    /// </summary>
    public void ToggleStrikethrough() => ApplyAstEdit(EditorDocumentEditor.ToggleStrikethrough);

    /// <summary>
    /// Toggle <c>`inline code`</c> around the current selection.
    /// Mutates the <see cref="EditorDocument"/> AST directly.
    /// </summary>
    public void ToggleInlineCode() => ApplyAstEdit(EditorDocumentEditor.ToggleInlineCode);

    // ── block formatting toggles ─────────────────────────────

    /// <summary>Toggle an ATX heading prefix on the current line(s).</summary>
    /// <param name="level">Heading level 1-6.</param>
    public void ToggleHeading(int level)
        => ApplyAstEdit((doc, start, end) => EditorDocumentEditor.ToggleHeading(doc, start, end, level));

    /// <summary>Toggle unordered list prefix on the current line(s).</summary>
    public void ToggleUnorderedList()
        => ApplyAstEdit(EditorDocumentEditor.ToggleUnorderedList);

    /// <summary>Toggle ordered list prefix on the current line(s).</summary>
    public void ToggleOrderedList()
        => ApplyAstEdit(EditorDocumentEditor.ToggleOrderedList);

    /// <summary>Toggle blockquote prefix on the current line(s).</summary>
    public void ToggleBlockquote()
        => ApplyAstEdit(EditorDocumentEditor.ToggleBlockquote);

    // ── insertions ───────────────────────────────────────────

    /// <summary>Insert a Markdown link at the current selection.</summary>
    public void InsertLink()
        => ApplyAstEdit(EditorDocumentEditor.InsertLink);

    /// <summary>Insert a Markdown image at the current selection.</summary>
    public void InsertImage()
        => ApplyAstEdit(EditorDocumentEditor.InsertImage);

    /// <summary>Insert a fenced code block around the current selection.</summary>
    public void InsertCodeBlock()
        => ApplyAstEdit(EditorDocumentEditor.InsertCodeBlock);

    /// <summary>Insert a horizontal rule at the current position.</summary>
    public void InsertHorizontalRule()
        => ApplyAstEdit(EditorDocumentEditor.InsertHorizontalRule);

    /// <summary>Insert arbitrary text at the current caret position.</summary>
    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Derive current markdown from the AST, apply insertion, then re-parse
        string currentMd = EditorDocumentRenderer.Render(_editorDoc);
        string newText = currentMd.Substring(0, SelectionStart)
                        + text
                        + currentMd.Substring(SelectionEnd);
        int newPos = SelectionStart + text.Length;

        // Re-parse into the AST to keep it as the single source of truth
        _editorDoc = EditorDocumentParser.Parse(newText);
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
    /// AST-first edit: mutates <see cref="_editorDoc"/> directly via
    /// <see cref="EditorDocumentEditor"/> methods, then derives the markdown
    /// text from the mutated AST.
    ///
    /// Flow:
    /// 1. Selection → map to AST range via EditorDocumentEditor
    /// 2. Mutate AST directly (toggle style flags, change block types, etc.)
    /// 3. Extract markdown from AST via EditorDocumentRenderer.Render()
    /// 4. Re-parse into _editorDoc to keep AST as single source of truth
    ///    (for operations that only modify text, like no-selection marker
    ///    insertion or link insertion)
    /// 5. Fire TextChanged → ValueChanged on the component
    /// </summary>
    private void ApplyAstEdit(Func<EditorDocument, int, int, TextEditResult> editFunc)
    {
        // Step 1-2: Apply edit on _editorDoc (mutates AST directly for
        // style toggles; returns text-only result for insertions/no-selection)
        var result = editFunc(_editorDoc, SelectionStart, SelectionEnd);

        // Step 3: Derive text from the result
        _text = result.Text;

        // Step 4: Re-parse to keep _editorDoc as the single source of truth.
        // For AST-mutating edits (style toggles with selection), this produces
        // an equivalent AST. For text-only edits (no-selection marker insertion,
        // link insertion), re-parsing is necessary because _editorDoc wasn't
        // mutated to reflect the new text.
        _editorDoc = EditorDocumentParser.Parse(_text);

        // Step 5: Update selection positions
        NewSelectionStart = result.SelectionStart;
        NewSelectionEnd = result.SelectionEnd;
        SelectionStart = result.SelectionStart;
        SelectionEnd = result.SelectionEnd;

        // Step 6: Mark dirty for HTML re-rendering
        MarkDirty();
        _ = Document; // Force immediate re-render

        // Step 7: Notify listeners
        TextChanged?.Invoke(_text);
    }

    private void MarkDirty()
    {
        _dirty = true;
    }

    private void EnsureRendered()
    {
        _renderResult = EditorDocumentHtmlRenderer.Render(_editorDoc);
        _renderedHtml = _renderResult.Html;
        _document = _renderResult.Document;
        _dirty = false;
    }
}
