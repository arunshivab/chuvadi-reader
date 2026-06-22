using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Core.Documents;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>Guards the Bench geometry pass (crop + size-normalise) that runs in
/// <see cref="BenchComposer"/> via the library's PageComposer. A 300×300 page is split into
/// four coloured quadrants so position is unambiguous after a transform.</summary>
public class BenchGeometryTests
{
    // TL red, TR blue, BL green, BR teal (authoring is top-left / y-down).
    private static string QuadDoc()
    {
        var b = PdfDocumentBuilder.Create();
        var pb = b.AddPage(new PageSize(300, 300));
        pb.DrawRectangle(0, 0, 150, 150, Color.FromHex("#D03030"), null, 0);
        pb.DrawRectangle(150, 0, 150, 150, Color.FromHex("#3050D0"), null, 0);
        pb.DrawRectangle(0, 150, 150, 150, Color.FromHex("#30A040"), null, 0);
        pb.DrawRectangle(150, 150, 150, 150, Color.FromHex("#00B0B0"), null, 0);
        return TestPdf.WriteTemp(b.ToByteArray());
    }

    private static BenchPage Page(string path, int idx, CropRect? crop = null) => new()
    {
        SourcePath = path,
        SourceName = "q.pdf",
        SourceIndex = 0,
        OriginalIndex = idx,
        Crop = crop,
    };

    private static bool IsRed((int R, int G, int B) p) => p.R >= 150 && p.G <= 120 && p.B <= 120;
    private static bool IsTeal((int R, int G, int B) p) => p.G >= 120 && p.B >= 120 && p.R <= 120;

    [Fact]
    public async Task Crop_TopLeftQuadrant_PageBecomesCropSize_AndShowsOnlyThatQuadrant()
    {
        var src = QuadDoc();
        var bytes = await new BenchComposer().ComposeAsync(new[] { Page(src, 0, new CropRect(0, 0, 0.5, 0.5)) });
        var outPath = TestPdf.WriteTemp(bytes);

        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(150.0, doc.Pages[0].Width, 1);
        Assert.Equal(150.0, doc.Pages[0].Height, 1);
        // The whole cropped page should be the TL (red) quadrant.
        Assert.True(IsRed(TestPdf.SamplePixel(outPath, 0, 0.5, 0.5)), "crop centre should be RED");
        Assert.True(IsRed(TestPdf.SamplePixel(outPath, 0, 0.1, 0.9)), "crop corner should be RED");
    }

    [Fact]
    public async Task Crop_LeavesUncroppedPagesUntouched()
    {
        var src = QuadDoc();
        var bytes = await new BenchComposer().ComposeAsync(new[]
        {
            Page(src, 0, new CropRect(0, 0, 0.5, 0.5)), // cropped → red 150×150
            Page(src, 0, null),                          // full quad page, kept as-is
        });
        var outPath = TestPdf.WriteTemp(bytes);

        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(2, doc.PageCount);
        Assert.Equal(300.0, doc.Pages[1].Width, 1);   // second page untouched
        Assert.Equal(300.0, doc.Pages[1].Height, 1);
        Assert.True(IsTeal(TestPdf.SamplePixel(outPath, 1, 0.75, 0.75)), "BR quadrant of page 2 should stay TEAL");
        Assert.True(IsRed(TestPdf.SamplePixel(outPath, 1, 0.25, 0.25)), "TL quadrant of page 2 should stay RED");
    }

    [Fact]
    public async Task Normalize_A4_SetsPageSize_AndScalesContent()
    {
        var src = QuadDoc();
        var bytes = await new BenchComposer().ComposeAsync(new[] { Page(src, 0) }, default, "A4");
        var outPath = TestPdf.WriteTemp(bytes);

        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(595.0, doc.Pages[0].Width, 2);
        Assert.Equal(842.0, doc.Pages[0].Height, 2);
        // ScaleToFit anchors top-left: the TL red quadrant sits near the top.
        Assert.True(IsRed(TestPdf.SamplePixel(outPath, 0, 0.2, 0.15)), "TL should be RED near the top after normalise");
    }

