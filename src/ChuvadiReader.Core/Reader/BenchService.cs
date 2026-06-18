using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Watermark;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// The Bench workspace: a shelf of loaded source documents and a set of independent
/// <see cref="Desk"/>s. Pages are copied from the shelf into desks, arranged, and each
/// desk binds to its own PDF. A single instance is shared across the app so the
/// workspace survives navigating away and back.
/// </summary>
public sealed class BenchService
{
    private readonly IPdfReader _reader;
    private readonly BenchComposer _composer;
    private readonly ChuvadiReader.Core.Documents.ExportService _export;
    private readonly List<BenchSource> _sources = new();
    private readonly List<Desk> _desks = new();
    private readonly HashSet<Guid> _selected = new();
    private Guid? _selectionAnchor;

    public const int MaxDesks = 30;

    public BenchService(IPdfReader reader, BenchComposer composer, ChuvadiReader.Core.Documents.ExportService export)
    {
        _reader = reader;
        _composer = composer;
        _export = export;
    }

    public IReadOnlyList<BenchSource> Sources => _sources;

    public IReadOnlyList<Desk> Desks => _desks;

    public IReadOnlyCollection<Guid> Selected => _selected;

    public int SelectedCount => _selected.Count;

    public bool HasSources => _sources.Count > 0;

    public bool IsEmpty => _desks.All(d => d.IsEmpty);

    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    // ── shelf ─────────────────────────────────────────────────────────────────

    /// <summary>Loads a PDF document onto the shelf. Does not place any pages — those are
    /// dragged into desks. Ensures one desk exists. (Images go straight into a desk via
    /// <see cref="AddImageToDesk"/>; they are not shelf sources.)</summary>
    public async Task AddSourceAsync(string path, string fileName, CancellationToken ct = default)
    {
        var existing = _sources.FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                                                    && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            var session = await _reader.OpenAsync(path, fileName, null, ct).ConfigureAwait(false);
            _sources.Add(new BenchSource
            {
                Index = _sources.Count,
                Path = path,
                FileName = fileName,
                Session = session,
            });
        }

