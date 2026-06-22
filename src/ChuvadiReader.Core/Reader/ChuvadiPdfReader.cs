using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Svg;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// Real reader over the chuvadi-pdf distribution. As a factory it opens a file
/// into an independent <see cref="ChuvadiPdfSession"/>; several can be open at
/// once (one per tab), each holding its own document handle and page cache.
/// </summary>
public sealed class ChuvadiPdfReader : IPdfReader
{
    public async Task<IPdfSession> OpenAsync(
        string path,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default,
        bool transparentBackground = false)
    {
        // Open straight from the path with the library's async open, so it can read
        // the file efficiently instead of us buffering the whole thing into memory.
        var document = string.IsNullOrEmpty(password)
            ? await PdfDocument.OpenAsync(path, cancellationToken).ConfigureAwait(false)
            : await PdfDocument.OpenAsync(path, password!, cancellationToken).ConfigureAwait(false);

        var vm = new PdfDocumentVm
        {
            FileName = fileName,
            PageCount = document.PageCount,
            IsEncrypted = !string.IsNullOrEmpty(password),
            IsXfaForm = IsXfaForm(document),
        };

        return new ChuvadiPdfSession(document, vm, transparentBackground);
    }

    /// <summary>True only for XFA documents that <em>cannot</em> be displayed — i.e. pure
    /// dynamic XFA whose content lives solely in the form layer, so the page renders blank.
    /// Hybrid/static XFA (an /XFA entry alongside real page content) renders fine and is NOT
    /// flagged: we probe-render the first page and only flag when it has no drawable content.</summary>
    private static bool IsXfaForm(PdfDocument document)
    {
        try
        {
            var catalog = document.Catalog;
            if (catalog is null)
            {
                return false;
            }

            var acroForm = document.Objects.ResolveDictionaryEntry<PdfDictionary>(catalog, PdfName.Intern("AcroForm"));
            var hasXfa = acroForm is not null && acroForm.ContainsKey(PdfName.Intern("XFA"));
            if (!hasXfa)
            {
                return false;
            }

            // Hybrid XFA renders normally; only block when the first page is truly empty.
            return document.PageCount == 0 || !PageHasDrawableContent(document, 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Renders one page and reports whether it produced any drawable content
    /// (text, vector paths, or images) as opposed to a blank/background-only page.</summary>
    private static bool PageHasDrawableContent(PdfDocument document, int pageIndex)
    {
        try
        {
            var svg = new SvgRenderer(new SvgExportOptions { TextStrategy = SvgTextStrategy.Selectable })
                .RenderPage(document, pageIndex);
            if (string.IsNullOrEmpty(svg))
            {
                return false;
            }

            return svg.Contains("<text", StringComparison.Ordinal)
                || svg.Contains("<image", StringComparison.Ordinal)
                || svg.Contains("<path", StringComparison.Ordinal);
        }
        catch
        {
            // If we cannot render it at all, treat it as undisplayable (keep the notice).
            return false;
        }
    }
}

/// <summary>
/// One open document over chuvadi-pdf: renders each page to selectable-text SVG
/// via <see cref="SvgRenderer"/> and caches the result. Disposing closes the
/// underlying document handle.
/// </summary>
public sealed class ChuvadiPdfSession : IPdfSession
{
    private readonly SvgRenderer _svgRenderer;
    private readonly Dictionary<int, string> _svgCache = new();
    private PdfDocument? _document;

    public ChuvadiPdfSession(PdfDocument document, PdfDocumentVm vm, bool transparentBackground = false)
    {
        _document = document;
        Document = vm;
        // The library now defaults the SVG page background to opaque white. The reader
        // opts out (null) so a CSS page tint can show through; the bench/export keep white.
        _svgRenderer = new SvgRenderer(new SvgExportOptions
        {
            TextStrategy = SvgTextStrategy.Selectable,
            Background = transparentBackground ? (ColorF?)null : ColorF.White,
        });
    }

    public PdfDocumentVm Document { get; }

    public bool TryGetRenderedSvg(int pageNumber, out string svg) => _svgCache.TryGetValue(pageNumber, out svg!);

    public Task<string> RenderPageSvgAsync(int pageNumber, CancellationToken cancellationToken = default)
    {
        if (_document is null)
        {
            return Task.FromResult(string.Empty);
        }

        if (_svgCache.TryGetValue(pageNumber, out var cached))
        {
            return Task.FromResult(cached);
        }

        // SvgRenderer renders at the page's natural size; zoom is applied in the
        // UI (CSS), so there is no scale parameter here.
        var svg = _svgRenderer.RenderPage(_document, pageNumber);
        _svgCache[pageNumber] = svg;
        return Task.FromResult(svg);
    }

    public void DropRenderCache() => _svgCache.Clear();

    public void Dispose()
    {
        _document?.Dispose();
        _document = null;
        _svgCache.Clear();
    }
}
