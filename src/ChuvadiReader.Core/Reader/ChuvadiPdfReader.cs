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

    /// <summary>True when the catalog's AcroForm carries an /XFA entry. XFA content
    /// isn't drawn by the SVG renderer, so such documents would otherwise open blank.</summary>
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
            return acroForm is not null && acroForm.ContainsKey(PdfName.Intern("XFA"));
        }
        catch
        {
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
