namespace ChuvadiReader.Core.Reader;

/// <summary>Lightweight viewmodel describing an open document, free of any Chuvadi type.</summary>
public sealed class PdfDocumentVm
{
    public required string FileName { get; init; }

    public required int PageCount { get; init; }

    public bool IsEncrypted { get; init; }

    /// <summary>True when the document is an XFA-based form. The SVG renderer can't
    /// display XFA content (it lives in a separate stream), so the UI shows a notice
    /// instead of a blank page.</summary>
    public bool IsXfaForm { get; init; }
}

/// <summary>
/// One open document. Tabs hold several of these at once, so the reader is no
/// longer a single-document service: each session owns its document handle and
/// its own rendered-page cache. Background tabs keep their handle open (so
/// switching is instant) but can drop their render cache to save memory.
/// </summary>
public interface IPdfSession : IDisposable
{
    PdfDocumentVm Document { get; }

    /// <summary>True if the page's SVG is already rendered and cached (and returns it).</summary>
    bool TryGetRenderedSvg(int pageNumber, out string svg);

    /// <summary>Render a page to selectable-text inline SVG, caching the result.</summary>
    Task<string> RenderPageSvgAsync(int pageNumber, CancellationToken cancellationToken = default);

    /// <summary>Drop the rendered-page cache (keeps the document open). Used for
    /// background tabs so many open PDFs don't pin a lot of memory.</summary>
    void DropRenderCache();
}

/// <summary>
/// The seam between the app and the Chuvadi PDF library — now a factory that
/// opens a document into an independent <see cref="IPdfSession"/>. The UI and the
/// tabs service consume only these interfaces, so the real-vs-placeholder swap
/// stays a one-line DI change.
/// </summary>
public interface IPdfReader
{
    /// <summary>Open a document from a file path into its own session. Opening by
    /// path lets the library read the file efficiently instead of buffering it.</summary>
    Task<IPdfSession> OpenAsync(
        string path,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default,
        bool transparentBackground = false);
}

/// <summary>Synthetic reader used at design time / before the real library is wired.</summary>
public sealed class PlaceholderPdfReader : IPdfReader
{
    public Task<IPdfSession> OpenAsync(string path, string fileName, string? password = null, CancellationToken cancellationToken = default, bool transparentBackground = false)
    {
        var vm = new PdfDocumentVm { FileName = fileName, PageCount = 3, IsEncrypted = false };
        return Task.FromResult<IPdfSession>(new PlaceholderPdfSession(vm));
    }
}

/// <summary>Synthetic session backing <see cref="PlaceholderPdfReader"/>.</summary>
public sealed class PlaceholderPdfSession : IPdfSession
{
    private readonly Dictionary<int, string> _cache = new();

    public PlaceholderPdfSession(PdfDocumentVm document) => Document = document;

    public PdfDocumentVm Document { get; }

    public bool TryGetRenderedSvg(int pageNumber, out string svg) => _cache.TryGetValue(pageNumber, out svg!);

    public Task<string> RenderPageSvgAsync(int pageNumber, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(pageNumber, out var cached))
        {
            return Task.FromResult(cached);
        }

        var svg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 595 842' width='595' height='842'>" +
            "<rect width='595' height='842' fill='#fbf7ec' stroke='#c4b497'/>" +
            "<text x='297' y='421' text-anchor='middle' font-family='IBM Plex Mono, monospace' font-size='18' fill='#8a7c64'>" +
            $"{Document.FileName} · page {pageNumber + 1}</text></svg>";
        _cache[pageNumber] = svg;
        return Task.FromResult(svg);
    }

    public void DropRenderCache() => _cache.Clear();

    public void Dispose() => _cache.Clear();
}
