using System.Globalization;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;
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
    // A page whose area exceeds this (≈ larger than A2, i.e. CAD/large-format sheets)
    // is displayed as a rasterised image rather than SVG: dense vector drawings emit
    // tens of MB of SVG with 100k+ <path> nodes that no WebView can hold.
    private const double LargeFormatAreaPt = 1_500_000d;
    // A normal-size page whose SVG still comes out this large also falls back to raster.
    private const int HeavySvgBytes = 6_000_000;
    private const double RasterDpi = 150d;

    private readonly bool _transparentBackground;
    private SvgRenderer _svgRenderer;
    private readonly Dictionary<int, string> _svgCache = new();
    private PdfDocument? _document;

    // Layers: the file's groups (read once, lazily) and this view's overrides (name → visible).
    // Overrides only ever reach the renderers — the document itself is never modified.
    private List<LayerEntry>? _layerEntries;
    private readonly Dictionary<string, bool> _layerOverrides = new(StringComparer.Ordinal);
    private int _renderGeneration;

    public ChuvadiPdfSession(PdfDocument document, PdfDocumentVm vm, bool transparentBackground = false)
    {
        _document = document;
        Document = vm;
        _transparentBackground = transparentBackground;
        _svgRenderer = CreateSvgRenderer();
    }

    public PdfDocumentVm Document { get; }

    /// <summary>The open library document, for Core services that work on the same handle
    /// (e.g. <see cref="MeasureService"/>). Null once disposed.</summary>
    internal PdfDocument? PdfDocument => _document;

    public event EventHandler? RenderInvalidated;

    public int RenderGeneration => _renderGeneration;

    public bool CanToggleLayers => _document is not null && LayerEntries.Count > 0;

    public IReadOnlyList<PdfLayerInfo> Layers =>
        LayerEntries.Select(e => new PdfLayerInfo(
                e.Name, e.Name, e.DefaultVisible, IsLayerShown(e), e.Locked, e.Intents, e.Count))
            .ToList();

    public void SetLayerVisible(string layerId, bool visible)
    {
        var entry = LayerEntries.FirstOrDefault(e => string.Equals(e.Name, layerId, StringComparison.Ordinal));
        if (entry is null || entry.Locked || IsLayerShown(entry) == visible)
        {
            return;
        }

        // Store only real deviations from the file's default, so "back to default" leaves no override.
        if (visible == entry.DefaultVisible)
        {
            _layerOverrides.Remove(entry.Name);
        }
        else
        {
            _layerOverrides[entry.Name] = visible;
        }

        InvalidateRender();
    }

    public void ResetLayerVisibility()
    {
        if (_layerOverrides.Count == 0)
        {
            return;
        }

        _layerOverrides.Clear();
        InvalidateRender();
    }

    private bool IsLayerShown(LayerEntry e) =>
        _layerOverrides.TryGetValue(e.Name, out var v) ? v : e.DefaultVisible;

    private void InvalidateRender()
    {
        _svgRenderer = CreateSvgRenderer();
        _svgCache.Clear();
        _renderGeneration++;
        RenderInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyDictionary<string, bool>? CurrentOverrides =>
        _layerOverrides.Count == 0 ? null : new Dictionary<string, bool>(_layerOverrides, StringComparer.Ordinal);

    private SvgRenderer CreateSvgRenderer() =>
        // The library now defaults the SVG page background to opaque white. The reader
        // opts out (null) so a CSS page tint can show through; the bench/export keep white.
        new(new SvgExportOptions
        {
            TextStrategy = SvgTextStrategy.Selectable,
            Background = _transparentBackground ? (ColorF?)null : ColorF.White,
            LayerVisibility = CurrentOverrides,
        });

    private List<LayerEntry> LayerEntries => _layerEntries ??= ReadLayers(_document);

    private sealed record LayerEntry(string Name, bool DefaultVisible, bool Locked, IReadOnlyList<string> Intents, int Count);

    /// <summary>Reads the layers once: names/default state/intents from the library's
    /// <see cref="OptionalContentReader"/>, plus the /D /Locked array (which the library
    /// doesn't surface) straight from /OCProperties. Never throws — a damaged
    /// /OCProperties just yields no layers.</summary>
    private static List<LayerEntry> ReadLayers(PdfDocument? document)
    {
        var result = new List<LayerEntry>();
        if (document is null)
        {
            return result;
        }

        try
        {
            var groups = OptionalContentReader.GetGroups(document);
            if (groups.Count == 0)
            {
                return result;
            }

            var locked = ReadLockedLayerNames(document);
            foreach (var g in groups.GroupBy(g => g.Name ?? string.Empty, StringComparer.Ordinal))
            {
                var first = g.First();
                result.Add(new LayerEntry(
                    g.Key,
                    first.IsVisibleByDefault,
                    locked.Contains(g.Key),
                    first.Intents ?? Array.Empty<string>(),
                    g.Count()));
            }
        }
        catch
        {
            result.Clear();
        }

        return result;
    }

    private static HashSet<string> ReadLockedLayerNames(PdfDocument document)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var objects = document.Objects;
            var ocProps = objects.ResolveDictionaryEntry<PdfDictionary>(document.Catalog, PdfName.Intern("OCProperties"));
            var d = ocProps is null ? null : objects.ResolveDictionaryEntry<PdfDictionary>(ocProps, PdfName.Intern("D"));
            var lockedArr = d is null ? null : objects.ResolveDictionaryEntry<PdfArray>(d, PdfName.Intern("Locked"));
            if (lockedArr is null)
            {
                return names;
            }

            foreach (var item in lockedArr)
            {
                if (objects.Resolve(item) is PdfDictionary ocg
                    && objects.ResolveDictionaryEntry<PdfString>(ocg, PdfName.Intern("Name")) is { } name)
                {
                    names.Add(name.ToTextString());
                }
            }
        }
        catch
        {
            // Locked is advisory; failing to read it just means nothing is locked.
        }

        return names;
    }

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

        var page = _document.Pages[pageNumber];
        string svg;

        // Large-format sheets (CAD/architectural) are rasterised up front: their vector
        // SVG is too dense for the WebView. Everything else renders as selectable-text SVG,
        // with a safety fallback if a normal-size page's SVG is unexpectedly huge.
        if (page.Width * page.Height > LargeFormatAreaPt)
        {
            svg = RenderPageAsRasterSvg(page);
        }
        else
        {
            // SvgRenderer renders at the page's natural size; zoom is applied in the
            // UI (CSS), so there is no scale parameter here.
            svg = _svgRenderer.RenderPage(_document, pageNumber);
            if (svg.Length > HeavySvgBytes)
            {
                svg = RenderPageAsRasterSvg(page);
            }
        }

        _svgCache[pageNumber] = svg;
        return Task.FromResult(svg);
    }

    /// <summary>Renders a page to a raster image (via the library rasteriser + PNG encoder)
    /// and wraps it in a minimal SVG carrying a single &lt;image&gt;, so the UI's SVG display
    /// path is unchanged while the WebView only has to hold one image node instead of
    /// hundreds of thousands of vector paths.</summary>
    private string RenderPageAsRasterSvg(PdfPage page)
    {
        var buffer = new PageRasterizer(
                _document!.Objects,
                new RenderOptions
                {
                    Dpi = RasterDpi,
                    Background = ColorF.White,
                    AntiAlias = true,
                    LayerVisibility = CurrentOverrides,
                })
            .Rasterize(page);

        using var ms = new MemoryStream();
        PngEncoder.Encode(new ImageFrame(buffer, ImageColorFormat.Rgba32), ms, false);
        var dataUri = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());

        string w = page.Width.ToString("0.###", CultureInfo.InvariantCulture);
        string h = page.Height.ToString("0.###", CultureInfo.InvariantCulture);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}pt\" height=\"{h}pt\" " +
               $"viewBox=\"0 0 {w} {h}\" preserveAspectRatio=\"xMidYMid meet\">" +
               $"<image x=\"0\" y=\"0\" width=\"{w}\" height=\"{h}\" preserveAspectRatio=\"none\" " +
               $"href=\"{dataUri}\"/></svg>";
    }

    public void DropRenderCache() => _svgCache.Clear();

    public void Dispose()
    {
        _document?.Dispose();
        _document = null;
        _svgCache.Clear();
    }
}