    [Fact]
    public async Task Normalize_Off_LeavesPageSizeUnchanged()
    {
        var src = QuadDoc();
        var bytes = await new BenchComposer().ComposeAsync(new[] { Page(src, 0) }, default, null);
        var outPath = TestPdf.WriteTemp(bytes);

        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(300.0, doc.Pages[0].Width, 1);
        Assert.Equal(300.0, doc.Pages[0].Height, 1);
    }

    [Fact]
    public async Task Normalize_Letter_SetsLetterPageSize()
    {
        var src = QuadDoc();
        var bytes = await new BenchComposer().ComposeAsync(new[] { Page(src, 0) }, default, "Letter");
        var outPath = TestPdf.WriteTemp(bytes);
        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(612.0, doc.Pages[0].Width, 2);   // US Letter width
        Assert.Equal(792.0, doc.Pages[0].Height, 2);  // US Letter height
    }


    [Fact]
    public async Task Normalize_MultiPage_NonIdentityOrder_AllPagesBecomeLetter()
    {
        // Two distinct source docs, interleaved order (forces the multi-page merge branch).
        var a = QuadDoc();
        var b = QuadDoc();
        var pages = new[]
        {
            Page(a, 0), Page(b, 0), Page(a, 0), Page(b, 0), Page(a, 0),
        };
        var bytes = await new BenchComposer().ComposeAsync(pages, default, "Letter");
        var outPath = TestPdf.WriteTemp(bytes);
        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(5, doc.PageCount);
        for (var i = 0; i < doc.PageCount; i++)
        {
            Assert.Equal(612.0, doc.Pages[i].Width, 2);
            Assert.Equal(792.0, doc.Pages[i].Height, 2);
        }
    }


    [Fact]
    public async Task Normalize_Letter_CropBox_AlsoMatches_MediaBox()
    {
        var src = QuadDoc(); // 300x300 source
        var bytes = await new BenchComposer().ComposeAsync(new[] { Page(src, 0) }, default, "Letter");
        var outPath = TestPdf.WriteTemp(bytes);
        using var doc = PdfDocument.Open(outPath);
        var mb = doc.Pages[0].MediaBox;
        var cb = doc.Pages[0].CropBox;
        // Surface both boxes in the failure message so we can see the mismatch.
        Assert.True(System.Math.Abs(cb.Width - 612) < 2 && System.Math.Abs(cb.Height - 792) < 2,
            $"MediaBox={mb.Width}x{mb.Height}  CropBox={cb.Width}x{cb.Height} (expected CropBox 612x792)");
    }


    // A4-sized source (matches the user's real PDFs), multi-page merge branch.
    private static string A4Doc()
    {
        var b = PdfDocumentBuilder.Create();
        var pb = b.AddPage(new PageSize(595, 842));
        pb.DrawRectangle(0, 0, 595, 842, Color.FromHex("#EFEFEF"), null, 0);
        pb.DrawRectangle(40, 40, 200, 120, Color.FromHex("#D03030"), null, 0);
        return TestPdf.WriteTemp(b.ToByteArray());
    }

    [Fact]
    public async Task Normalize_Letter_A4Source_MultiPage_BothBoxesLetter()
    {
        var a = A4Doc();
        var b = A4Doc();
        var pages = new[] { Page(a, 0), Page(b, 0), Page(a, 0) };
        var bytes = await new BenchComposer().ComposeAsync(pages, default, "Letter");
        var outPath = TestPdf.WriteTemp(bytes);
        using var doc = PdfDocument.Open(outPath);
        var report = "";
        for (var i = 0; i < doc.PageCount; i++)
        {
            var mb = doc.Pages[i].MediaBox; var cb = doc.Pages[i].CropBox;
            report += $"p{i}: MB={mb.Width:0}x{mb.Height:0} CB={cb.Width:0}x{cb.Height:0}; ";
        }
        var ok = true;
        for (var i = 0; i < doc.PageCount; i++)
        {
            var cb = doc.Pages[i].CropBox;
            if (System.Math.Abs(cb.Width - 612) > 2 || System.Math.Abs(cb.Height - 792) > 2) ok = false;
        }
        Assert.True(ok, report);
    }

