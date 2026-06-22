using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Text;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;
using Path = System.IO.Path;

namespace ChuvadiReader.Tests;

/// <summary>Guards for the shared StampService (watermark + header/footer). Assertions use text
/// extraction — a stamped watermark / footer is drawn text, so it must extract back out.</summary>
public class StampTests
{
    private static string MultiPageDoc(int pages, double w = 595, double h = 842)
    {
        var b = PdfDocumentBuilder.Create();
        for (int i = 0; i < pages; i++)
        {
            var pb = b.AddPage(new PageSize(w, h));
            pb.DrawRectangle(0, 0, w, h, Color.FromHex("#FFFFFF"), Color.FromHex("#FFFFFF"), 0);
            pb.DrawText($"Body of page {i + 1}", 60, 400, "Helvetica", 14, Color.FromHex("#000000"));
        }
        return TestPdf.WriteTemp(b.ToByteArray());
    }

    private static string PageText(string pdf, int page)
    {
        using var doc = PdfDocument.Open(pdf);
        var ex = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);
        return ex.ExtractText(doc.Pages[page]) ?? "";
    }

    private static int PageCount(string pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return doc.PageCount;
    }

    [Fact]
    public async Task Watermark_IsStampedOnEveryPage()
    {
        var src = MultiPageDoc(3);
        var outp = TestPdf.TempOutput();
        var wm = new DeskWatermark { Text = "CONFIDENTIAL", Opacity = 0.4, RotationDegrees = 45, AllPages = true };

        await new StampService().ApplyToFileAsync(src, outp, wm, null, "doc.pdf");

        Assert.Equal(3, PageCount(outp));
        for (int p = 0; p < 3; p++)
        {
            Assert.Contains("CONFIDENTIAL", PageText(outp, p));
        }
    }

    [Fact]
    public async Task Footer_PageTokens_Resolve()
    {
        var src = MultiPageDoc(2);
        var outp = TestPdf.TempOutput();
        var hf = new DeskHeaderFooter
        {
            FooterEnabled = true,
            FooterCenter = "{page} / {total}",
            FontSize = 9,
            AllPages = true,
            Fit = "overlay",
        };

        await new StampService().ApplyToFileAsync(src, outp, null, hf, "doc.pdf");

        Assert.Equal(2, PageCount(outp));
        // {page}/{total} should resolve to "1 / 2" and "2 / 2".
        Assert.Contains("2", PageText(outp, 0));   // total
        Assert.Contains("2", PageText(outp, 1));   // page + total
    }

    [Fact]
    public async Task Watermark_RangeOnly_LeavesOtherPagesClean()
    {
        var src = MultiPageDoc(3);
        var outp = TestPdf.TempOutput();
        // Only page 2 (1-based) gets the watermark.
        var wm = new DeskWatermark { Text = "DRAFTMARK", AllPages = false, FromPage = 2, ToPage = 2 };

        await new StampService().ApplyToFileAsync(src, outp, wm, null, "doc.pdf");

        Assert.DoesNotContain("DRAFTMARK", PageText(outp, 0));
        Assert.Contains("DRAFTMARK", PageText(outp, 1));
        Assert.DoesNotContain("DRAFTMARK", PageText(outp, 2));
    }

    [Fact]
    public void ResolveRange_AllAndBounded()
    {
        Assert.Equal(new[] { 0, 1, 2 }, StampService.ResolveRange(true, 0, 0, 3));
        Assert.Equal(new[] { 1, 2 }, StampService.ResolveRange(false, 2, 3, 5)); // 1-based 2..3
        Assert.Equal(new[] { 2, 3, 4 }, StampService.ResolveRange(false, 3, 0, 5)); // 3..last
        Assert.Empty(StampService.ResolveRange(true, 0, 0, 0));
    }
}
