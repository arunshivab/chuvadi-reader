using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Operations;
using Path = System.IO.Path;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// Composes an arbitrary arrangement of pages (drawn from several sources, in any
/// order, with rotations, with inserted blank pages, and with raw image pages) into
/// a single PDF.
///
/// Blank pages and image pages carry no source PDF, so they are first materialised
/// into tiny one-page temp PDFs (images via <see cref="ImagePdfConverter"/>) and then
/// flow through the same verified <see cref="PageOperations"/> pipeline as everything
/// else: Merge distinct sources, ReorderPages into screen order, ExtractPages to drop
/// the rest, RotatePages per distinct angle.
/// </summary>
public sealed class BenchComposer
{
    private sealed record Resolved(string Path, int Index, int Rotation);

    public Task ComposeToFileAsync(IReadOnlyList<BenchPage> pages, string outputPath, CancellationToken ct = default, string? normalizeSize = null)
        => Task.Run(async () =>
        {
            var bytes = await ComposeAsync(pages, ct, normalizeSize).ConfigureAwait(false);
            await File.WriteAllBytesAsync(outputPath, bytes, ct).ConfigureAwait(false);
        }, ct);

    /// <summary>Merges already-built PDF files (in order) into one output file, object-level
    /// (preserving each input's pages as-is). Used to combine every desk into a single PDF.</summary>
    public async Task MergeFilesAsync(IReadOnlyList<string> inputPaths, string outputPath, CancellationToken ct = default)
    {
        var docs = new List<PdfDocument>(inputPaths.Count);
        try
        {
            foreach (var path in inputPaths)
                docs.Add(await PdfDocument.OpenAsync(path, ct).ConfigureAwait(false));
            var array = docs.ToArray();
            await Task.Run(() =>
            {
                using var output = File.Create(outputPath);
                PageOperations.Merge(output, array);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            foreach (var d in docs) d.Dispose();
        }
    }

    /// <summary>N-up imposition (#38): lays the input PDF's pages onto sheets in a
    /// <paramref name="rows"/>×<paramref name="cols"/> grid (row-major, top-left first), each page
    /// scaled-to-fit-centered into its cell. Writes the imposed PDF to <paramref name="outputPath"/>.</summary>
    public Task ImposeNupAsync(string inputPath, string outputPath, int rows, int cols,
        double sheetW, double sheetH, double gutter, double margin, CancellationToken ct = default)
        => Task.Run(() =>
        {
            rows = Math.Max(1, rows); cols = Math.Max(1, cols);
            using var src = PdfDocument.Open(inputPath);
            int per = rows * cols, total = src.PageCount;
            double cellW = (sheetW - 2 * margin - (cols - 1) * gutter) / cols;
            double cellH = (sheetH - 2 * margin - (rows - 1) * gutter) / rows;
            var pc = new PageComposer();
            for (int start = 0; start < total; start += per)
            {
                ct.ThrowIfCancellationRequested();
                pc.AddPage(sheetW, sheetH);
                for (int k = 0; k < per; k++)
                {
                    int pi = start + k;
                    if (pi >= total) break;
                    int r = k / cols, c = k % cols;
                    double cellX = margin + c * (cellW + gutter);
                    double cellTop = margin + r * (cellH + gutter);
                    double cellBottom = sheetH - cellTop - cellH; // y-up
                    pc.PlacePage(src, pi, FitCentered(src.Pages[pi].Width, src.Pages[pi].Height, cellX, cellBottom, cellW, cellH));
                }
            }
            using var fs = File.Create(outputPath);
            pc.Write(fs);
        }, ct);

    /// <summary>Booklet (saddle-stitch) imposition (#39): pads the input to a multiple of four
    /// with blanks, computes the fold order, and lays out two pages per sheet side. The result
    /// prints double-sided and folds into a booklet that reads in order.</summary>
    public Task ImposeBookletAsync(string inputPath, string outputPath,
        double sheetW, double sheetH, double gutter, double margin, CancellationToken ct = default)
        => Task.Run(() =>
        {
            using var src = PdfDocument.Open(inputPath);
            int n = src.PageCount;
            int padded = ((n + 3) / 4) * 4;

            // Build the side-by-side order: [last,first], [2,last-1], [last-2,3], [4,last-3]…
            var order = new List<int>(padded);
            int left = 0, right = padded - 1;
            bool flip = false;
            for (int i = 0; i < padded / 2; i++)
            {
                if (!flip) { order.Add(right); order.Add(left); }
                else { order.Add(left); order.Add(right); }
                left++; right--; flip = !flip;
            }

            double cellW = (sheetW - 2 * margin - gutter) / 2;
            double cellH = sheetH - 2 * margin;
            double bottom = margin; // single row, full height
            var pc = new PageComposer();
            for (int i = 0; i < order.Count; i += 2)
            {
                ct.ThrowIfCancellationRequested();
                pc.AddPage(sheetW, sheetH);
                PlaceCell(pc, src, order[i], n, margin, bottom, cellW, cellH);
                PlaceCell(pc, src, order[i + 1], n, margin + cellW + gutter, bottom, cellW, cellH);
            }
            using var fs = File.Create(outputPath);
            pc.Write(fs);
        }, ct);

    private static void PlaceCell(PageComposer pc, PdfDocument src, int pageIndex, int realCount,
        double x, double bottom, double w, double h)
    {
        if (pageIndex < 0 || pageIndex >= realCount) return; // blank padding slot
        pc.PlacePage(src, pageIndex, FitCentered(src.Pages[pageIndex].Width, src.Pages[pageIndex].Height, x, bottom, w, h));
    }

    public async Task<byte[]> ComposeAsync(IReadOnlyList<BenchPage> pages, CancellationToken ct = default, string? normalizeSize = null)
    {
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("There are no pages to compose.");
        }

        var tempFiles = new List<string>();
        try
        {
            // Resolve blanks to temp one-page PDFs (reused per background colour),
            // and image pages to their own temp one-page PDFs (converted only now).
            var blankByColor = new Dictionary<string, string>();
            var resolved = new List<Resolved>(pages.Count);
            foreach (var page in pages)
            {
                if (page.IsBlank)
                {
                    var key = page.BackgroundHex ?? "";
                    if (!blankByColor.TryGetValue(key, out var temp))
                    {
                        temp = Path.GetTempFileName();
                        await File.WriteAllBytesAsync(temp, BuildBlankPdf(page.BackgroundHex), ct).ConfigureAwait(false);
                        blankByColor[key] = temp;
                        tempFiles.Add(temp);
                    }
                    resolved.Add(new Resolved(temp, 0, ((page.Rotation % 360) + 360) % 360));
                }
                else if (page.IsImage && !string.IsNullOrWhiteSpace(page.ImagePath))
                {
                    var temp = Path.GetTempFileName();
                    ImagePdfConverter.ConvertFile(page.ImagePath, temp, ImagePdfOptions.Default);
                    tempFiles.Add(temp);
                    resolved.Add(new Resolved(temp, 0, ((page.Rotation % 360) + 360) % 360));
                }
                else
                {
                    resolved.Add(new Resolved(page.SourcePath, page.OriginalIndex, ((page.Rotation % 360) + 360) % 360));
                }
            }

            var sourceOrder = new List<string>();
            foreach (var r in resolved)
            {
                if (!sourceOrder.Contains(r.Path))
                {
                    sourceOrder.Add(r.Path);
                }
            }

            var docs = new List<PdfDocument>(sourceOrder.Count);
            try
            {
                foreach (var path in sourceOrder)
                {
                    docs.Add(await PdfDocument.OpenAsync(path, ct).ConfigureAwait(false));
                }

                var offset = new Dictionary<string, int>(sourceOrder.Count);
                var running = 0;
                for (var i = 0; i < sourceOrder.Count; i++)
                {
                    offset[sourceOrder[i]] = running;
                    running += docs[i].PageCount;
                }

                var total = running;

                var combined = docs.Count == 1
                    ? await File.ReadAllBytesAsync(sourceOrder[0], ct).ConfigureAwait(false)
                    : MergeToBytes(docs);

                var want = new List<int>(resolved.Count);
                foreach (var r in resolved)
                {
                    want.Add(offset[r.Path] + r.Index);
                }

                var k = want.Count;
                byte[] result;

                if (k == total && IsIdentity(want, total))
                {
                    result = combined;
                }
                else
                {
                    // Build the output one page at a time and merge. This is the only
                    // approach that is correct when a page appears more than once (the
                    // same source page can be placed in a desk twice), which a single
                    // ReorderPages permutation cannot express.
                    using var combinedDoc = await OpenBytesAsync(combined, ct).ConfigureAwait(false);
                    var singleCache = new Dictionary<int, byte[]>();

                    async Task<byte[]> SingleAsync(int idx)
                    {
                        if (!singleCache.TryGetValue(idx, out var bytes))
                        {
                            using var sms = new MemoryStream();
                            PageOperations.ExtractPages(sms, combinedDoc, idx, 1);
                            bytes = sms.ToArray();
                            singleCache[idx] = bytes;
                        }
                        return bytes;
                    }

                    var singleDocs = new List<PdfDocument>(want.Count);
                    var singleTemps = new List<string>(want.Count);
                    try
                    {
                        foreach (var idx in want)
                        {
                            var temp = Path.GetTempFileName();
                            await File.WriteAllBytesAsync(temp, await SingleAsync(idx).ConfigureAwait(false), ct).ConfigureAwait(false);
                            singleTemps.Add(temp);
                            singleDocs.Add(await PdfDocument.OpenAsync(temp, ct).ConfigureAwait(false));
                        }

                        using var mms = new MemoryStream();
                        PageOperations.Merge(mms, singleDocs.ToArray());
                        result = mms.ToArray();
                    }
                    finally
                    {
                        foreach (var sd in singleDocs)
                        {
                            sd.Dispose();
                        }
                        foreach (var temp in singleTemps)
                        {
                            try { File.Delete(temp); } catch { /* best effort */ }
                        }
                    }
                }

                // Crop (per page) then normalise size (per desk), BEFORE rotation — both are
                // expressed in the page's own/display space, and the rotation pass below then
                // turns the cropped/resized page. Two single-transform passes (no compose).
                result = ApplyCrop(result, pages);
                result = ApplyNormalize(result, normalizeSize);
                result = ApplyMargins(result, pages);

                // Apply rotations, grouped by angle, on final page positions.
                var byAngle = resolved
                    .Select((r, i) => (r.Rotation, Index: i))
                    .Where(t => t.Rotation != 0)
                    .GroupBy(t => t.Rotation);

                foreach (var group in byAngle)
                {
                    using var doc = await OpenBytesAsync(result, ct).ConfigureAwait(false);
                    using var oms = new MemoryStream();
                    PageOperations.RotatePages(oms, doc, group.Key, group.Select(t => t.Index));
                    result = oms.ToArray();
                }

                return result;
            }
            finally
            {
                foreach (var doc in docs)
                {
                    doc.Dispose();
                }
            }
        }
        finally
        {
            foreach (var temp in tempFiles)
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }
    }

    private static (double W, double H)? ParseSize(string? name) => (name ?? "").Trim().ToUpperInvariant() switch
    {
        "A4" => (595.0, 842.0),
        "LETTER" => (612.0, 792.0),
        _ => null,
    };

    /// <summary>Maps a displayed (post-rotation) normalised point back into the page's own
    /// normalised space. Mirror of the redaction rotation convention.</summary>
    private static (double X, double Y) InverseRotate(double x, double y, int rotation) => rotation switch
    {
        90 => (y, 1 - x),
        180 => (1 - x, 1 - y),
        270 => (1 - y, x),
        _ => (x, y),
    };

    /// <summary>0.5 in default margin used by Fit+margin crop and the Margins "Default" preset.</summary>
    private const double DefaultMarginPt = 36.0;

    /// <summary>Un-rotates a displayed crop rect into the page's own space and returns the crop
    /// window in y-up PDF points (left, bottom, width, height), or null if degenerate.</summary>
    private static (double X, double Bottom, double W, double H)? CropWindow(CropRect crop, int rotation, double pw, double ph)
    {
        var rot = ((rotation % 360) + 360) % 360;
        var (ax, ay) = InverseRotate(crop.X, crop.Y, rot);
        var (bx, by) = InverseRotate(crop.X + crop.W, crop.Y + crop.H, rot);
        double ux = Math.Min(ax, bx), uy = Math.Min(ay, by);
        double uw = Math.Abs(bx - ax), uh = Math.Abs(by - ay);
        double x = ux * pw, w = uw * pw, h = uh * ph;
        double bottom = ph - (uy + uh) * ph;
        if (w <= 1 || h <= 1) return null;
        return (x, bottom, w, h);
    }

    /// <summary>Transform that scales a source (sw×sh, y-up origin) to fit a box at
    /// (boxX, boxBottom) of size (boxW×boxH), centered, aspect preserved. Verified upright.</summary>
    private static Transform FitCentered(double sw, double sh, double boxX, double boxBottom, double boxW, double boxH)
    {
        double s = Math.Min(boxW / sw, boxH / sh);
        if (s <= 0 || double.IsNaN(s) || double.IsInfinity(s)) s = 1;
        double tx = boxX + (boxW - sw * s) / 2.0;
        double ty = boxBottom + (boxH - sh * s) / 2.0;
        return Transform.CreateScale(s, s).Multiply(Transform.CreateTranslation(tx, ty));
    }

    /// <summary>Paints white over everything outside the crop window on one full-size page,
    /// by stamping a four-band overlay. Authoring coords are top-left/y-down.</summary>
    private static byte[] StampMaskBands(byte[] bytes, int pageIndex, (double X, double Bottom, double W, double H) win, double pw, double ph)
    {
        double cx = win.X, cw = win.W, ch = win.H;
        double cy = ph - (win.Bottom + win.H); // top edge from the top
        var overlay = PdfDocumentBuilder.Create();
        var pb = overlay.AddPage(new PageSize(pw, ph));
        var white = Color.FromHex("#FFFFFF");
        if (cy > 0) pb.DrawRectangle(0, 0, pw, cy, white, null, 0);                  // top band
        double below = cy + ch;
        if (below < ph) pb.DrawRectangle(0, below, pw, ph - below, white, null, 0);  // bottom band
        if (cx > 0) pb.DrawRectangle(0, cy, cx, ch, white, null, 0);                 // left band
        double right = cx + cw;
        if (right < pw) pb.DrawRectangle(right, cy, pw - right, ch, white, null, 0); // right band

        using var target = PdfDocument.Open(new MemoryStream(bytes, writable: false));
        using var overlayDoc = PdfDocument.Open(new MemoryStream(overlay.ToByteArray(), writable: false));
        using var ms = new MemoryStream();
        PageStamper.Place(ms, target, pageIndex, overlayDoc, 0, Transform.Identity, StampPlacement.Overlay);
        return ms.ToArray();
    }

    /// <summary>Realises any page crops per their <see cref="BenchPage.CropMode"/>:
    /// Mask (page kept, outside whitened), ToSize (page shrinks to the rect), FitPage / FitMargin
    /// (the rect is placed, scaled and centered, into the page / page-minus-margin). For Mask and
    /// Fit, everything outside the kept region is then painted white — this both realises the mask
    /// and hides the surrounding source content that <see cref="PageComposer"/> cannot clip when a
    /// page is placed (placing never clips, so the neighbours would otherwise bleed in).</summary>
    private static byte[] ApplyCrop(byte[] bytes, IReadOnlyList<BenchPage> pages)
    {
        if (!pages.Any(p => p.Crop != null)) return bytes;

        var origSizes = new List<(double W, double H)>();
        // page index -> kept rectangle (y-up points) to whiten around after compose.
        var maskRects = new Dictionary<int, (double X, double Bottom, double W, double H)>();

        byte[] composed;
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(temp, bytes);
            using var src = PdfDocument.Open(temp);
            var pc = new PageComposer();
            for (var i = 0; i < src.PageCount; i++)
            {
                double pw = src.Pages[i].Width, ph = src.Pages[i].Height;
                origSizes.Add((pw, ph));
                var page = i < pages.Count ? pages[i] : null;
                var crop = page?.Crop;
                var mode = page?.CropMode ?? CropFit.ToSize;
                var win = crop == null ? null : CropWindow(crop, page!.Rotation, pw, ph);
                if (crop == null || win == null)
                {
                    pc.AddPageMatching(src, i).PlacePage(src, i, Transform.Identity);
                    continue;
                }
                var w = win.Value;
                if (mode == CropFit.ToSize)
                {
                    pc.AddPage(w.W, w.H).PlacePage(src, i, Transform.CreateTranslation(-w.X, -w.Bottom));
                }
                else if (mode == CropFit.Mask)
                {
                    pc.AddPageMatching(src, i).PlacePage(src, i, Transform.Identity);
                    maskRects[i] = w; // kept region = the crop window on the full page
                }
                else // FitPage / FitMargin: map the crop rect into a centered box, mask the rest.
                {
                    double m = mode == CropFit.FitMargin ? DefaultMarginPt : 0;
                    double boxX = m, boxBottom = m, boxW = pw - 2 * m, boxH = ph - 2 * m;
                    double s = Math.Min(boxW / w.W, boxH / w.H);
                    if (s <= 0 || double.IsNaN(s) || double.IsInfinity(s)) s = 1;
                    double cw = w.W * s, ch = w.H * s;
                    double destX = boxX + (boxW - cw) / 2.0;
                    double destBottom = boxBottom + (boxH - ch) / 2.0;
                    var t = Transform.CreateTranslation(-w.X, -w.Bottom)
                        .Multiply(Transform.CreateScale(s, s))
                        .Multiply(Transform.CreateTranslation(destX, destBottom));
                    pc.AddPage(pw, ph).PlacePage(src, i, t);
                    maskRects[i] = (destX, destBottom, cw, ch); // kept region = the placed crop
                }
            }
            using var ms = new MemoryStream();
            pc.Write(ms);
            composed = ms.ToArray();
        }
        finally { try { File.Delete(temp); } catch { /* best effort */ } }

        // Whiten everything outside the kept rectangle for Mask + Fit pages.
        foreach (var kv in maskRects)
        {
            var (ow, oh) = origSizes[kv.Key];
            composed = StampMaskBands(composed, kv.Key, kv.Value, ow, oh);
        }
        return composed;
    }