    // ── crop modes (Mask / FitPage / FitMargin) + Add Margins ────────────────
    private static bool IsWhite((int R, int G, int B) p) => p.R >= 240 && p.G >= 240 && p.B >= 240;
    private static bool IsBlue((int R, int G, int B) p) => p.B >= 150 && p.R <= 120 && p.G <= 130;
    private static bool IsGreen((int R, int G, int B) p) => p.G >= 120 && p.R <= 120 && p.B <= 120;

    private static bool RegionHasDark(string path, int page, double x0, double y0, double x1, double y1)
    {
        for (var fy = y0; fy <= y1; fy += 0.02)
            for (var fx = x0; fx <= x1; fx += 0.02)
            {
                var p = TestPdf.SamplePixel(path, page, fx, fy);
                if (p.R < 100 && p.G < 100 && p.B < 100) return true;
            }
        return false;
    }

    private static string ColorPagesDoc(params string[] hexes)
    {
        var b = PdfDocumentBuilder.Create();
        foreach (var hex in hexes)
        {
            var pb = b.AddPage(new PageSize(300, 300));
            pb.DrawRectangle(0, 0, 300, 300, Color.FromHex(hex), null, 0);
        }
        return TestPdf.WriteTemp(b.ToByteArray());
    }

    private static BenchPage CropPage(string path, CropRect crop, CropFit mode) => new()
    {
        SourcePath = path, SourceName = "q.pdf", SourceIndex = 0, OriginalIndex = 0,
        Crop = crop, CropMode = mode,
    };

    [Fact]
    public async Task Crop_Mask_KeepsPageSize_AndWhitensOutsideRect()
    {
        var src = QuadDoc();
        var bytes = await new BenchComposer().ComposeAsync(new[] { CropPage(src, new CropRect(0, 0, 0.5, 0.5), CropFit.Mask) });
        var outPath = TestPdf.WriteTemp(bytes);

        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(300.0, doc.Pages[0].Width, 1);   // page size unchanged
        Assert.Equal(300.0, doc.Pages[0].Height, 1);
        Assert.True(IsRed(TestPdf.SamplePixel(outPath, 0, 0.25, 0.25)), "kept TL quadrant should stay RED");
        Assert.True(IsWhite(TestPdf.SamplePixel(outPath, 0, 0.75, 0.25)), "TR should be whitened");
        Assert.True(IsWhite(TestPdf.SamplePixel(outPath, 0, 0.25, 0.75)), "BL should be whitened");
        Assert.True(IsWhite(TestPdf.SamplePixel(outPath, 0, 0.75, 0.75)), "BR should be whitened");
    }

    [Fact]
    public async Task Crop_FitPage_KeepsOriginalSize_AndScalesCropToFill()
    {
        var src = QuadDoc();
        // Crop the TOP HALF (red + blue), aspect 2:1 → letterbox top & bottom when fit into a square page.
        var bytes = await new BenchComposer().ComposeAsync(new[] { CropPage(src, new CropRect(0, 0, 1.0, 0.5), CropFit.FitPage) });
        var outPath = TestPdf.WriteTemp(bytes);

        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(300.0, doc.Pages[0].Width, 1);   // NOT 150 — back to original size
        Assert.Equal(300.0, doc.Pages[0].Height, 1);
        // Centred band holds the cropped strip: left RED, right BLUE.
        Assert.True(IsRed(TestPdf.SamplePixel(outPath, 0, 0.25, 0.5)), "band left RED");
        Assert.True(IsBlue(TestPdf.SamplePixel(outPath, 0, 0.75, 0.5)), "band right BLUE");
        // Letterbox must be WHITE — the bottom-half quadrants must NOT bleed in.
        Assert.True(IsWhite(TestPdf.SamplePixel(outPath, 0, 0.5, 0.06)), "top letterbox WHITE");
        Assert.True(IsWhite(TestPdf.SamplePixel(outPath, 0, 0.5, 0.94)), "bottom letterbox WHITE (no bleed)");
    }

