using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;

namespace Kanawanagasaki.Blazor.MarkdownEditor;

/// <summary>
/// Manages the state of a Markdown document for the WYSIWYG editor.
/// Holds the raw markdown text, tracks selection, renders to HTML,
/// and exposes methods to apply formatting and insert Markdown syntax.
///
/// This class is decoupled from Blazor — it has no dependency on
/// <see cref="Microsoft.AspNetCore.Components"/> or <c>Microsoft.JSInterop</c>,
/// making it easy to test and reuse outside of the editor component.
/// </summary>
public class MarkdownDocument
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

    /// <summary>Full render result including per-line position mappings.</summary>
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
    public MarkdownDocument() { }

    /// <summary>Creates a document with initial content.</summary>
    public MarkdownDocument(string initialText)
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

    // ── inline formatting toggles ────────────────────────────

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
    /// </summary>
    private void ApplyEdit(Func<string, int, int, TextEditResult> editFunc)
    {
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
        _dirty = false;
    }
}
