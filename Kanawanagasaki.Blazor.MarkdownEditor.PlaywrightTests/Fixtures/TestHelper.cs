using Microsoft.Playwright;
using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

/// <summary>
/// Abstract base class for all editor Playwright tests.
/// Provides shared helpers for navigation, content injection,
/// selection, and evaluation that eliminate boilerplate and
/// replace hard Task.Delay calls with smart waits.
/// </summary>
public abstract class EditorTestBase : IAsyncLifetime
{
    protected readonly TestAppFixture Fixture;
    protected IPage Page = null!;

    protected EditorTestBase(TestAppFixture fixture)
    {
        Fixture = fixture;
    }

    public virtual async Task InitializeAsync()
    {
        Page = await Fixture.CreatePageAsync();
    }

    public virtual async Task DisposeAsync()
    {
        await Page.CloseAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Navigation
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Navigates to the test app and waits for the Blazor WASM editor
    /// to fully initialise (DOM rendered + JS interop ready).
    /// Uses smart waits — no hard Task.Delay.
    /// </summary>
    protected async Task NavigateToEditor()
    {
        await Page.GotoAsync(Fixture.ServerUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // Blazor WASM bootstrap
        await Page.WaitForFunctionAsync(
            "() => !document.getElementById('app')?.textContent?.includes('Loading...')",
            new PageWaitForFunctionOptions { Timeout = 60000 });

        // Editor DOM elements
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('.md-editor-body') !== null " +
            "&& document.querySelector('.md-textarea') !== null " +
            "&& document.querySelector('.md-overlay') !== null",
            new PageWaitForFunctionOptions { Timeout = 30000 });

        // JS interop fully ready (dotNetRef wired up)
        await Page.WaitForFunctionAsync(
            @"() => {
                const insts = window.__mdEditorInstances;
                if (!insts || insts.size === 0) return false;
                const inst = [...insts.values()][0];
                return inst.dotNetRef != null;
            }",
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Content manipulation
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Fills the editor via JS (instant). Sets textarea.value and
    /// dispatches a synthetic 'input' event so Blazor processes the
    /// change through OnInputFromJs → re-render → overlay update.
    /// </summary>
    protected async Task FillContentAsync(string content)
    {
        await Page.EvaluateAsync<string>(@"(text) => {
            const ta = document.querySelector('.md-textarea');
            ta.focus();
            ta.value = text;
            ta.dispatchEvent(new Event('input', { bubbles: true }));
            return text;
        }", content);

        await WaitForOverlayUpdate();
    }

    /// <summary>
    /// FillContentAsync + wait for a specific number of rendered lines.
    /// Use for scroll / layout tests that need mappings pushed.
    /// </summary>
    protected async Task FillContentAsync(string content, int expectedLineCount)
    {
        await FillContentAsync(content);

        if (expectedLineCount > 0)
        {
            await Page.WaitForFunctionAsync(
                $"() => document.querySelectorAll('.md-overlay [data-line-index]').length >= {expectedLineCount}",
                new PageWaitForFunctionOptions { Timeout = 10000 });
        }
    }

    /// <summary>
    /// FillContentAsync + wait for line-mappings to be pushed from Blazor.
    /// Required before any scroll-sync test.
    /// </summary>
    protected async Task FillContentAndWaitForMappings(string content, int expectedLineCount)
    {
        await FillContentAsync(content, expectedLineCount);

        // Wait for Blazor to push lineMappings (happens in OnAfterRenderAsync)
        await Page.WaitForFunctionAsync(
            $@"() => {{
                const insts = window.__mdEditorInstances;
                if (!insts || insts.size === 0) return false;
                const inst = [...insts.values()][0];
                return inst.lineMappings && inst.lineMappings.length >= {expectedLineCount};
            }}",
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    /// <summary>
    /// Click the overlay (focuses textarea) then type via keyboard.
    /// Use for tests that specifically need to exercise keyboard input
    /// and verify the input→overlay rendering pipeline.
    /// </summary>
    protected async Task ClickOverlayAndType(string text)
    {
        await Page.Locator(".md-overlay").ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => document.activeElement === document.querySelector('.md-textarea')",
            new PageWaitForFunctionOptions { Timeout = 3000 });
        await Page.Keyboard.TypeAsync(text);
        await WaitForOverlayUpdate();
    }

    /// <summary>
    /// Sets the textarea selection range directly via JS.
    /// </summary>
    protected async Task SetTextareaSelection(int start, int end)
    {
        await Page.EvaluateAsync($"() => {{ const ta = document.querySelector('.md-textarea'); ta.focus(); ta.setSelectionRange({start}, {end}); }}");
    }

    /// <summary>
    /// Select all text: re-focuses textarea via overlay click, then Ctrl+A.
    /// </summary>
    protected async Task SelectAll()
    {
        await Page.Locator(".md-overlay").ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => document.activeElement === document.querySelector('.md-textarea')",
            new PageWaitForFunctionOptions { Timeout = 3000 });
        await Page.Keyboard.PressAsync("Control+a");
    }

