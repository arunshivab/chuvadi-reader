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

/// <summary>
/// Flattens reader markup into a NEW pdf: redactions (true byte-level removal via the
/// library's <see cref="Redactor"/>, verified afterwards) and text overlays (authored and
/// stamped over the page). The source file is only ever read; output is always a separate
/// path chosen by the user, so the reader stays read-only.
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
            new Dictionary<int, PageText>(), ct);

    /// <summary>Apply redactions (if any) then text overlays (if any) and write the result
    /// to <paramref name="outputPath"/> (a different file). Redactions are flattened first,
    /// then text is stamped on top.</summary>
    public async Task FlattenToFileAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<int, PageRedaction> redactions,
        string overlayHex,
        IReadOnlyDictionary<int, PageText> texts,
        CancellationToken ct = default)
    {
        var hasRedactions = redactions.Any(kv => kv.Value.Boxes.Count > 0);
        var hasText = texts.Any(kv => kv.Value.Items.Count > 0);
        if (!hasRedactions && !hasText)
        {
            throw new InvalidOperationException("Nothing to save — no redactions or text.");
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

            // 3) Text overlays — one full-page overlay per box, stamped on top.
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
        Transform t;
        if (Math.Abs(eff % 360) < 0.001)
        {
            t = Transform.Identity;
        }
        else
        {
            double cx = rect.X + rect.Width / 2.0;
            double cy = rect.Y + rect.Height / 2.0;
            t = Transform.CreateTranslation(-cx, -cy)
                .Multiply(Transform.CreateRotationDegrees(-eff))
                .Multiply(Transform.CreateTranslation(cx, cy));
        }

        using var ms = new MemoryStream();
        PageStamper.Place(ms, target, pageIndex, overlayDoc, 0, t, StampPlacement.Overlay);
        return ms.ToArray();
    }

    /// <summary>True if the document carries a structure tree (tagged PDF). Redaction can't
    /// yet fully strip tagged text, so the reader warns and the save is blocked by verification.
    /// (Text overlays are unaffected — they add content rather than remove it.)</summary>
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
