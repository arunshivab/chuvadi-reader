using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Redaction;
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
    byte[] ImageBytes, double Rotation, double Opacity);

/// <summary>The image overlays for one page, plus that page's on-screen view rotation.</summary>
public sealed record PageImage(int ViewRotation, IReadOnlyList<ImageAnno> Items);

/// <summary>The kind of vector shape an overlay draws.</summary>
public enum ShapeKind
{
    Rectangle,
    Line,
}

/// <summary>A single overlay shape, normalised to the displayed page (top-left origin).
/// For <see cref="ShapeKind.Rectangle"/> the box is the rectangle bounds. For
/// <see cref="ShapeKind.Line"/> the line runs from the box's top-left corner to its
/// bottom-right corner (so the box is the line's bounding span). <paramref name="FillHex"/>
/// is null/empty for no fill (rectangles only). Rotation is in degrees (clockwise on
/// screen).</summary>
public sealed record ShapeAnno(
    double X, double Y, double W, double H,
    ShapeKind Kind, string? FillHex, string StrokeHex, double StrokeWidthPt, double Rotation);

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
                var opts = new RedactionOptions { OverlayColor = ParseColor(overlayHex) };

                foreach (var (pageIndex, page) in redactions)
                {
                    if (page.Boxes.Count == 0 || pageIndex < 0 || pageIndex >= doc.PageCount) continue;
                    var pdfPage = doc.Pages[pageIndex];
                    var rot = NormalizeRotation(pdfPage.Rotate + page.ViewRotation);
                    var rects = new List<RectangleF>();
                    foreach (var box in page.Boxes)
                    {
                        var bounds = MapToPdf(box, rot, pdfPage.Width, pdfPage.Height);
                        rects.Add(bounds);
                        opts.Rectangles.Add(new RedactionRect(pageIndex, bounds));
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

        // Box rect in content space (bottom-left origin), mapped through page rotation.
        var rect = MapToPdf(new NormBox(item.X, item.Y, item.W, item.H), rot, w, h);

        var overlay = PdfDocumentBuilder.Create();
        var pb = overlay.AddPage(new PageSize(w, h));
        var font = ResolveFont(item.FontFamily, item.Bold, item.Italic);
        var color = SafeColor(item.ColorHex);
        var align = ParseAlign(item.Align);
        var lineHeight = item.LineHeight <= 0 ? 1.2 : item.LineHeight;
        double yTop = rect.Y + rect.Height; // DrawTextBlock takes the top edge (y-up page)

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
    private static byte[] StampPageOverlay(
        byte[] source, int pageIndex, int viewRotation,
        IReadOnlyList<ShapeAnno> shapes, IReadOnlyList<ImageAnno> imageItems)
    {
        using var target = PdfDocument.Open(new MemoryStream(source, writable: false));
        if (pageIndex < 0 || pageIndex >= target.PageCount) return source;

        var pdfPage = target.Pages[pageIndex];
        double w = pdfPage.Width, h = pdfPage.Height;
        var rot = NormalizeRotation(pdfPage.Rotate + viewRotation);

        var overlay = PdfDocumentBuilder.Create();
        var pb = overlay.AddPage(new PageSize(w, h));

        // Shapes first.
        foreach (var item in shapes)
        {
            var stroke = SafeColor(item.StrokeHex);
            double strokeWidth = item.StrokeWidthPt <= 0 ? 1.0 : item.StrokeWidthPt;

            if (item.Kind == ShapeKind.Rectangle)
            {
                var rect = MapToPdfTopLeft(new NormBox(item.X, item.Y, item.W, item.H), rot, w, h);
                Color? fill = string.IsNullOrWhiteSpace(item.FillHex) ? null : SafeColor(item.FillHex);
                pb.DrawRectangle(rect.X, rect.Y, rect.Width, rect.Height, fill, stroke, strokeWidth);
            }
            else // Line: map both endpoints individually so any drawn angle is preserved.
            {
                var (ax, ay) = MapPointTopLeft(item.X, item.Y, rot, w, h);
                var (bx, by) = MapPointTopLeft(item.X + item.W, item.Y + item.H, rot, w, h);
                pb.DrawLine(ax, ay, bx, by, stroke, strokeWidth);
            }
        }

        // Images above shapes.
        foreach (var item in imageItems)
        {
            if (item.ImageBytes is null || item.ImageBytes.Length == 0) continue;
            var rect = MapToPdfTopLeft(new NormBox(item.X, item.Y, item.W, item.H), rot, w, h);
            // NOTE: no image-opacity parameter in the library yet, so item.Opacity is not applied
            // (images stamp at full opacity). Per-item rotation also needs a library CTM — ROADMAP.
            pb.DrawImage(item.ImageBytes, rect.X, rect.Y, rect.Width, rect.Height);
        }

        using var overlayDoc = PdfDocument.Open(new MemoryStream(overlay.ToByteArray(), writable: false));
        using var ms = new MemoryStream();
        PageStamper.Place(ms, target, pageIndex, overlayDoc, 0, Transform.Identity, StampPlacement.Overlay);
        return ms.ToArray();
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
