using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;

namespace Kanawanagasaki.Blazor.MarkdownEditor;

/// <summary>
/// Base class for the <see cref="MarkdownEditor"/> component.
/// Owns the Blazor lifecycle, JS interop, and wires the <see cref="MarkdownDocumentWrapper"/>
/// to the UI.
///
/// The <see cref="MarkdownDocumentWrapper"/> holds a Markdig <see cref="MarkdownDocument"/>
/// as the single source of truth. All rendering, position mapping, and formatting
/// detection flow through Markdig's AST. Textarea input events trigger re-parsing
/// into a fresh MarkdownDocument, and toolbar actions use the AST for accurate
/// style detection before producing source text mutations.
/// </summary>
public abstract class MarkdownEditorBase : ComponentBase, IAsyncDisposable
{
    // ── JS interop ─────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private IJSObjectReference? _jsModule;
    private string _instanceId = Guid.NewGuid().ToString("N");

    // ── element references ─────────────────────────────────────

    protected ElementReference _editorBody;
    protected ElementReference _textarea;
    protected ElementReference _overlay;
    protected ElementReference _cursor;
    protected ElementReference _selectionContainer;

    // ── parameters ─────────────────────────────────────────────

    /// <summary>Two-way bindable Markdown source text.</summary>
    [Parameter]
    public string Value { get; set; } = "";

    /// <summary>Callback invoked when <see cref="Value"/> changes.</summary>
    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Placeholder shown when the editor is empty.</summary>
    [Parameter]
    public string Placeholder { get; set; } = "Type markdown here...";

    /// <summary>Disable all input and toolbar buttons.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>Whether the toolbar is visible.</summary>
    [Parameter]
    public bool ShowToolbar { get; set; } = true;

    /// <summary>Additional HTML attributes forwarded to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>
    /// Exposes the <see cref="MarkdownDocumentWrapper"/> that backs this editor
    /// so consumers can programmatically apply formatting or inspect state.
    /// </summary>
    [Parameter]
    public MarkdownDocumentWrapper? Document { get; set; }

    /// <summary>
    /// Callback invoked when the <see cref="MarkdownDocumentWrapper"/> is first created.
    /// Use this to capture a reference and call methods like
    /// <see cref="MarkdownDocumentWrapper.ToggleBold"/> from code.
    /// </summary>
    [Parameter]
    public EventCallback<MarkdownDocumentWrapper> DocumentCreated { get; set; }

    // ── internal state ─────────────────────────────────────────

    protected MarkdownDocumentWrapper _document = new();
    private bool _jsReady;
    private bool _externalValueChange;
    private bool _pendingMappingPush;
    private DotNetObjectReference<MarkdownEditorBase>? _dotNetRef;
    private bool _documentCreatedFired;

    // ── lifecycle ──────────────────────────────────────────────

    protected override void OnInitialized()
    {
        _dotNetRef = DotNetObjectReference.Create(this);

        // Allow external document injection
        if (Document is not null)
        {
            _document = Document;
        }

        _document.TextChanged += OnDocumentTextChanged;
    }

    protected override void OnParametersSet()
    {
        // Sync external Value into document
        if (Value != _document.Text)
        {
            // Temporarily unsubscribe to avoid re-firing ValueChanged
            _document.TextChanged -= OnDocumentTextChanged;
            _document.Text = Value;
            _document.TextChanged += OnDocumentTextChanged;
            _externalValueChange = true;
        }

        // Handle external Document injection after init
        if (Document is not null && Document != _document)
        {
            _document.TextChanged -= OnDocumentTextChanged;
            _document = Document;
            _document.TextChanged += OnDocumentTextChanged;
            _externalValueChange = true;
        }

        // Fire DocumentCreated callback once
        if (!_documentCreatedFired)
        {
            _documentCreatedFired = true;
            _ = DocumentCreated.InvokeAsync(_document);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _jsModule = await JS.InvokeAsync<IJSObjectReference>("import",
                "./_content/Kanawanagasaki.Blazor.MarkdownEditor/js/markdownEditor.js");

            await _jsModule.InvokeVoidAsync("initEditor",
                _instanceId, _editorBody, _textarea, _overlay, _cursor, _selectionContainer);

            await _jsModule.InvokeVoidAsync("setDotNetRef", _instanceId, _dotNetRef);
            await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _document.Text);
            _pendingMappingPush = true;
            _jsReady = true;
        }