        EnsureDesk();
        Raise();
    }

    public void ToggleSourceCollapsed(int sourceIndex)
    {
        var source = _sources.FirstOrDefault(s => s.Index == sourceIndex);
        if (source is null)
        {
            return;
        }

        source.Collapsed = !source.Collapsed;
        Raise();
    }

    public bool TryGetSourceThumb(int sourceIndex, int pageIndex, out string svg)
    {
        var source = _sources.FirstOrDefault(s => s.Index == sourceIndex);
        if (source is null)
        {
            svg = string.Empty;
            return false;
        }

        return source.Session.TryGetRenderedSvg(pageIndex, out svg);
    }

    public Task<string> RenderSourceThumbAsync(int sourceIndex, int pageIndex, CancellationToken ct = default)
    {
        var source = _sources.First(s => s.Index == sourceIndex);
        return source.Session.RenderPageSvgAsync(pageIndex, ct);
    }

    // ── desks ─────────────────────────────────────────────────────────────────

    public Desk? FindDesk(Guid deskId) => _desks.FirstOrDefault(d => d.Id == deskId);

    public void EnsureDesk()
    {
        if (_desks.Count == 0)
        {
            _desks.Add(new Desk { Name = "Desk 1" });
        }
    }

    public Desk AddDesk()
    {
        if (_desks.Count >= MaxDesks)
        {
            return _desks[^1];
        }

        var n = _desks.Count + 1;
        var desk = new Desk { Name = $"Desk {n}" };
        _desks.Add(desk);
        Raise();
        return desk;
    }

    public bool CanAddDesk => _desks.Count < MaxDesks;

    public void RemoveDesk(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        foreach (var p in desk.Pages)
        {
            _selected.Remove(p.Id);
        }

        _desks.Remove(desk);
        EnsureDesk();
        Raise();
    }

    public void RenameDesk(Guid deskId, string name)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        desk.Name = string.IsNullOrWhiteSpace(name) ? desk.Name : name.Trim();
        desk.NameLocked = true;
        Raise();
    }

    private void AutoNameDesk(Desk desk)
    {
        if (desk.NameLocked)
        {
            return;
        }

        var sources = desk.Pages.Where(p => !p.IsBlank && !p.IsImage).Select(p => p.SourceName).Distinct().ToList();
        if (sources.Count == 1)
        {
            desk.Name = Path.GetFileNameWithoutExtension(sources[0]);
        }
    }

    // ── populate desks ─────────────────────────────────────────────────────────

    public void AddPageToDesk(Guid deskId, int sourceIndex, int originalIndex, int insertIndex = -1)
    {
        var desk = FindDesk(deskId);
        var source = _sources.FirstOrDefault(s => s.Index == sourceIndex);
        if (desk is null || source is null)
        {
            return;
        }

        var page = new BenchPage
        {
            SourcePath = source.Path,
            SourceName = source.FileName,
            SourceIndex = source.Index,
            OriginalIndex = originalIndex,
        };

        Insert(desk, page, insertIndex);
        AutoNameDesk(desk);
        Raise();
    }

    /// <summary>Drops every page of a source into a desk, in order, at one spot
    /// (used when a whole file is dragged from the shelf).</summary>
    public void AddSourceAllToDesk(Guid deskId, int sourceIndex, int insertIndex = -1)
    {
        var desk = FindDesk(deskId);
        var source = _sources.FirstOrDefault(s => s.Index == sourceIndex);
        if (desk is null || source is null)
        {
            return;
        }

        var at = insertIndex;
        for (var i = 0; i < source.PageCount; i++)
        {
            var page = new BenchPage
            {
                SourcePath = source.Path,
                SourceName = source.FileName,
                SourceIndex = source.Index,
                OriginalIndex = i,
            };
            Insert(desk, page, at);
            if (at >= 0)
            {
                at++;
            }
        }

        AutoNameDesk(desk);
        Raise();
    }

    /// <summary>Drops a raw image straight into a desk as an image page. It is not
    /// converted to PDF until Bind.</summary>
    public void AddImageToDesk(Guid deskId, string imagePath, int insertIndex = -1)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var page = new BenchPage
        {
            SourcePath = string.Empty,
            SourceName = Path.GetFileName(imagePath),
            SourceIndex = -1,
            OriginalIndex = -1,
            IsImage = true,
            ImagePath = imagePath,
        };

        Insert(desk, page, insertIndex);
        Raise();
    }

    /// <summary>A data-URI thumbnail for an image page, so it can render as an
    /// &lt;img&gt; without converting to PDF. Returns empty if it can't be read.</summary>
    public string ImageDataUri(BenchPage page)
    {
        if (!page.IsImage || string.IsNullOrWhiteSpace(page.ImagePath) || !File.Exists(page.ImagePath))
        {
            return string.Empty;
        }

        try
        {
            var bytes = File.ReadAllBytes(page.ImagePath);
            var ext = Path.GetExtension(page.ImagePath).TrimStart('.').ToLowerInvariant();
            var mime = ext switch
            {
                "jpg" or "jpeg" => "image/jpeg",
                "png" => "image/png",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "webp" => "image/webp",
                "tif" or "tiff" => "image/tiff",
                _ => "application/octet-stream",
            };
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return string.Empty;
        }
    }

    public void AddBlankToDesk(Guid deskId, int insertIndex = -1, string? bgHex = null)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var page = new BenchPage
        {
            SourcePath = string.Empty,
            SourceName = "Blank",
            SourceIndex = -1,
            OriginalIndex = -1,
            IsBlank = true,
            BackgroundHex = bgHex,
        };

        Insert(desk, page, insertIndex);
        Raise();
    }

    private static void Insert(Desk desk, BenchPage page, int insertIndex)
    {
        if (insertIndex < 0 || insertIndex > desk.Pages.Count)
        {
            desk.Pages.Add(page);
        }
        else
        {
            desk.Pages.Insert(insertIndex, page);
        }
    }

    /// <summary>Moves an existing page to a position in a (possibly different) desk.</summary>
    public void MovePage(Guid pageId, Guid toDeskId, int insertIndex)
    {
        var from = DeskOf(pageId);
        var to = FindDesk(toDeskId);
        if (from is null || to is null)
        {
            return;
        }

        var idx = from.Pages.FindIndex(p => p.Id == pageId);
        if (idx < 0)
        {
            return;
        }

        var page = from.Pages[idx];
        from.Pages.RemoveAt(idx);

        if (ReferenceEquals(from, to) && insertIndex > idx)
        {
            insertIndex--;
        }

        Insert(to, page, insertIndex);
        AutoNameDesk(to);
        Raise();
    }

    public void RemovePage(Guid pageId)
    {
        var desk = DeskOf(pageId);
        if (desk is null)
        {
            return;
        }

        desk.Pages.RemoveAll(p => p.Id == pageId);
        _selected.Remove(pageId);
        Raise();
    }

    public void TurnPage(Guid pageId)
    {
        var desk = DeskOf(pageId);
        var page = desk?.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null)
        {
            return;
        }

        page.Rotation = (page.Rotation + 90) % 360;
        Raise();
    }

    public Desk? DeskOf(Guid pageId) => _desks.FirstOrDefault(d => d.Pages.Any(p => p.Id == pageId));

    // ── desk-page thumbnails ────────────────────────────────────────────────────

    public bool TryGetThumb(BenchPage page, out string svg)
    {
        if (page.IsBlank)
        {
            svg = string.Empty;
            return false;
        }

        var source = _sources.FirstOrDefault(s => s.Index == page.SourceIndex);
        if (source is null)
        {
            svg = string.Empty;
            return false;
        }

        return source.Session.TryGetRenderedSvg(page.OriginalIndex, out svg);
    }

    public Task<string> RenderThumbAsync(BenchPage page, CancellationToken ct = default)
    {
        var source = _sources.First(s => s.Index == page.SourceIndex);
        return source.Session.RenderPageSvgAsync(page.OriginalIndex, ct);
    }

    // ── selection (global across desks) ─────────────────────────────────────────

    public bool IsSelected(Guid id) => _selected.Contains(id);

    public int SelectedInDesk(Guid deskId)
    {
        var desk = FindDesk(deskId);
        return desk is null ? 0 : desk.Pages.Count(p => _selected.Contains(p.Id));
    }

    public void ToggleSelect(Guid id)
    {
        if (!_selected.Remove(id))
        {
            _selected.Add(id);
        }

        _selectionAnchor = id;
        Raise();
    }

    public void SelectRangeTo(Guid id)
    {
        var desk = DeskOf(id);
        if (_selectionAnchor is not Guid anchor || anchor == id || desk is null)
        {
            ToggleSelect(id);
            return;
        }

        var a = desk.Pages.FindIndex(p => p.Id == anchor);
        var b = desk.Pages.FindIndex(p => p.Id == id);
        if (a < 0 || b < 0)
        {
            ToggleSelect(id);
            return;
        }

        var (lo, hi) = a <= b ? (a, b) : (b, a);
        for (var i = lo; i <= hi; i++)
        {
            _selected.Add(desk.Pages[i].Id);
        }

        Raise();
    }

    public void ClearSelection()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        _selected.Clear();
        _selectionAnchor = null;
        Raise();
    }

    // ── per-desk trim / turn ────────────────────────────────────────────────────

    public void TrimSelected(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var removed = desk.Pages.RemoveAll(p => _selected.Contains(p.Id));
        if (removed == 0)
        {
            return;
        }

        _selected.RemoveWhere(id => !_desks.Any(d => d.Pages.Any(p => p.Id == id)));
        Raise();
    }

    public void TurnSelected(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var targets = desk.Pages.Where(p => _selected.Contains(p.Id)).ToList();
        if (targets.Count == 0)
        {
            targets = desk.Pages.ToList();
        }

        foreach (var page in targets)
        {
            page.Rotation = (page.Rotation + 90) % 360;
        }

        Raise();
    }

    // ── output ──────────────────────────────────────────────────────────────────

    public string SuggestName(Guid deskId)
    {
        var desk = FindDesk(deskId);
        return desk is null ? "binding" : SafeName(desk.Name);
    }

    private static string SafeName(string name)
    {
        var safe = new string(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "binding" : safe.Trim();
    }

    public async Task BindDeskAsync(Guid deskId, string outputPath, CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.IsEmpty)
        {
            return;
        }

        var bytes = await _composer.ComposeAsync(desk.Pages, ct).ConfigureAwait(false);
        bytes = ApplyWatermark(bytes, desk, desk.Pages, allComposed: true);
        bytes = ApplyHeaderFooter(bytes, desk, desk.Pages);
        await File.WriteAllBytesAsync(outputPath, bytes, ct).ConfigureAwait(false);
    }

    public async Task LiftDeskAsync(Guid deskId, string outputPath, CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var selected = desk.Pages.Where(p => _selected.Contains(p.Id)).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var bytes = await _composer.ComposeAsync(selected, ct).ConfigureAwait(false);
        bytes = ApplyWatermark(bytes, desk, selected, allComposed: true);
        bytes = ApplyHeaderFooter(bytes, desk, selected);
        await File.WriteAllBytesAsync(outputPath, bytes, ct).ConfigureAwait(false);
    }

    /// <summary>Binds every non-empty desk to <paramref name="folder"/>, one PDF per desk.
    /// Returns the number of files written.</summary>
    public async Task<int> BindAllAsync(string folder, CancellationToken ct = default)
    {
        var written = 0;
        foreach (var desk in _desks.Where(d => !d.IsEmpty))
        {
            ct.ThrowIfCancellationRequested();
            var outPath = ChuvadiReader.Core.Documents.ExportService.UniquePath(folder, SafeName(desk.Name), "pdf");
            await BindDeskAsync(desk.Id, outPath, ct).ConfigureAwait(false);
            written++;
        }

        return written;
    }

    public async Task<int> ScatterDeskAsync(Guid deskId, string folder, CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.IsEmpty)
        {
            return 0;
        }

        var width = desk.Pages.Count.ToString().Length;
        for (var i = 0; i < desk.Pages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = desk.Pages[i];
            var baseName = page.IsBlank ? "blank" : Path.GetFileNameWithoutExtension(page.SourceName);
            var fileBase = $"{(i + 1).ToString().PadLeft(width, '0')}_{baseName}";
            var outPath = ChuvadiReader.Core.Documents.ExportService.UniquePath(folder, fileBase, "pdf");
            await _composer.ComposeToFileAsync(new[] { page }, outPath, ct).ConfigureAwait(false);
        }

        return desk.Pages.Count;
    }

    public async Task<int> ExportDeskAsync(
        Guid deskId,
        string folder,
        ChuvadiReader.Core.Documents.ExportFormat format,
        int dpi,
        bool onlySelected,
        CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return 0;
        }

        var pages = (onlySelected && SelectedInDesk(deskId) > 0)
            ? desk.Pages.Where(p => _selected.Contains(p.Id)).ToList()
            : desk.Pages.ToList();
        if (pages.Count == 0)
        {
            return 0;
        }

        var tmp = Path.Combine(Path.GetTempPath(), $"chuvadi-bench-{Guid.NewGuid():N}.pdf");
        try
        {
            await _composer.ComposeToFileAsync(pages, tmp, ct).ConfigureAwait(false);
            return await _export.ExportAsync(tmp, folder, format, allPages: true,
                currentPageIndex: 0, dpi: dpi, baseNameOverride: SafeName(desk.Name), ct: ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(tmp); }
            catch { }
        }
    }

    // ── watermark ────────────────────────────────────────────────────────────────

    public void SetDeskWatermark(Guid deskId, string text, string fontFamily, bool bold, bool italic,
        double fontSize, string colorHex, double opacity, double rotationDegrees, bool allPages)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            desk.Watermark = null;
        }
        else
        {
            desk.Watermark = new DeskWatermark
            {
                Text = text.Trim(),
                FontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Helvetica" : fontFamily,
                Bold = bold,
                Italic = italic,
                FontSize = Math.Clamp(fontSize, 6, 400),
                ColorHex = string.IsNullOrWhiteSpace(colorHex) ? "#808080" : colorHex,
                Opacity = Math.Clamp(opacity, 0.05, 1.0),
                RotationDegrees = rotationDegrees,
                AllPages = allPages,
            };
        }

        Raise();
    }

    private static Chuvadi.Pdf.Graphics.ColorF HexToColorF(string? hex)
    {
        var s = (hex ?? "#808080").TrimStart('#');
        if (s.Length == 3)
        {
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        }
        byte r = 0x80, g = 0x80, b = 0x80;
        if (s.Length >= 6
            && byte.TryParse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var rr)
            && byte.TryParse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var gg)
            && byte.TryParse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var bb))
        {
            r = rr; g = gg; b = bb;
        }
        return Chuvadi.Pdf.Graphics.ColorF.FromRgb8(r, g, b, 255);
    }

    private byte[] ApplyWatermark(byte[] bytes, Desk desk, IReadOnlyList<BenchPage> composed, bool allComposed)
    {
        var wm = desk.Watermark;
        if (wm is null || string.IsNullOrWhiteSpace(wm.Text))
        {
            return bytes;
        }

        int[]? indices = null;
        if (!wm.AllPages)
        {
            indices = composed
                .Select((p, i) => (p, i))
                .Where(t => _selected.Contains(t.p.Id))
                .Select(t => t.i)
                .ToArray();
            if (indices.Length == 0)
            {
                return bytes; // nothing selected to stamp
            }
        }

        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(temp, bytes);
            using var doc = PdfDocument.Open(temp);
            using var ms = new MemoryStream();
            var pageIdx = indices ?? Enumerable.Range(0, doc.PageCount).ToArray();
            var opts = new TextWatermarkOptions(wm.Text)
            {
                FontName = wm.ResolveFontName(),
                FontSize = wm.FontSize,
                Opacity = (float)wm.Opacity,
                RotationDegrees = wm.RotationDegrees,
                Color = HexToColorF(wm.ColorHex),
                PageIndices = pageIdx,
            };

            WatermarkStamper.ApplyText(ms, doc, opts);
            return ms.ToArray();
        }
        finally
        {
            try { File.Delete(temp); }
            catch { }
        }
    }

    // ── header / footer ────────────────────────────────────────────────────────

    public void SetDeskHeaderFooter(Guid deskId, DeskHeaderFooter? config)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        desk.HeaderFooter = (config is not null && config.HasAnyContent) ? config : null;
        Raise();
    }

    private byte[] ApplyHeaderFooter(byte[] bytes, Desk desk, IReadOnlyList<BenchPage> composed)
    {
        var hf = desk.HeaderFooter;
        if (hf is null || !hf.HasAnyContent)
        {
            return bytes;
        }

        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(temp, bytes);
            using var doc = PdfDocument.Open(temp);
            using var ms = new MemoryStream();

            // Resolve the page range (1-based inclusive; ToPage 0 = last).
            int last = doc.PageCount;
            int[] pageIdx;
            if (hf.AllPages)
            {
                pageIdx = Enumerable.Range(0, last).ToArray();
            }
            else
            {
                int from = Math.Clamp(hf.FromPage <= 0 ? 1 : hf.FromPage, 1, last);
                int to = hf.ToPage <= 0 ? last : Math.Clamp(hf.ToPage, 1, last);
                if (to < from)
                {
                    (from, to) = (to, from);
                }
                pageIdx = Enumerable.Range(from - 1, to - from + 1).ToArray();
            }
            if (pageIdx.Length == 0)
            {
                return bytes;
            }

            var header = hf.HeaderHasText
                ? new BandText(hf.HeaderLeft ?? "", hf.HeaderCenter ?? "", hf.HeaderRight ?? "")
                : null;
            var footer = hf.FooterHasText
                ? new BandText(hf.FooterLeft ?? "", hf.FooterCenter ?? "", hf.FooterRight ?? "")
                : null;

            var opts = new HeaderFooterOptions
            {
                Header = header,
                Footer = footer,
                FontSize = hf.FontSize,
                Color = HexToColorF(hf.ColorHex),
                Background = hf.BackgroundEnabled ? HexToColorF(hf.BackgroundHex) : null,
                MarginX = hf.MarginX,
                HeaderHeight = hf.BandHeight,
                FooterHeight = hf.BandHeight,
                Fit = hf.Fit switch
                {
                    "reserve" => PageContentFit.ReserveAndScale,
                    "reserveIfIntruding" => PageContentFit.ScaleIfIntruding,
                    _ => PageContentFit.Overlay,
                },
                PageIndices = pageIdx,
                FilePath = string.IsNullOrWhiteSpace(desk.Name) ? "document.pdf" : desk.Name + ".pdf",
                Timestamp = DateTimeOffset.Now,
            };

            HeaderFooter.Apply(ms, doc, opts);
            return ms.ToArray();
        }
        catch
        {
            return bytes; // never let a footer failure block the bind
        }
        finally
        {
            try { File.Delete(temp); }
            catch { }
        }
    }

    // ── reset ──────────────────────────────────────────────────────────────────

    public void Reset()
    {
        foreach (var source in _sources)
        {
            source.Session.Dispose();
        }

        _sources.Clear();
        _desks.Clear();
        _selected.Clear();
        _selectionAnchor = null;
        EnsureDesk();
        Raise();
    }
}