    /// <summary>Insets each page's content by its per-side <see cref="BenchPage.Margins"/>:
    /// the whole page scales, centered, to fit inside the page minus the margins. The page size
    /// is unchanged (white margins appear around the content). Margins are in the page's own
    /// space (v1: not re-mapped for rotated pages).</summary>
    private static byte[] ApplyMargins(byte[] bytes, IReadOnlyList<BenchPage> pages)
    {
        if (!pages.Any(p => p.Margins != null)) return bytes;

        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(temp, bytes);
            using var src = PdfDocument.Open(temp);
            var pc = new PageComposer();
            for (var i = 0; i < src.PageCount; i++)
            {
                double pw = src.Pages[i].Width, ph = src.Pages[i].Height;
                var m = i < pages.Count ? pages[i].Margins : null;
                if (m == null)
                {
                    pc.AddPageMatching(src, i).PlacePage(src, i, Transform.Identity);
                    continue;
                }
                double iw = Math.Max(1, pw - m.Left - m.Right);
                double ih = Math.Max(1, ph - m.Top - m.Bottom);
                // inner box in y-up page coords: left=Left, bottom=Bottom.
                pc.AddPage(pw, ph).PlacePage(src, i, FitCentered(pw, ph, m.Left, m.Bottom, iw, ih));
            }
            using var ms = new MemoryStream();
            pc.Write(ms);
            return ms.ToArray();
        }
        finally { try { File.Delete(temp); } catch { /* best effort */ } }
    }

    /// <summary>Rebuilds every page at the target size (A4/Letter), scaling each page to fit
    /// (aspect preserved, top-anchored) via the library's PageComposer.</summary>
    private static byte[] ApplyNormalize(byte[] bytes, string? normalizeSize)
    {
        var target = ParseSize(normalizeSize);
        if (target == null) return bytes;
        var (tw, th) = target.Value;

        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(temp, bytes);
            using var src = PdfDocument.Open(temp);
            var pc = new PageComposer();
            for (var i = 0; i < src.PageCount; i++)
            {
                double pw = src.Pages[i].Width, ph = src.Pages[i].Height;
                pc.AddPage(tw, th).PlacePage(src, i, Placement.ScaleToFit(pw, ph, tw, th));
            }
            using var ms = new MemoryStream();
            pc.Write(ms);
            return ms.ToArray();
        }
        finally { try { File.Delete(temp); } catch { /* best effort */ } }
    }

    private static byte[] BuildBlankPdf(string? bgHex)
    {
        var builder = PdfDocumentBuilder.Create();
        var page = builder.AddPage(PageSize.A4);
        if (!string.IsNullOrWhiteSpace(bgHex))
        {
            var color = Color.FromHex(bgHex);
            page.DrawRectangle(0, 0, page.Width, page.Height, color, null, 0);
        }

        return builder.ToByteArray();
    }

    private static bool IsIdentity(List<int> order, int total)
    {
        if (order.Count != total)
        {
            return false;
        }

        for (var i = 0; i < total; i++)
        {
            if (order[i] != i)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] MergeToBytes(List<PdfDocument> docs)
    {
        using var ms = new MemoryStream();
        PageOperations.Merge(ms, docs.ToArray());
        return ms.ToArray();
    }

    private static async Task<PdfDocument> OpenBytesAsync(byte[] bytes, CancellationToken ct)
    {
        var temp = Path.GetTempFileName();
        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
        return await PdfDocument.OpenAsync(temp, ct).ConfigureAwait(false);
    }
}
