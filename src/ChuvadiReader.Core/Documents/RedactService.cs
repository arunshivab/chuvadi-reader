using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Redaction;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Text;

namespace ChuvadiReader.Core.Documents;

/// <summary>Thrown when a saved redaction still leaves extractable text inside the
/// redaction rectangles (so the file would not be securely redacted).</summary>
public sealed class RedactionNotRemovedException : Exception
{
    public RedactionNotRemovedException(string message) : base(message) { }
}

/// <summary>A box in normalised page fractions (0–1), measured on the page as it is
/// shown on screen (top-left origin, x right, y down).</summary>
public readonly record struct NormBox(double X, double Y, double W, double H);

/// <summary>The redactions for one page, plus the on-screen view rotation the boxes
/// were drawn under (0/90/180/270, clockwise). The page's own /Rotate is added on top
/// when mapping, so boxes land in the page's content coordinates either way.</summary>
public sealed record PageRedaction(int ViewRotation, IReadOnlyList<NormBox> Boxes);

/// <summary>A single overlay text box, normalised to the displayed page (top-left origin).
/// Rotation is in degrees (clockwise on screen). Font is a Standard-14 family name
/// ("Helvetica" | "Times" | "Courier"); bold/italic pick the variant.</summary>
public sealed record TextAnno(
    double X, double Y, double W, double H,
    string Text, string FontFamily, bool Bold, bool Italic,
    double FontSizePt, string ColorHex, string Align, double Rotation, double LineHeight);

/// <summary>The text overlays for one page, plus that page's on-screen view rotation.</summary>
public sealed record PageText(int ViewRotation, IReadOnlyList<TextAnno> Items);

/// <summary>A single overlay image, normalised to the displayed page (top-left origin).
/// <paramref name="ImageBytes"/> are the encoded image bytes (PNG/JPEG). Rotation is in
/// degrees (clockwise on screen). Opacity is 0–1; the underlying library does not yet
/// honour image alpha, so values below 1 are recorded but currently stamp at full opacity
/// (a library-side capability is needed — see ROADMAP).</summary>
public sealed record ImageAnno(
    double X, double Y, double W, double H,
    byte[] ImageBytes, double Rotation, double Opacity,
    bool BehindContent = false);

/// <summary>The image overlays for one page, plus that page's on-screen view rotation.</summary>
public sealed record PageImage(int ViewRotation, IReadOnlyList<ImageAnno> Items);

/// <summary>The kind of vector shape an overlay draws.</summary>
public enum ShapeKind
{
    Rectangle,
    Line,
    Ellipse,
    Arrow,
    Freehand,
}

/// <summary>A single overlay shape, normalised to the displayed page (top-left origin).
/// For <see cref="ShapeKind.Rectangle"/> the box is the rectangle bounds. For
/// <see cref="ShapeKind.Line"/> the line runs from the box's top-left corner to its
/// bottom-right corner (so the box is the line's bounding span). <paramref name="FillHex"/>
/// is null/empty for no fill (rectangles only). Rotation is in degrees (clockwise on
/// screen).</summary>
public sealed record ShapeAnno(
    double X, double Y, double W, double H,
    ShapeKind Kind, string? FillHex, string StrokeHex, double StrokeWidthPt, double Rotation,
    bool BehindContent = false,
    IReadOnlyList<(double X, double Y)>? Points = null);

/// <summary>The shape overlays for one page, plus that page's on-screen view rotation.</summary>
public sealed record PageShape(int ViewRotation, IReadOnlyList<ShapeAnno> Items);