    [Fact]
    public async Task Crop_FitMargin_KeepsOriginalSize_AndLeavesWhiteBorder()
    {
        var src = QuadDoc();
        var bytes = await new BenchComposer().ComposeAsync(new[] { CropPage(src, new CropRect(0, 0, 1.0, 0.5), CropFit.FitMargin) });
        var outPath = TestPdf.WriteTemp(bytes);

        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(300.0, doc.Pages[0].Width, 1);
        Assert.Equal(300.0, doc.Pages[0].Height, 1);
        Assert.True(IsRed(TestPdf.SamplePixel(outPath, 0, 0.25, 0.5)), "band left RED");
        Assert.True(IsBlue(TestPdf.SamplePixel(outPath, 0, 0.75, 0.5)), "band right BLUE");
        Assert.True(IsWhite(TestPdf.SamplePixel(outPath, 0, 0.03, 0.03)), "corner inside the default margin should be WHITE");
        Assert.True(IsWhite(TestPdf.SamplePixel(outPath, 0, 0.5, 0.94)), "bottom letterbox WHITE (no bleed)");
    }

    [Fact]
    public async Task Margins_InsetContent_KeepsPageSize_AndBordersWhite()
    {
        var src = QuadDoc();
        var page = new BenchPage
        {
            SourcePath = src, SourceName = "q.pdf", SourceIndex = 0, OriginalIndex = 0,
            Margins = new MarginSet(30, 30, 30, 30), // 30pt of 300pt = 0.1 each side
        };
        var bytes = await new BenchComposer().ComposeAsync(new[] { page });
        var outPath = TestPdf.WriteTemp(bytes);

        using var doc = PdfDocument.Open(outPath);
        Assert.Equal(300.0, doc.Pages[0].Width, 1);   // page size unchanged
        Assert.Equal(300.0, doc.Pages[0].Height, 1);
        Assert.True(IsWhite(TestPdf.SamplePixel(outPath, 0, 0.03, 0.03)), "outer corner should be WHITE margin");
        Assert.True(IsRed(TestPdf.SamplePixel(outPath, 0, 0.3, 0.3)), "inset TL should be RED");
        Assert.True(IsTeal(TestPdf.SamplePixel(outPath, 0, 0.7, 0.7)), "inset BR should be TEAL");
    }

