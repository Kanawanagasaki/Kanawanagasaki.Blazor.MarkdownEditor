using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;

namespace Kanawanagasaki.Blazor.MarkdownEditor;

/// <summary>
/// Base class for the <see cref="MarkdownEditor"/> component.
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
    protected ElementReference _selectionLayer;

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

    // ── internal state ─────────────────────────────────────────

    protected string _value = "";
    protected string _renderedHtml = "";
    protected RenderResult _renderResult = new();
    private bool _jsReady;
    private bool _externalValueChange;
    private bool _pendingMappingPush;
    private DotNetObjectReference<MarkdownEditorBase>? _dotNetRef;

    // ── lifecycle ──────────────────────────────────────────────

    protected override void OnInitialized()
    {
        _dotNetRef = DotNetObjectReference.Create(this);
    }

    protected override void OnParametersSet()
    {
        if (Value != _value)
        {
            _value = Value;
            _renderResult = MarkdownRenderer.Render(_value);
            _renderedHtml = _renderResult.Html;
            _externalValueChange = true;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _jsModule = await JS.InvokeAsync<IJSObjectReference>("import",
                "./_content/Kanawanagasaki.Blazor.MarkdownEditor/js/markdownEditor.js");

            await _jsModule.InvokeVoidAsync("initEditor",
                _instanceId, _editorBody, _textarea, _overlay, _cursor, _selectionLayer);

            await _jsModule.InvokeVoidAsync("setDotNetRef", _instanceId, _dotNetRef);
            await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
            _pendingMappingPush = true;
            _jsReady = true;
        }

        if (_externalValueChange && _jsReady && _jsModule is not null)
        {
            await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
            await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);
            _externalValueChange = false;
        }

        if (_pendingMappingPush && _jsReady && _jsModule is not null)
        {
            _pendingMappingPush = false;
            await PushMappings();
            // After pushing mappings, sync cursor and scroll
            // since the overlay content just changed
            await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);
        }
    }

    // ── JS callbacks ───────────────────────────────────────────

    /// <summary>Called from JS when the textarea value changes (input event).
    /// Used instead of Blazor @oninput because pointer-events:none on the
    /// textarea prevents Blazor's event binding from firing.</summary>
    [JSInvokable("OnInputFromJs")]
    public async Task OnInputFromJs(string newValue)
    {
        _value = newValue ?? "";
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;

        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        StateHasChanged();
    }

    /// <summary>Called from JS for keyboard shortcut handling.
    /// Used instead of Blazor @onkeydown for the same reason as OnInputFromJs.</summary>
    [JSInvokable("OnKeyDownFromJs")]
    public async Task OnKeyDownFromJs(string key, bool ctrlKey, bool metaKey)
    {
        if (ctrlKey || metaKey)
        {
            switch (key.ToLowerInvariant())
            {
                case "b":
                    await ApplyBold();
                    return;
                case "i":
                    await ApplyItalic();
                    return;
            }
        }

        if (key == "Tab")
        {
            // preventDefault is handled in JS keydown listener
            await InsertText("  ");
        }
    }

    /// <summary>Called from JS when the cursor changes via overlay click.</summary>
    [JSInvokable("OnCursorChangedFromJs")]
    public void OnCursorChangedFromJs()
    {
        // The textarea cursor moved due to an overlay click.
        // We don't need to change the value, just let Blazor know
        // the cursor position might have changed.
        // No StateHasChanged needed — the overlay didn't change.
    }

    // ── mapping sync ───────────────────────────────────────────

    private async Task PushMappings()
    {
        if (_jsModule is null) return;

        var serializable = _renderResult.Lines.Select(l => new
        {
            sourceStart = l.SourceStart,
            visibleToSource = l.VisibleToSource,
        }).ToArray();

        await _jsModule.InvokeVoidAsync("updateMappings", _instanceId, serializable);
    }

    // ── toolbar actions: inline toggles ────────────────────────

    protected async Task ApplyBold()
        => await ApplyInlineToggle(MarkdownTextExtensions.ToggleBold);

    protected async Task ApplyItalic()
        => await ApplyInlineToggle(MarkdownTextExtensions.ToggleItalic);

    protected async Task ApplyStrikethrough()
        => await ApplyInlineToggle(MarkdownTextExtensions.ToggleStrikethrough);

    protected async Task ApplyInlineCode()
        => await ApplyInlineToggle(MarkdownTextExtensions.ToggleInlineCode);

    protected async Task ApplyInlineToggle(
        Func<string, int, int, TextEditResult> editFunc)
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        var result = editFunc(_value, sel.Start, sel.End);

        _value = result.Text;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, result.SelectionStart, result.SelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    // ── toolbar actions: block-level ───────────────────────────

    protected async Task ApplyHeading(int level)
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        var result = MarkdownTextExtensions.ToggleHeading(_value, sel.Start, sel.End, level);

        _value = result.Text;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, result.SelectionStart, result.SelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    protected async Task ApplyUnorderedList()
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        var result = MarkdownTextExtensions.ToggleUnorderedList(_value, sel.Start, sel.End);

        _value = result.Text;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, result.SelectionStart, result.SelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    protected async Task ApplyOrderedList()
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        var result = MarkdownTextExtensions.ToggleOrderedList(_value, sel.Start, sel.End);

        _value = result.Text;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, result.SelectionStart, result.SelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    protected async Task ApplyBlockquote()
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        var result = MarkdownTextExtensions.ToggleBlockquote(_value, sel.Start, sel.End);

        _value = result.Text;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, result.SelectionStart, result.SelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    // ── toolbar actions: insertions ────────────────────────────

    protected async Task InsertLink()
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        var result = MarkdownTextExtensions.InsertLink(_value, sel.Start, sel.End);

        _value = result.Text;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, result.SelectionStart, result.SelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    protected async Task InsertImage()
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        var result = MarkdownTextExtensions.InsertImage(_value, sel.Start, sel.End);

        _value = result.Text;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, result.SelectionStart, result.SelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    protected async Task InsertCodeBlock()
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        var result = MarkdownTextExtensions.InsertCodeBlock(_value, sel.Start, sel.End);

        _value = result.Text;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, result.SelectionStart, result.SelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    protected async Task InsertHorizontalRule()
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        var result = MarkdownTextExtensions.InsertHorizontalRule(_value, sel.Start, sel.End);

        _value = result.Text;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection",
            _instanceId, result.SelectionStart, result.SelectionEnd);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

    protected async Task InsertText(string text)
    {
        if (_jsModule is null) return;

        var sel = await _jsModule.InvokeAsync<SelectionRange>("getSelection", _instanceId);
        string newText = _value.Substring(0, sel.Start) + text + _value.Substring(sel.End);
        int newPos = sel.Start + text.Length;

        _value = newText;
        _renderResult = MarkdownRenderer.Render(_value);
        _renderedHtml = _renderResult.Html;
        await ValueChanged.InvokeAsync(_value);
        _pendingMappingPush = true;

        await _jsModule.InvokeVoidAsync("setTextValue", _instanceId, _value);
        await _jsModule.InvokeVoidAsync("setSelection", _instanceId, newPos, newPos);
        await _jsModule.InvokeVoidAsync("updateCursorPosition", _instanceId);

        StateHasChanged();
    }

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
