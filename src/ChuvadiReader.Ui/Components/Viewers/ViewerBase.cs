using ChuvadiReader.Core.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ChuvadiReader.Ui.Components.Viewers;

/// <summary>
/// Common contract of the view-only viewers (D-07, D-09): DocxView, XlsxView, MarkdownView,
/// TextView and HtmlView. The Reader hosts one per non-PDF tab and drives it through these
/// parameters and public methods; each viewer implements what makes sense for its format and
/// ignores the rest.
/// </summary>
public abstract class ViewerBase : ComponentBase, IAsyncDisposable
{
    [Inject] protected IJSRuntime JS { get; set; } = default!;

    /// <summary>The file to show. Changing it loads the new file.</summary>
    [Parameter] public string Path { get; set; } = "";

    /// <summary>Position for the top-bar counter; fired on load and as the user scrolls or switches.</summary>
    [Parameter] public EventCallback<ViewerCounter> OnCounter { get; set; }

    /// <summary>Outline for the Pages &amp; outline drawer; fired after each load.</summary>
    [Parameter] public EventCallback<IReadOnlyList<ViewerOutlineEntry>> OnOutline { get; set; }

    /// <summary>Find text (case-insensitive). Matches are highlighted; the count goes to <see cref="OnFindCount"/>.</summary>
    [Parameter] public string? FindQuery { get; set; }

    [Parameter] public EventCallback<int> OnFindCount { get; set; }

    /// <summary>Text size in percent (View rail). 100 = design size.</summary>
    [Parameter] public int TextSizePct { get; set; } = 100;

    /// <summary>"narrow" | "normal" | "wide" (View rail).</summary>
    [Parameter] public string ReadingWidth { get; set; } = "normal";

    /// <summary>Wrap long lines (plain text and source view).</summary>
    [Parameter] public bool WrapLines { get; set; }

    /// <summary>Markdown / HTML: show the raw source as numbered text instead of the rendered view.</summary>
    [Parameter] public bool ShowSource { get; set; }

    /// <summary>One-line description for the status bar, e.g. "Markdown · rendered · 186 words".</summary>
    [Parameter] public EventCallback<string> OnStatus { get; set; }

    /// <summary>Go to page / section / line / sheet <paramref name="index"/> (1-based).</summary>
    public abstract Task GoToAsync(int index);

    /// <summary>Scroll to an outline entry's <see cref="ViewerOutlineEntry.Anchor"/>.</summary>
    public abstract Task ScrollToAnchorAsync(string anchor);

    /// <summary>All text of the document, for "Copy all".</summary>
    public abstract string GetAllText();

    // ── shared plumbing ───────────────────────────────────────────────────────────────────

    private IJSObjectReference? _module;
    private DotNetObjectReference<ViewerBase>? _self;
    private ViewerCounter? _lastCounter;
    private bool _disposed;

    protected bool Disposed => _disposed;

    protected double Scale => Math.Clamp(TextSizePct <= 0 ? 100 : TextSizePct, 50, 300) / 100.0;

    protected string WidthClass => ReadingWidth?.ToLowerInvariant() switch
    {
        "narrow" => "w-narrow",
        "wide" => "w-wide",
        _ => "w-normal",
    };

    protected static string Css(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    protected DotNetObjectReference<ViewerBase> Self => _self ??= DotNetObjectReference.Create(this);

    protected async ValueTask<IJSObjectReference?> ModuleAsync()
    {
        if (_disposed) return null;
        try
        {
            return _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/ChuvadiReader.Ui/js/viewers.js");
        }
        catch (JSDisconnectedException) { return null; }
        catch (InvalidOperationException) { return null; } // no JS yet (prerender / tests)
    }

    /// <summary>Calls a viewers.js export, swallowing teardown races.</summary>
    protected async Task JsVoidAsync(string fn, params object?[] args)
    {
        var m = await ModuleAsync();
        if (m is null) return;
        try { await m.InvokeVoidAsync(fn, args); }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    protected async Task<T?> JsAsync<T>(string fn, params object?[] args)
    {
        var m = await ModuleAsync();
        if (m is null) return default;
        try { return await m.InvokeAsync<T>(fn, args); }
        catch (JSDisconnectedException) { return default; }
        catch (JSException) { return default; }
        catch (TaskCanceledException) { return default; }
        catch (ObjectDisposedException) { return default; }
    }

    /// <summary>Called from viewers.js with the 1-based section/line at the top of the view.</summary>
    [JSInvokable]
    public Task OnViewerScroll(int index) => HandleScrollAsync(index);

    protected virtual Task HandleScrollAsync(int index) => Task.CompletedTask;

    protected Task ReportCounterAsync(int current, int total, string unit, bool force = false)
    {
        total = Math.Max(1, total);
        var c = new ViewerCounter(Math.Clamp(current, 1, total), total, unit);
        if (!force && c == _lastCounter) return Task.CompletedTask;
        _lastCounter = c;
        return OnCounter.HasDelegate ? OnCounter.InvokeAsync(c) : Task.CompletedTask;
    }

    protected Task ReportOutlineAsync(IReadOnlyList<ViewerOutlineEntry> outline) =>
        OnOutline.HasDelegate ? OnOutline.InvokeAsync(outline) : Task.CompletedTask;

    protected Task ReportFindCountAsync(int count) =>
        OnFindCount.HasDelegate ? OnFindCount.InvokeAsync(count) : Task.CompletedTask;

    protected Task ReportStatusAsync(string status) =>
        OnStatus.HasDelegate ? OnStatus.InvokeAsync(status) : Task.CompletedTask;

    /// <summary>Friendly message for a load failure.</summary>
    protected static string Describe(Exception ex, string kind) => ex switch
    {
        DocumentViewException dve => dve.Message,
        _ => $"Couldn't open this {kind}: {ex.Message}",
    };

    protected virtual ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await DisposeCoreAsync(); } catch { /* teardown */ }
        _disposed = true;
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (ObjectDisposedException) { }
        }
        _self?.Dispose();
        GC.SuppressFinalize(this);
    }
}