    /// <summary>
    /// Select <paramref name="charCount"/> characters from the start
    /// of the textarea using keyboard.
    /// </summary>
    protected async Task SelectCharsFromStart(int charCount)
    {
        await Page.Keyboard.PressAsync("Home");
        for (int i = 0; i < charCount; i++)
            await Page.Keyboard.PressAsync("Shift+ArrowRight");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Smart waits (replaces Task.Delay)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Waits until the overlay has re-rendered after a content change.
    /// Checks for any child elements in the overlay (or a minimum count).
    /// Replaces all Task.Delay(150) after typing / button clicks / edits.
    /// </summary>
    protected async Task WaitForOverlayUpdate(int? minLineCount = null)
    {
        if (minLineCount.HasValue && minLineCount.Value > 0)
        {
            await Page.WaitForFunctionAsync(
                $"() => document.querySelectorAll('.md-overlay [data-line-index]').length >= {minLineCount.Value}",
                new PageWaitForFunctionOptions { Timeout = 5000 });
        }
        else
        {
            // Generic wait: give the Blazor async pipeline time to
            // invokeMethodAsync → StateHasChanged → OnAfterRenderAsync
            await Page.WaitForFunctionAsync(
                @"() => {
                    const ta = document.querySelector('.md-textarea');
                    const ov = document.querySelector('.md-overlay');
                    // The overlay should have settled — check that the
                    // raw-value display matches the textarea value.
                    const raw = document.getElementById('raw-value');
                    if (!ta || !ov || !raw) return false;
                    return raw.textContent.trim() === ta.value.trim()
                        || ov.children.length > 0
                        || ta.value.trim() === '';
                }",
                new PageWaitForFunctionOptions { Timeout = 5000 });
        }
    }

    /// <summary>
    /// Waits until the overlay scroll position has been synced from
    /// the textarea after a scroll event. Replaces Task.Delay after
    /// programmatic scrollTop changes or mouse wheel actions.
    /// </summary>
    protected async Task WaitForScrollSync()
    {
        await Page.WaitForFunctionAsync(
            @"() => {
                const insts = window.__mdEditorInstances;
                if (!insts || insts.size === 0) return false;
                const inst = [...insts.values()][0];
                // The _updatingFromTextarea guard should be cleared
                return !inst._updatingFromTextarea && !inst._updatingFromOverlay;
            }",
            new PageWaitForFunctionOptions { Timeout = 3000 });
    }

    /// <summary>
    /// Waits until the cursor element is visible (display === 'block').
    /// Used after clicks that should show the cursor.
    /// </summary>
    protected async Task WaitForCursorVisible()
    {
        await Page.WaitForFunctionAsync(
            "() => { const c = document.querySelector('.md-cursor'); return c && c.style.display === 'block'; }",
            new PageWaitForFunctionOptions { Timeout = 3000 });
    }

    /// <summary>
    /// Waits for the textarea to have focus.
    /// </summary>
    protected async Task WaitForTextareaFocus()
    {
        await Page.WaitForFunctionAsync(
            "() => document.activeElement === document.querySelector('.md-textarea')",
            new PageWaitForFunctionOptions { Timeout = 3000 });
    }

    /// <summary>
    /// Waits for the given raw value to contain the specified text.
    /// </summary>
    protected async Task WaitForRawValueContains(string expected)
    {
        await Page.WaitForFunctionAsync(
            $"() => (document.getElementById('raw-value')?.textContent || '').includes('{EscapeJs(expected)}')",
            new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    /// <summary>
    /// Waits for the given raw value to NOT contain the specified text.
    /// </summary>
    protected async Task WaitForRawValueNotContains(string unexpected)
    {
        await Page.WaitForFunctionAsync(
            $"() => !(document.getElementById('raw-value')?.textContent || '').includes('{EscapeJs(unexpected)}')",
            new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Evaluation helpers
    // ═══════════════════════════════════════════════════════════════

    protected async Task<string> GetRawValue()
        => await Page.Locator("#raw-value").InnerTextAsync();

    protected async Task<string> GetOverlayText()
        => await Page.EvaluateAsync<string>(
            "() => document.querySelector('.md-overlay')?.textContent?.trim() || ''");

    protected async Task<int> GetOverlayLineCount()
        => await Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.md-overlay [data-line-index]').length");

    protected async Task<T> EvalAsync<T>(string expression)
        => await Page.EvaluateAsync<T>(expression);

    protected async Task EvalVoid(string expression, object? arg = null)
    {
        if (arg is null)
            await Page.EvaluateAsync(expression);
        else
            await Page.EvaluateAsync(expression, arg);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Utilities
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates "prefix 1\nprefix 2\n…\nprefix {count}" (count lines).
    /// </summary>
    protected static string GenerateLines(int count, string prefix)
        => string.Join("\n",
            Enumerable.Range(1, count).Select(i => $"{prefix} {i}"));

    /// <summary>
    /// Minimal JS string escaping for use in EvaluateAsync expressions.
    /// </summary>
    private static string EscapeJs(string s) => s.Replace("'", "\\'");
}