        if (_externalValueChange && _jsReady && _jsModule is not null)
        {
            await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _document.Text);
            await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);
            _externalValueChange = false;
        }

        if (_pendingMappingPush && _jsReady && _jsModule is not null)
        {
            _pendingMappingPush = false;
            await PushMappings();
            await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);
        }
    }

    // ── document → UI sync ─────────────────────────────────────

    private async void OnDocumentTextChanged(string newText)
    {
        await ValueChanged.InvokeAsync(newText);
        _pendingMappingPush = true;
        StateHasChanged();
    }

    // ── JS callbacks ───────────────────────────────────────────

    /// <summary>
    /// Called from JS when the textarea value changes.
    /// The new text is set on the document, which triggers re-parsing
    /// through Markdig to produce a fresh MarkdownDocument (the source of truth).
    /// </summary>
    [JSInvokable("OnInputFromJs")]
    public async Task OnInputFromJs(string newValue)
    {
        // Direct text update bypasses the TextChanged event
        // so we don't double-fire ValueChanged
        _document.TextChanged -= OnDocumentTextChanged;
        _document.Text = newValue ?? "";
        _document.TextChanged += OnDocumentTextChanged;

        await ValueChanged.InvokeAsync(_document.Text);
        _pendingMappingPush = true;

        StateHasChanged();
    }

    /// <summary>Called from JS for keyboard shortcut handling.</summary>
    [JSInvokable("OnKeyDownFromJs")]
    public async Task OnKeyDownFromJs(string key, bool ctrlKey, bool metaKey)
    {
        if (ctrlKey || metaKey)
        {
            switch (key.ToLowerInvariant())
            {
                case "b":
                    await ApplyAndSync(_document.ToggleBold);
                    return;
                case "i":
                    await ApplyAndSync(_document.ToggleItalic);
                    return;
            }
        }

        if (key == "Tab")
        {
            await ApplyAndSync(() => _document.InsertText("  "));
        }
    }

    /// <summary>Called from JS when the cursor changes via overlay click.</summary>
    [JSInvokable("OnCursorChangedFromJs")]
    public void OnCursorChangedFromJs()
    {
        // Cursor moved via overlay click — no value change needed.
    }

    // ── mapping sync ───────────────────────────────────────────

    private async Task PushMappings()
    {
        if (_jsModule is null) return;

        var result = _document.RenderResult;
        var serializable = result.Lines.Select(l => new
        {
            sourceStart = l.SourceStart,
            visibleToSource = l.VisibleToSource,
        }).ToArray();

        await _jsModule.InvokeVoidAsync("updateMappings", _instanceId, serializable);
    }

    // ── toolbar actions ────────────────────────────────────────

    /// <summary>
    /// Generic helper: execute a document edit and sync to JS.
    /// After the edit, the document's MarkdownDocument is re-parsed
    /// (lazily on next access), producing fresh position mappings.
    /// </summary>
    private async Task ApplyAndSync(Action editAction)
    {
        if (_jsModule is null) return;

        // Read current selection from JS textarea
        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        _document.SetSelection(sel.Start, sel.End);

        editAction();

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _document.Text);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, _document.NewSelectionStart, _document.NewSelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    // ── toolbar: inline toggles ────────────────────────────────

    protected Task ApplyBold() => ApplyAndSync(_document.ToggleBold);
    protected Task ApplyItalic() => ApplyAndSync(_document.ToggleItalic);
    protected Task ApplyStrikethrough() => ApplyAndSync(_document.ToggleStrikethrough);
    protected Task ApplyInlineCode() => ApplyAndSync(_document.ToggleInlineCode);

    // ── toolbar: block-level ───────────────────────────────────

    protected Task ApplyHeading(int level) => ApplyAndSync(() => _document.ToggleHeading(level));
    protected Task ApplyUnorderedList() => ApplyAndSync(_document.ToggleUnorderedList);
    protected Task ApplyOrderedList() => ApplyAndSync(_document.ToggleOrderedList);
    protected Task ApplyBlockquote() => ApplyAndSync(_document.ToggleBlockquote);

    // ── toolbar: insertions ────────────────────────────────────

    protected Task InsertLink() => ApplyAndSync(_document.InsertLink);
    protected Task InsertImage() => ApplyAndSync(_document.InsertImage);
    protected Task InsertCodeBlock() => ApplyAndSync(_document.InsertCodeBlock);
    protected Task InsertHorizontalRule() => ApplyAndSync(_document.InsertHorizontalRule);

    // ── native undo / redo ─────────────────────────────────────

    protected async Task NativeUndo()
    {
        if (_jsModule is null) return;
        await _jsModule.InvokeVoidAsync("nativeUndo", _instanceId);
    }

    protected async Task NativeRedo()
    {
        if (_jsModule is null) return;
        await _jsModule.InvokeVoidAsync("nativeRedo", _instanceId);
    }

    // ── cleanup ────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _document.TextChanged -= OnDocumentTextChanged;

        if (_dotNetRef is not null)
        {
            try { _dotNetRef.Dispose(); }
            catch { }
        }

        if (_jsModule is not null)
        {
            try { await _jsModule.InvokeVoidAsync("dispose", _instanceId); }
            catch { }

            try { await _jsModule.DisposeAsync(); }
            catch { }
        }
    }

    // ── JS interop DTO ─────────────────────────────────────────

    private record SelectionRange(int Start, int End);
}