    [Fact]
    public async Task MergeFiles_ConcatenatesPagesInOrder()
    {
        var a = QuadDoc();
        var b = QuadDoc();
        var outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"chuvadi-merge-{Guid.NewGuid():N}.pdf");
        try
        {
            await new BenchComposer().MergeFilesAsync(new[] { a, b }, outPath);
            using var doc = PdfDocument.Open(outPath);
            Assert.Equal(2, doc.PageCount);
            Assert.Equal(300.0, doc.Pages[0].Width, 1);
            Assert.Equal(300.0, doc.Pages[1].Width, 1);
        }
        finally { try { System.IO.File.Delete(outPath); } catch { } }
    }

    [Fact]
    public void Outline_WriteRead_RoundTrips()
    {
        var svc = new OutlineService();
        var src = ColorPagesDoc("#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF");
        var tree = new List<BookmarkNode>
        {
            new("Cover", 0),
            new("Chapter 1", 1) { Children = { new BookmarkNode("Section 1.1", 2) } },
            new("End", 3),
        };
        var withToc = svc.Write(System.IO.File.ReadAllBytes(src), tree);
        var outPath = TestPdf.WriteTemp(withToc);
        using var doc = PdfDocument.Open(outPath);
        var read = svc.Read(doc);

        Assert.Equal(3, read.Count);
        Assert.Equal("Cover", read[0].Title);
        Assert.Equal(0, read[0].PageIndex);
        Assert.Single(read[1].Children);
        Assert.Equal("Section 1.1", read[1].Children[0].Title);
        Assert.Equal(2, read[1].Children[0].PageIndex);
        Assert.Equal(3, read[2].PageIndex);
    }

    [Fact]
    public void Outline_Export_SectionsAndNesting()
    {
        var svc = new OutlineService();
        var aRaw = ColorPagesDoc("#FFFFFF", "#FFFFFF", "#FFFFFF");
        var aBytes = svc.Write(System.IO.File.ReadAllBytes(aRaw), new List<BookmarkNode> { new("Intro", 0), new("Body", 2) });
        var aPath = TestPdf.WriteTemp(aBytes);
        var bPath = ColorPagesDoc("#FFFFFF", "#FFFFFF");

        var pages = new List<BenchPage>
        {
            MakePage(aPath, "A.pdf", 0, 0), MakePage(aPath, "A.pdf", 0, 1), MakePage(aPath, "A.pdf", 0, 2),
            MakePage(bPath, "B.pdf", 1, 0), MakePage(bPath, "B.pdf", 1, 1),
        };

        var outline = svc.BuildExportOutline(pages, System.IO.Path.GetFileName, s => svc.Read(s));

        Assert.Equal(2, outline.Count);
        Assert.Equal(0, outline[0].PageIndex);   // section A starts at output page 0
        Assert.Equal(3, outline[1].PageIndex);   // section B starts at output page 3
        Assert.Equal(2, outline[0].Children.Count);
        Assert.Equal("Intro", outline[0].Children[0].Title);
        Assert.Equal(0, outline[0].Children[0].PageIndex);
        Assert.Equal(2, outline[0].Children[1].PageIndex); // Body @orig2 → output 2
        Assert.Empty(outline[1].Children);                 // B has no outline
    }

    private static BenchPage MakePage(string path, string name, int srcIdx, int origIdx) => new()
    {
        SourcePath = path, SourceName = name, SourceIndex = srcIdx, OriginalIndex = origIdx,
    };

    [Fact]
    public void Numbering_Plan_FirstPageModes()
    {
        var bates = new DeskNumbering { Style = NumberStyle.Bates, Prefix = "ABC", Start = 1, PadWidth = 6 };
        var p = bates.Stamps(4);
        Assert.Equal(4, p.Count);
        Assert.Equal((0, "ABC000001"), p[0]);
        Assert.Equal((3, "ABC000004"), p[3]);

        // Skip, keep count: page 0 unstamped but counted → page 2 reads "2 of 4".
        var skip = new DeskNumbering { Style = NumberStyle.PageOfTotal, FirstPage = FirstPageMode.SkipKeepCount };
        var ps = skip.Stamps(4);
        Assert.Equal(3, ps.Count);
        Assert.Equal((1, "Page 2 of 4"), ps[0]);
        Assert.Equal((3, "Page 4 of 4"), ps[2]);

        // Skip & renumber: page 0 excluded and uncounted → page 2 reads "1 of 3".
        var ren = new DeskNumbering { Style = NumberStyle.PageOfTotal, FirstPage = FirstPageMode.SkipRenumber };
        var pr = ren.Stamps(4);
        Assert.Equal(3, pr.Count);
        Assert.Equal((1, "Page 1 of 3"), pr[0]);
        Assert.Equal((3, "Page 3 of 3"), pr[2]);

        // Range scope still subsets independently.
        var rng = new DeskNumbering { Style = NumberStyle.PageOnly, AllPages = false, FromPage = 2, ToPage = 3 };
        var prg = rng.Stamps(4);
        Assert.Equal(2, prg.Count);
        Assert.Equal((1, "Page 2"), prg[0]);
        Assert.Equal((2, "Page 3"), prg[1]);
    }

    [Fact]
    public void Numbering_CornerStamp_Renders()
    {
        var src = ColorPagesDoc("#FFFFFF", "#FFFFFF");
        var num = new DeskNumbering { Style = NumberStyle.Bates, Prefix = "B", Start = 1, PadWidth = 4, Position = NumberPosition.BottomRight, FontSize = 14, ColorHex = "#000000" };
        var stamped = new ChuvadiReader.Core.Documents.StampService().ApplyNumbering(System.IO.File.ReadAllBytes(src), num, 2);
        var outPath = TestPdf.WriteTemp(stamped);
        try
        {
            Assert.True(RegionHasDark(outPath, 0, 0.55, 0.83, 0.99, 0.96), "page0 bottom-right carries the number");
            Assert.False(RegionHasDark(outPath, 0, 0.02, 0.02, 0.45, 0.15), "top-left stays blank");
        }
        finally { try { System.IO.File.Delete(outPath); } catch { } }
    }

    [Fact]
    public async Task Nup_2x2_PlacesFourPagesRowMajor()
    {
        var src = ColorPagesDoc("#D03030", "#30A040", "#3050D0", "#00B0B0"); // R G B teal
        var outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"chuvadi-nup-{Guid.NewGuid():N}.pdf");
        try
        {
            await new BenchComposer().ImposeNupAsync(src, outPath, 2, 2, 600, 600, 0, 0);
            using var doc = PdfDocument.Open(outPath);
            Assert.Equal(1, doc.PageCount);
            Assert.True(IsRed(TestPdf.SamplePixel(outPath, 0, 0.25, 0.25)), "TL = page1 RED");
            Assert.True(IsGreen(TestPdf.SamplePixel(outPath, 0, 0.75, 0.25)), "TR = page2 GREEN");
            Assert.True(IsBlue(TestPdf.SamplePixel(outPath, 0, 0.25, 0.75)), "BL = page3 BLUE");
            Assert.True(IsTeal(TestPdf.SamplePixel(outPath, 0, 0.75, 0.75)), "BR = page4 TEAL");
        }
        finally { try { System.IO.File.Delete(outPath); } catch { } }
    }

    [Fact]
    public async Task Booklet_FourPages_OrdersAndPlacesTwoUp()
    {
        var src = ColorPagesDoc("#D03030", "#30A040", "#3050D0", "#00B0B0"); // pages 1R 2G 3B 4teal
        var outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"chuvadi-bk-{Guid.NewGuid():N}.pdf");
        try
        {
            await new BenchComposer().ImposeBookletAsync(src, outPath, 600, 400, 0, 0);
            using var doc = PdfDocument.Open(outPath);
            Assert.Equal(2, doc.PageCount); // 4 pages → 2 sheet sides
            // sheet 0 = [page4, page1]
            Assert.True(IsTeal(TestPdf.SamplePixel(outPath, 0, 0.25, 0.5)), "sheet0 left = page4 TEAL");
            Assert.True(IsRed(TestPdf.SamplePixel(outPath, 0, 0.75, 0.5)), "sheet0 right = page1 RED");
            // sheet 1 = [page2, page3]
            Assert.True(IsGreen(TestPdf.SamplePixel(outPath, 1, 0.25, 0.5)), "sheet1 left = page2 GREEN");
            Assert.True(IsBlue(TestPdf.SamplePixel(outPath, 1, 0.75, 0.5)), "sheet1 right = page3 BLUE");
        }
        finally { try { System.IO.File.Delete(outPath); } catch { } }
    }

}