/// <summary>
/// Flattens reader markup into a NEW pdf: redactions (true byte-level removal via the
/// library's <see cref="Redactor"/>, verified afterwards) and overlays — shapes, images and
/// text — authored and stamped over the page. The source file is only ever read; output is
/// always a separate path chosen by the user, so the reader stays read-only.
/// </summary>
public sealed class RedactService
{
    /// <summary>Redaction-only convenience wrapper.</summary>
    public Task RedactToFileAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<int, PageRedaction> pages,
        string overlayHex = "#000000",
        CancellationToken ct = default)
        => FlattenToFileAsync(sourcePath, outputPath, pages, overlayHex,
            new Dictionary<int, PageText>(),
            new Dictionary<int, PageImage>(),
            new Dictionary<int, PageShape>(), ct);

    /// <summary>Back-compatible overload: redactions + text only (no images or shapes).</summary>
    public Task FlattenToFileAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<int, PageRedaction> redactions,
        string overlayHex,
        IReadOnlyDictionary<int, PageText> texts,
        CancellationToken ct = default)
        => FlattenToFileAsync(sourcePath, outputPath, redactions, overlayHex, texts,
            new Dictionary<int, PageImage>(),
            new Dictionary<int, PageShape>(), ct);

    /// <summary>Apply redactions (if any) then overlays and write the result to
    /// <paramref name="outputPath"/> (a different file). Order is: redactions flattened
    /// first (true removal), then shapes, then images, then text stamped on top — so text
    /// always reads above any image or shape it shares space with.</summary>
    public async Task FlattenToFileAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<int, PageRedaction> redactions,
        string overlayHex,
        IReadOnlyDictionary<int, PageText> texts,
        IReadOnlyDictionary<int, PageImage> images,
        IReadOnlyDictionary<int, PageShape> shapes,
        CancellationToken ct = default)
    {
        var hasRedactions = redactions.Any(kv => kv.Value.Boxes.Count > 0);
        var hasText = texts.Any(kv => kv.Value.Items.Count > 0);
        var hasImages = images.Any(kv => kv.Value.Items.Count > 0);
        var hasShapes = shapes.Any(kv => kv.Value.Items.Count > 0);
        if (!hasRedactions && !hasText && !hasImages && !hasShapes)
        {
            throw new InvalidOperationException("Nothing to save — no redactions, shapes, images or text.");
        }

        await Task.Run(() =>
        {
            byte[] current = File.ReadAllBytes(sourcePath);
            var mappedRects = new Dictionary<int, List<RectangleF>>();

            // 1) Redactions — true removal.
            if (hasRedactions)
            {
                using var doc = PdfDocument.Open(new MemoryStream(current, writable: false));
                var opts = new RedactionOptions { DrawOverlay = true, OverlayColor = ParseColor(overlayHex) };

                foreach (var (pageIndex, page) in redactions)
                {
                    if (page.Boxes.Count == 0 || pageIndex < 0 || pageIndex >= doc.PageCount) continue;
                    var pdfPage = doc.Pages[pageIndex];
                    var rot = NormalizeRotation(pdfPage.Rotate + page.ViewRotation);
                    var rects = new List<RectangleF>();
                    foreach (var box in page.Boxes)
                    {
                        // chuvadi-pdf 3.14.1: RedactionRect.Bounds is BOTTOM-LEFT / y-up — the same
                        // frame the verify guard uses to compare against TextExtractor fragments — so
                        // the apply rect and the verify rect are now one and the same.
                        var r = MapToPdf(box, rot, pdfPage.Width, pdfPage.Height);
                        opts.Rectangles.Add(new RedactionRect(pageIndex, r));
                        rects.Add(r);
                    }
                    mappedRects[pageIndex] = rects;
                }

                using var ms = new MemoryStream();
                Redactor.Apply(ms, doc, opts);
                current = ms.ToArray();
            }

            // 2) Verify removal actually happened (blocks tagged-PDF leaks).
            if (mappedRects.Count > 0 && CountSurvivingText(current, mappedRects) > 0)
            {
                throw new RedactionNotRemovedException(
                    "Redaction could not fully remove text from this file — it was NOT saved. " +
                    "This PDF is tagged, and the underlying library can't yet strip text from " +
                    "tagged content. A library-side fix is needed before redaction is secure here.");
            }

            // 3) Shapes + images — drawn together onto ONE overlay per page and stamped once.
            // The library's PageStamper.Place cannot composite multiple overlays (a second
            // stamp drops the first), so every shape and image for a page must share a single
            // overlay. Shapes are drawn first, then images, so an image sits above a shape it
            // overlaps; text (its own later pass) sits above both.
            if (hasShapes || hasImages)
            {
                var pageIndices = new SortedSet<int>();
                foreach (var k in shapes.Keys) pageIndices.Add(k);
                foreach (var k in images.Keys) pageIndices.Add(k);

                foreach (var pageIndex in pageIndices)
                {
                    shapes.TryGetValue(pageIndex, out var pageShapes);
                    images.TryGetValue(pageIndex, out var pageImages);
                    var shapeItems = pageShapes?.Items ?? (IReadOnlyList<ShapeAnno>)Array.Empty<ShapeAnno>();
                    var imageItems = pageImages?.Items ?? (IReadOnlyList<ImageAnno>)Array.Empty<ImageAnno>();
                    if (shapeItems.Count == 0 && imageItems.Count == 0) continue;

                    // View rotation is the same for every item on a page; take whichever exists.
                    int viewRotation = pageShapes?.ViewRotation ?? pageImages?.ViewRotation ?? 0;
                    current = StampPageOverlay(current, pageIndex, viewRotation, shapeItems, imageItems);
                }
            }

            // 5) Text overlays — one full-page overlay per box, stamped on top.
            if (hasText)
            {
                foreach (var (pageIndex, page) in texts)
                {
                    foreach (var item in page.Items)
                    {
                        if (string.IsNullOrEmpty(item.Text)) continue;
                        current = StampText(current, pageIndex, page.ViewRotation, item);
                    }
                }
            }

            File.WriteAllBytes(outputPath, current);
        }, ct).ConfigureAwait(false);
    }

    private static byte[] StampText(byte[] source, int pageIndex, int viewRotation, TextAnno item)
    {
        using var target = PdfDocument.Open(new MemoryStream(source, writable: false));
        if (pageIndex < 0 || pageIndex >= target.PageCount) return source;

        var pdfPage = target.Pages[pageIndex];
        double w = pdfPage.Width, h = pdfPage.Height;
        var rot = NormalizeRotation(pdfPage.Rotate + viewRotation);

        // Box rect in y-up content space — used as the rotation centre for the stamp transform.
        var rect = MapToPdf(new NormBox(item.X, item.Y, item.W, item.H), rot, w, h);

        var overlay = PdfDocumentBuilder.Create();
        var pb = overlay.AddPage(new PageSize(w, h));
        var font = ResolveFont(item.FontFamily, item.Bold, item.Italic);
        var color = SafeColor(item.ColorHex);
        var align = ParseAlign(item.Align);
        var lineHeight = item.LineHeight <= 0 ? 1.2 : item.LineHeight;
        // DrawTextBlock uses the same TOP-LEFT / y-down frame as the other authoring primitives:
        // its y is the block's TOP edge measured from the page top. Passing the y-up top edge
        // (rect.Y + rect.Height) vertically MIRRORS the text, so convert to the top-down top edge.
        double yTop = h - (rect.Y + rect.Height);

        pb.DrawTextBlock(item.Text, rect.X, yTop, rect.Width, rect.Height, font, item.FontSizePt, color, align, lineHeight);

        using var overlayDoc = PdfDocument.Open(new MemoryStream(overlay.ToByteArray(), writable: false));

        // Effective rotation = the text's own angle plus the page's view rotation so the
        // text reads upright relative to how the page was shown. Negated for PDF's CCW,
        // y-up space vs the screen's CW, y-down. Rotate about the box centre.
        double eff = item.Rotation + rot;
        var t = RotateAboutCentre(eff, rect);

        using var ms = new MemoryStream();
        PageStamper.Place(ms, target, pageIndex, overlayDoc, 0, t, StampPlacement.Overlay);
        return ms.ToArray();
    }

    /// <summary>Draws every shape and image for one page onto a single overlay page and stamps
    /// it once (identity transform). This is required because the library's PageStamper cannot
    /// composite multiple overlays. Shapes are drawn first, then images, so images sit above
    /// overlapping shapes. All authoring draw calls use the TOP-LEFT, y-DOWN coordinate system.
    /// Lines carry their angle directly through their two mapped endpoints; filled rectangles and
    /// images are axis-aligned (per-item rotation needs a library-side transform/CTM — ROADMAP).</summary>
    /// <summary>Stamps every shape and image for one page. Each item is stamped as its own
    /// single-item overlay (chained onto the running bytes) so per-item rotation and
    /// behind/over placement apply independently. Shapes are drawn first, then images, so an
    /// image sits above a shape it overlaps; text (its own later pass) sits above both.</summary>
    private static byte[] StampPageOverlay(
        byte[] source, int pageIndex, int viewRotation,
        IReadOnlyList<ShapeAnno> shapes, IReadOnlyList<ImageAnno> imageItems)
    {
        byte[] current = source;
        foreach (var item in shapes) current = StampOneShape(current, pageIndex, viewRotation, item);
        foreach (var item in imageItems) current = StampOneImage(current, pageIndex, viewRotation, item);
        return current;
    }

    /// <summary>Stamps a single shape onto its own overlay, rotated about its centre by the item's
    /// rotation and placed over or behind the page content. Identity transform when not rotated, so
    /// an un-rotated overlay shape lands exactly where it did before.</summary>
    private static byte[] StampOneShape(byte[] source, int pageIndex, int viewRotation, ShapeAnno item)
    {
        using var target = PdfDocument.Open(new MemoryStream(source, writable: false));
        if (pageIndex < 0 || pageIndex >= target.PageCount) return source;
        var pdfPage = target.Pages[pageIndex];
        double w = pdfPage.Width, h = pdfPage.Height;
        var rot = NormalizeRotation(pdfPage.Rotate + viewRotation);

        var overlay = PdfDocumentBuilder.Create();
        var pb = overlay.AddPage(new PageSize(w, h));
        DrawShape(pb, item, rot, w, h);

        using var overlayDoc = PdfDocument.Open(new MemoryStream(overlay.ToByteArray(), writable: false));
        var rectYUp = MapToPdf(new NormBox(item.X, item.Y, item.W, item.H), rot, w, h);
        var t = RotateAboutCentre(item.Rotation, rectYUp);
        var placement = item.BehindContent ? StampPlacement.Underlay : StampPlacement.Overlay;
        using var ms = new MemoryStream();
        PageStamper.Place(ms, target, pageIndex, overlayDoc, 0, t, placement);
        return ms.ToArray();
    }

    /// <summary>Stamps a single image onto its own overlay with its opacity, rotated about its
    /// centre, over or behind the page content.</summary>
    private static byte[] StampOneImage(byte[] source, int pageIndex, int viewRotation, ImageAnno item)
    {
        if (item.ImageBytes is null || item.ImageBytes.Length == 0) return source;
        using var target = PdfDocument.Open(new MemoryStream(source, writable: false));
        if (pageIndex < 0 || pageIndex >= target.PageCount) return source;
        var pdfPage = target.Pages[pageIndex];
        double w = pdfPage.Width, h = pdfPage.Height;
        var rot = NormalizeRotation(pdfPage.Rotate + viewRotation);
        var rect = MapToPdfTopLeft(new NormBox(item.X, item.Y, item.W, item.H), rot, w, h);

        var overlay = PdfDocumentBuilder.Create();
        var pb = overlay.AddPage(new PageSize(w, h));
        // Opacity 0–1; treat a non-positive value as "unset" → fully opaque.
        double op = item.Opacity <= 0 ? 1.0 : Math.Clamp(item.Opacity, 0.0, 1.0);
        pb.DrawImage(item.ImageBytes, rect.X, rect.Y, rect.Width, rect.Height, op);

        using var overlayDoc = PdfDocument.Open(new MemoryStream(overlay.ToByteArray(), writable: false));
        var rectYUp = MapToPdf(new NormBox(item.X, item.Y, item.W, item.H), rot, w, h);
        var t = RotateAboutCentre(item.Rotation, rectYUp);
        var placement = item.BehindContent ? StampPlacement.Underlay : StampPlacement.Overlay;
        using var ms = new MemoryStream();
        PageStamper.Place(ms, target, pageIndex, overlayDoc, 0, t, placement);
        return ms.ToArray();
    }

    /// <summary>Draws one shape into the overlay in TOP-LEFT, y-DOWN page points. Rectangles and
    /// lines keep their existing primitives; ellipse/arrow/freehand are authored via
    /// <see cref="Chuvadi.Pdf.Graphics.Path"/> + <c>DrawPath</c>.</summary>
    private static void DrawShape(PageBuilder pb, ShapeAnno item, int rot, double w, double h)
    {
        var strokeC = SafeColor(item.StrokeHex);
        double sw = item.StrokeWidthPt <= 0 ? 1.0 : item.StrokeWidthPt;

        switch (item.Kind)
        {
            case ShapeKind.Rectangle:
            {
                var rect = MapToPdfTopLeft(new NormBox(item.X, item.Y, item.W, item.H), rot, w, h);
                Color? fill = string.IsNullOrWhiteSpace(item.FillHex) ? null : SafeColor(item.FillHex);
                pb.DrawRectangle(rect.X, rect.Y, rect.Width, rect.Height, fill, strokeC, sw);
                break;
            }
            case ShapeKind.Line:
            {
                var (ax, ay) = MapPointTopLeft(item.X, item.Y, rot, w, h);
                var (bx, by) = MapPointTopLeft(item.X + item.W, item.Y + item.H, rot, w, h);
                pb.DrawLine(ax, ay, bx, by, strokeC, sw);
                break;
            }
            case ShapeKind.Ellipse:
            {
                var rect = MapToPdfTopLeft(new NormBox(item.X, item.Y, item.W, item.H), rot, w, h);
                Color? fillC = string.IsNullOrWhiteSpace(item.FillHex) ? null : SafeColor(item.FillHex);
                var path = new Chuvadi.Pdf.Graphics.Path()
                    .Ellipse(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0, rect.Width / 2.0, rect.Height / 2.0);
                pb.DrawPath(path, fillC, strokeC, sw, Chuvadi.Pdf.Graphics.FillRule.NonZeroWinding);
                break;
            }
            case ShapeKind.Arrow:
            {
                var (ax, ay) = MapPointTopLeft(item.X, item.Y, rot, w, h);
                var (bx, by) = MapPointTopLeft(item.X + item.W, item.Y + item.H, rot, w, h);
                pb.DrawPath(BuildArrow(ax, ay, bx, by, sw), null, strokeC, sw, Chuvadi.Pdf.Graphics.FillRule.NonZeroWinding);
                break;
            }
            case ShapeKind.Freehand:
            {
                if (item.Points is { Count: >= 2 } pts)
                {
                    var path = new Chuvadi.Pdf.Graphics.Path();
                    var (x0, y0) = MapPointTopLeft(pts[0].X, pts[0].Y, rot, w, h);
                    path.MoveTo(x0, y0);
                    for (int i = 1; i < pts.Count; i++)
                    {
                        var (px, py) = MapPointTopLeft(pts[i].X, pts[i].Y, rot, w, h);
                        path.LineTo(px, py);
                    }
                    pb.DrawPath(path, null, strokeC, sw, Chuvadi.Pdf.Graphics.FillRule.NonZeroWinding);
                }
                break;
            }
        }
    }

    /// <summary>Builds an open arrow path: a shaft A→B plus a two-line arrowhead at B.</summary>
    private static Chuvadi.Pdf.Graphics.Path BuildArrow(double ax, double ay, double bx, double by, double sw)
    {
        var path = new Chuvadi.Pdf.Graphics.Path().MoveTo(ax, ay).LineTo(bx, by);
        double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.0001) return path;
        double ux = dx / len, uy = dy / len;
        double head = Math.Max(8.0, sw * 3.5);
        const double ang = 0.5; // ~28.6° half-angle
        (double X, double Y) Rot(double vx, double vy, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return (vx * c - vy * s, vx * s + vy * c);
        }
        var (h1x, h1y) = Rot(-ux, -uy, ang);
        var (h2x, h2y) = Rot(-ux, -uy, -ang);
        path.MoveTo(bx, by).LineTo(bx + h1x * head, by + h1y * head);
        path.MoveTo(bx, by).LineTo(bx + h2x * head, by + h2y * head);
        return path;
    }

    /// <summary>Maps a single normalised on-screen point (top-left origin) through the page's
    /// display rotation into TOP-LEFT, y-DOWN page points.</summary>
    private static (double X, double Y) MapPointTopLeft(double nx, double ny, int rotation, double w, double h)
    {
        var (ux, uy) = InverseRotate(nx, ny, rotation);
        return (ux * w, uy * h);
    }

    /// <summary>Builds a transform that rotates by <paramref name="effDegrees"/> (screen-CW,
    /// converted to PDF-CCW) about the centre of <paramref name="rect"/> (y-up content space).
    /// Identity when the effective rotation is a no-op. Used by the text path.</summary>
    private static Transform RotateAboutCentre(double effDegrees, RectangleF rect)
    {
        if (Math.Abs(effDegrees % 360) < 0.001)
        {
            return Transform.Identity;
        }

        double cx = rect.X + rect.Width / 2.0;
        double cy = rect.Y + rect.Height / 2.0;
        return Transform.CreateTranslation(-cx, -cy)
            .Multiply(Transform.CreateRotationDegrees(-effDegrees))
            .Multiply(Transform.CreateTranslation(cx, cy));
    }

    /// <summary>True if the document carries a structure tree (tagged PDF). Redaction can't
    /// yet fully strip tagged text, so the reader warns and the save is blocked by verification.
    /// (Overlays — shapes, images, text — are unaffected; they add content rather than remove it.)</summary>
    public bool IsTagged(string sourcePath)
    {
        try
        {
            using var doc = PdfDocument.Open(sourcePath);
            var catalog = doc.Catalog;
            return catalog is not null
                && catalog.Keys.Any(k => (k?.ToString() ?? string.Empty)
                    .Contains("StructTreeRoot", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    // ── Find &amp; redact ──────────────────────────────────────────────────────

    /// <summary>One text match: the page it is on (0-based), its box(es) in normalised
    /// top-left page fractions (ready for the overlay layer and for redaction), and a short
    /// context snippet for the matches list.</summary>
    public sealed record RedactSearchMatch(int PageIndex, IReadOnlyList<NormBox> Boxes, string Snippet);

    /// <summary>Built-in regex preset groups (financial / medical / general PII), sourced from
    /// the library's <see cref="CommonPatterns"/>. A seam for a future library-side expansion —
    /// see ROADMAP "Ask B". Custom user regex is passed separately.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PatternPresets { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Financial"] = new[] { CommonPatterns.CreditCard, CommonPatterns.UsSsn, CommonPatterns.UsZip },
            ["Medical"] = new[] { CommonPatterns.Icd10Prefix, CommonPatterns.UkNhsNumber },
            ["PII"] = new[] { CommonPatterns.Email, CommonPatterns.UsPhone, CommonPatterns.IsoDate },
        };

    /// <summary>Plain-text search across the document, mirroring the standard find options.
    /// Page range is 0-based and inclusive; pass null for the whole document.</summary>
    public async Task<IReadOnlyList<RedactSearchMatch>> SearchAsync(
        string sourcePath, string query,
        bool caseSensitive, bool wholeWord,
        int? pageStart = null, int? pageEnd = null,
        CancellationToken ct = default)
    {
        var results = new List<RedactSearchMatch>();
        if (string.IsNullOrEmpty(query)) return results;

        using var doc = PdfDocument.Open(sourcePath);
        var opts = new SearchOptions
        {
            CaseSensitive = caseSensitive,
            WholeWord = wholeWord,
            PageRangeStart = pageStart,
            PageRangeEnd = pageEnd,
        };

        var pageTextCache = new Dictionary<int, string>();
        TextExtractor? extractor = null;

        await foreach (var m in DocumentSearch.SearchAsync(doc, query, opts, ct).WithCancellation(ct))
        {
            if (m.PageNumber < 0 || m.PageNumber >= doc.PageCount) continue;
            var page = doc.Pages[m.PageNumber];
            double w = page.Width, h = page.Height;
            if (w <= 0 || h <= 0) continue;

            var boxes = new List<NormBox>(m.BoundingBoxes.Count);
            foreach (var r in m.BoundingBoxes)
            {
                // Search boxes are in PDF user space (bottom-left origin, y UP). Convert to the
                // app's normalised top-left / y-DOWN fractions used by the overlay layer and by
                // MapToPdf for redaction.
                double nx = r.X / w;
                double ny = (h - (r.Y + r.Height)) / h;
                boxes.Add(new NormBox(nx, ny, r.Width / w, r.Height / h));
            }
            if (boxes.Count == 0) continue;

            // Build a short context snippet around the match offset.
            if (!pageTextCache.TryGetValue(m.PageNumber, out var text))
            {
                extractor ??= new TextExtractor(doc.Objects, ExtractionStrategy.Layout);
                try { text = extractor.ExtractText(page) ?? string.Empty; }
                catch { text = string.Empty; }
                pageTextCache[m.PageNumber] = text;
            }
            results.Add(new RedactSearchMatch(m.PageNumber, boxes, MakeSnippet(text, m.CharacterOffset, m.Length, query)));
        }

        return results;
    }

    private static string MakeSnippet(string text, int offset, int length, string fallback)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset >= text.Length) return fallback;
        int len = Math.Max(1, Math.Min(length, text.Length - offset));
        const int ctx = 24;
        int start = Math.Max(0, offset - ctx);
        int end = Math.Min(text.Length, offset + len + ctx);
        var snippet = text.Substring(start, end - start).Replace('\n', ' ').Replace('\r', ' ').Trim();
        return (start > 0 ? "… " : "") + snippet + (end < text.Length ? " …" : "");
    }

    /// <summary>Builds <see cref="PatternRule"/>s for a set of regex strings, applied to the
    /// whole document (or a 0-based inclusive page range).</summary>
    public static IReadOnlyList<PatternRule> BuildPatternRules(
        IEnumerable<string> regexes, int? pageStart = null, int? pageEnd = null)
    {
        int[] pages = Array.Empty<int>();
        if (pageStart is { } s && pageEnd is { } e && e >= s)
            pages = Enumerable.Range(s, e - s + 1).ToArray();

        var rules = new List<PatternRule>();
        foreach (var rx in regexes.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            try { rules.Add(new PatternRule(rx, pages)); }
            catch { /* skip an invalid custom regex */ }
        }
        return rules;
    }

    /// <summary>Unified redaction apply for the Redact destination: explicit boxes (manual draws
    /// + accepted find matches) PLUS regex patterns, in ONE pass. <paramref name="glyphOnly"/>
    /// removes content with no visible overlay (transparent); otherwise the overlay colour is
    /// drawn. Output is verified for true removal of the explicit boxes (the tagged-PDF guard);
    /// pattern hits inherit the same library limitation. Source is only read.</summary>
    public async Task ApplyAsync(
        string sourcePath, string outputPath,
        IReadOnlyDictionary<int, IReadOnlyList<NormBox>> boxesByPage,
        IReadOnlyList<PatternRule> patterns,
        bool glyphOnly, string overlayHex, double padding,
        CancellationToken ct = default)
    {
        bool hasBoxes = boxesByPage.Any(kv => kv.Value.Count > 0);
        bool hasPatterns = patterns.Count > 0;
        if (!hasBoxes && !hasPatterns)
            throw new InvalidOperationException("Nothing to redact — no boxes and no patterns.");

        await Task.Run(() =>
        {
            byte[] current = File.ReadAllBytes(sourcePath);
            using var doc = PdfDocument.Open(new MemoryStream(current, writable: false));

            var opts = new RedactionOptions
            {
                // 3.14.1: DrawOverlay=false gives a true glyph-only (boxless) redaction — the old
                // OverlayColor=Transparent signal was ignored. Keep a real colour for the box case.
                DrawOverlay = !glyphOnly,
                OverlayColor = ParseColor(overlayHex),
                PatternPadding = padding,
            };
            foreach (var rule in patterns) opts.Patterns.Add(rule);

            var mappedRects = new Dictionary<int, List<RectangleF>>();
            foreach (var (pageIndex, boxes) in boxesByPage)
            {
                if (boxes.Count == 0 || pageIndex < 0 || pageIndex >= doc.PageCount) continue;
                var pdfPage = doc.Pages[pageIndex];
                var rot = NormalizeRotation(pdfPage.Rotate); // destination renders unrotated
                var verifyRects = new List<RectangleF>();
                foreach (var box in boxes)
                {
                    var padded = Pad(box, padding, pdfPage.Width, pdfPage.Height);
                    // 3.14.1: RedactionRect.Bounds is BOTTOM-LEFT / y-up, which is the same frame the
                    // verify guard uses against TextExtractor fragments — so apply == verify rect.
                    var r = MapToPdf(padded, rot, pdfPage.Width, pdfPage.Height);
                    opts.Rectangles.Add(new RedactionRect(pageIndex, r));
                    verifyRects.Add(r);
                }
                mappedRects[pageIndex] = verifyRects;
            }

            using var ms = new MemoryStream();
            Redactor.Apply(ms, doc, opts);
            current = ms.ToArray();

            // Verify the explicit boxes actually cleared (blocks tagged-PDF leaks).
            if (mappedRects.Count > 0 && CountSurvivingText(current, mappedRects) > 0)
            {
                throw new RedactionNotRemovedException(
                    "Redaction could not fully remove text from this file — it was NOT saved. " +
                    "This PDF is tagged, and the underlying library can't yet strip text from " +
                    "tagged content. A library-side fix is needed before redaction is secure here.");
            }

            File.WriteAllBytes(outputPath, current);
        }, ct);
    }

    /// <summary>Expands a normalised box by <paramref name="padPoints"/> on every side.</summary>
    private static NormBox Pad(NormBox b, double padPoints, double w, double h)
    {
        if (padPoints <= 0 || w <= 0 || h <= 0) return b;
        double px = padPoints / w, py = padPoints / h;
        double x = Math.Max(0, b.X - px), y = Math.Max(0, b.Y - py);
        double right = Math.Min(1, b.X + b.W + px), bottom = Math.Min(1, b.Y + b.H + py);
        return new NormBox(x, y, right - x, bottom - y);
    }

    private static string ResolveFont(string? family, bool bold, bool italic)
    {
        var f = (family ?? "Helvetica").Trim().ToLowerInvariant();
        if (f.StartsWith("times"))
        {
            if (bold && italic) return "Times-BoldItalic";
            if (bold) return "Times-Bold";
            if (italic) return "Times-Italic";
            return "Times-Roman";
        }
        if (f.StartsWith("courier"))
        {
            if (bold && italic) return "Courier-BoldOblique";
            if (bold) return "Courier-Bold";
            if (italic) return "Courier-Oblique";
            return "Courier";
        }
        if (bold && italic) return "Helvetica-BoldOblique";
        if (bold) return "Helvetica-Bold";
        if (italic) return "Helvetica-Oblique";
        return "Helvetica";
    }

    private static TextAlignment ParseAlign(string? align) => (align ?? "left").Trim().ToLowerInvariant() switch
    {
        "center" => TextAlignment.Center,
        "right" => TextAlignment.Right,
        "justify" => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private static Color SafeColor(string? hex)
    {
        try { return Color.FromHex(string.IsNullOrWhiteSpace(hex) ? "#000000" : hex); }
        catch { return Color.FromBytes(0, 0, 0); }
    }

    private static int NormalizeRotation(int degrees) => ((degrees % 360) + 360) % 360;

    private static ColorF ParseColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            var h = hex.TrimStart('#');
            if (h.Length == 6
                && byte.TryParse(h.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
                && byte.TryParse(h.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
                && byte.TryParse(h.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return ColorF.FromRgb8(r, g, b, 255);
            }
        }

        return ColorF.FromRgb8(0, 0, 0, 255);
    }

    /// <summary>Re-opens the saved bytes and counts non-blank text fragments whose anchor still
    /// lands inside a redaction rectangle (slightly inset). Any survivors means removal failed.</summary>
    private static int CountSurvivingText(byte[] bytes, IReadOnlyDictionary<int, List<RectangleF>> rectsByPage)
    {
        int survivors = 0;
        try
        {
            using var doc = PdfDocument.Open(new MemoryStream(bytes, writable: false));
            var extractor = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);

            foreach (var (pageIndex, rects) in rectsByPage)
            {
                if (pageIndex < 0 || pageIndex >= doc.PageCount) continue;
                foreach (var frag in extractor.ExtractFragments(doc.Pages[pageIndex]))
                {
                    if (string.IsNullOrWhiteSpace(frag.Text)) continue;
                    foreach (var r in rects)
                    {
                        const float inset = 1.5f;
                        if (frag.X >= r.X + inset && frag.X <= r.X + r.Width - inset
                            && frag.Y >= r.Y + inset && frag.Y <= r.Y + r.Height - inset)
                        {
                            survivors++;
                            break;
                        }
                    }
                }
            }
        }
        catch
        {
            return int.MaxValue; // can't verify → treat as unsafe
        }

        return survivors;
    }

    /// <summary>Map a normalised on-screen box to a content-space rectangle (PDF points,
    /// origin bottom-left). <paramref name="rotation"/> is the clockwise display rotation the
    /// box was drawn under; <paramref name="w"/>/<paramref name="h"/> are the unrotated page
    /// dimensions in points.</summary>
    /// <summary>Like <see cref="MapToPdf"/> but returns the rectangle in the authoring layer's
    /// TOP-LEFT, y-DOWN coordinate system (origin at the page's top-left, y increases downward),
    /// which is what DrawRectangle / DrawLine / DrawImage / DrawTextBlock all expect. Page
    /// rotation is still applied via <see cref="InverseRotate"/>.</summary>
    internal static RectangleF MapToPdfTopLeft(NormBox box, int rotation, double w, double h)
    {
        var (ux0, uy0) = InverseRotate(box.X, box.Y, rotation);
        var (ux1, uy1) = InverseRotate(box.X + box.W, box.Y + box.H, rotation);

        double ux = Math.Min(ux0, ux1);
        double uy = Math.Min(uy0, uy1);
        double uw = Math.Abs(ux1 - ux0);
        double uh = Math.Abs(uy1 - uy0);

        return new RectangleF(
            (float)(ux * w),
            (float)(uy * h),
            (float)(uw * w),
            (float)(uh * h));
    }

    internal static RectangleF MapToPdf(NormBox box, int rotation, double w, double h)
    {
        var (ux0, uy0) = InverseRotate(box.X, box.Y, rotation);
        var (ux1, uy1) = InverseRotate(box.X + box.W, box.Y + box.H, rotation);

        double ux = Math.Min(ux0, ux1);
        double uy = Math.Min(uy0, uy1);
        double uw = Math.Abs(ux1 - ux0);
        double uh = Math.Abs(uy1 - uy0);

        return new RectangleF(
            (float)(ux * w),
            (float)(h - (uy + uh) * h),
            (float)(uw * w),
            (float)(uh * h));
    }

    private static (double X, double Y) InverseRotate(double x, double y, int rotation) => rotation switch
    {
        90 => (y, 1 - x),
        180 => (1 - x, 1 - y),
        270 => (1 - y, x),
        _ => (x, y),
    };
}
