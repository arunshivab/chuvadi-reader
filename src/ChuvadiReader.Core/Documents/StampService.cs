using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Watermark;
using ChuvadiReader.Core.Reader;

namespace ChuvadiReader.Core.Documents;

using Path = System.IO.Path;

/// <summary>
/// One code path for stamping a PDF with a text watermark and/or a header/footer band,
/// using the library's <see cref="WatermarkStamper"/> and <see cref="HeaderFooter"/>.
/// Both the Bench (per-desk press) and the Reader (stamp the open document) route through
/// here so behaviour and option-mapping stay identical. Callers resolve which pages to hit
/// and pass explicit 0-based indices; this service does not own scope semantics.
/// </summary>
public sealed class StampService
{
    /// <summary>Stamp a text watermark onto the given pages. Returns the input unchanged
    /// when there is nothing to do.</summary>
    public byte[] ApplyWatermark(byte[] src, DeskWatermark? wm, int[] pageIndices)
    {
        if (wm is null || string.IsNullOrWhiteSpace(wm.Text) || pageIndices.Length == 0)
        {
            return src;
        }

        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(temp, src);
            using var doc = PdfDocument.Open(temp);
            using var ms = new MemoryStream();
            var opts = new TextWatermarkOptions(wm.Text)
            {
                FontName = wm.ResolveFontName(),
                FontSize = wm.FontSize,
                Opacity = (float)wm.Opacity,
                RotationDegrees = wm.RotationDegrees,
                Color = HexToColorF(wm.ColorHex),
                PageIndices = pageIndices,
            };
            WatermarkStamper.ApplyText(ms, doc, opts);
            return ms.ToArray();
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    /// <summary>Stamp a header/footer band onto the given pages. Returns the input unchanged
    /// when there is nothing to do.</summary>
    public byte[] ApplyHeaderFooter(byte[] src, DeskHeaderFooter? hf, int[] pageIndices, string filePath, DateTimeOffset timestamp)
    {
        if (hf is null || !hf.HasAnyContent || pageIndices.Length == 0)
        {
            return src;
        }

        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(temp, src);
            using var doc = PdfDocument.Open(temp);
            using var ms = new MemoryStream();

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
                PageIndices = pageIndices,
                FilePath = string.IsNullOrWhiteSpace(filePath) ? "document.pdf" : filePath,
                Timestamp = timestamp,
            };

            HeaderFooter.Apply(ms, doc, opts);
            return ms.ToArray();
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    /// <summary>Per-page page/Bates numbering (#40). Executes the numbering plan from
    /// <see cref="DeskNumbering.Stamps"/> — which pages get a number and the exact text, honouring
    /// style, Start offset, scope and first-page handling.</summary>
    public byte[] ApplyNumbering(byte[] src, DeskNumbering? num, int totalPages)
    {
        if (num is null)
        {
            return src;
        }

        var plan = num.Stamps(totalPages);
        if (plan.Count == 0)
        {
            return src;
        }

        var bytes = src;
        foreach (var (pageIndex, text) in plan)
        {
            bytes = StampNumberOnPage(bytes, pageIndex, text, num);
        }
        return bytes;
    }

    private static byte[] StampNumberOnPage(byte[] bytes, int pageIndex, string text, DeskNumbering num)
    {
        using var target = PdfDocument.Open(new MemoryStream(bytes, writable: false));
        if (pageIndex < 0 || pageIndex >= target.PageCount)
        {
            return bytes;
        }

        double pw = target.Pages[pageIndex].Width, ph = target.Pages[pageIndex].Height;
        const double margin = 24;
        var lineH = num.FontSize * 1.4;
        var boxW = Math.Max(1, pw - 2 * margin);
        var y = num.Position is NumberPosition.TopLeft or NumberPosition.TopRight
            ? margin
            : ph - margin - lineH;
        var align = num.Position switch
        {
            NumberPosition.BottomLeft or NumberPosition.TopLeft => TextAlignment.Left,
            NumberPosition.BottomCenter => TextAlignment.Center,
            _ => TextAlignment.Right,
        };

        var overlay = PdfDocumentBuilder.Create();
        var pb = overlay.AddPage(new PageSize(pw, ph));
        pb.DrawTextBlock(text, margin, y, boxW, lineH, "Helvetica", num.FontSize, Color.FromHex(num.ColorHex), align);

        using var overlayDoc = PdfDocument.Open(new MemoryStream(overlay.ToByteArray(), writable: false));
        using var ms = new MemoryStream();
        PageStamper.Place(ms, target, pageIndex, overlayDoc, 0, Transform.Identity, StampPlacement.Overlay);
        return ms.ToArray();
    }

    /// <summary>Reader convenience: apply watermark then header/footer to a source file and
    /// write the result to <paramref name="outPath"/>. Page scope is taken from each config's
    /// AllPages / FromPage..ToPage (1-based, ToPage 0 = last).</summary>
    public async Task ApplyToFileAsync(
        string srcPath, string outPath,
        DeskWatermark? wm, DeskHeaderFooter? hf,
        string filePath, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(srcPath, ct).ConfigureAwait(false);

        int total;
        using (var doc = PdfDocument.Open(srcPath))
        {
            total = doc.PageCount;
        }

        if (wm is not null && !string.IsNullOrWhiteSpace(wm.Text))
        {
            var pages = ResolveRange(wm.AllPages, wm.FromPage, wm.ToPage, total);
            bytes = ApplyWatermark(bytes, wm, pages);
        }

        if (hf is not null && hf.HasAnyContent)
        {
            var pages = ResolveRange(hf.AllPages, hf.FromPage, hf.ToPage, total);
            bytes = ApplyHeaderFooter(bytes, hf, pages, filePath, DateTimeOffset.Now);
        }

        await File.WriteAllBytesAsync(outPath, bytes, ct).ConfigureAwait(false);
    }

    /// <summary>Resolve a 1-based inclusive AllPages / From..To range to 0-based page indices.</summary>
    public static int[] ResolveRange(bool allPages, int fromPage, int toPage, int total)
    {
        if (total <= 0)
        {
            return Array.Empty<int>();
        }
        if (allPages)
        {
            return Enumerable.Range(0, total).ToArray();
        }
        int from = Math.Clamp(fromPage <= 0 ? 1 : fromPage, 1, total);
        int to = toPage <= 0 ? total : Math.Clamp(toPage, 1, total);
        if (to < from)
        {
            (from, to) = (to, from);
        }
        return Enumerable.Range(from - 1, to - from + 1).ToArray();
    }

    /// <summary>Parse #RRGGBB (or #RGB) into a fully-opaque ColorF.</summary>
    public static ColorF HexToColorF(string? hex)
    {
        var h = (hex ?? "").TrimStart('#');
        if (h.Length == 3)
        {
            h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
        }
        byte r = 0, g = 0, b = 0;
        if (h.Length == 6)
        {
            byte.TryParse(h.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r);
            byte.TryParse(h.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g);
            byte.TryParse(h.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b);
        }
        return ColorF.FromRgb8(r, g, b, 255);
    }
}
