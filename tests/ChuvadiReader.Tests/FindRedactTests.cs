using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Text;
using ChuvadiReader.Core.Documents;
using Xunit;
using Path = System.IO.Path;

namespace ChuvadiReader.Tests;

/// <summary>Guards for find-and-redact. Assertions use text extraction (not rendering) so they
/// never touch the rasterizer; what matters for redaction is that text is gone.</summary>
public class FindRedactTests
{
    private static string DocWith(string line, double w = 460, double h = 160)
    {
        var b = PdfDocumentBuilder.Create();
        var pb = b.AddPage(new PageSize(w, h));
        pb.DrawRectangle(0, 0, w, h, Color.FromHex("#FFFFFF"), Color.FromHex("#FFFFFF"), 0);
        pb.DrawText(line, 24, 100, "Helvetica", 14, Color.FromHex("#000000"));
        return TestPdf.WriteTemp(b.ToByteArray());
    }

    private static string PageText(string pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        var ex = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);
        return string.Concat(Enumerable.Range(0, doc.PageCount).Select(p => ex.ExtractText(doc.Pages[p])));
    }

    [Fact]
    public async Task Search_FindsAllOccurrences()
    {
        var src = DocWith("Amount one. Amount two. Total amount three.");
        var matches = await new RedactService().SearchAsync(src, "amount", caseSensitive: false, wholeWord: false);
        Assert.Equal(3, matches.Count);
        Assert.All(matches, m => Assert.NotEmpty(m.Boxes));
        Assert.All(matches, m => Assert.All(m.Boxes, b =>
        {
            Assert.InRange(b.X, 0, 1); Assert.InRange(b.Y, 0, 1);
        }));
    }

    [Fact]
    public async Task Search_CaseSensitive_Distinguishes()
    {
        var src = DocWith("Amount and amount and AMOUNT.");
        var insensitive = await new RedactService().SearchAsync(src, "Amount", caseSensitive: false, wholeWord: false);
        var sensitive = await new RedactService().SearchAsync(src, "Amount", caseSensitive: true, wholeWord: false);
        Assert.True(insensitive.Count >= 3);
        Assert.True(sensitive.Count < insensitive.Count);
    }

    [Fact]
    public async Task Search_WholeWord_ExcludesSubstrings()
    {
        var src = DocWith("Ann visited the anniversary at Annapolis.");
        var loose = await new RedactService().SearchAsync(src, "Ann", caseSensitive: false, wholeWord: false);
        var whole = await new RedactService().SearchAsync(src, "Ann", caseSensitive: false, wholeWord: true);
        Assert.True(whole.Count < loose.Count);
    }

    [Fact]
    public async Task Box_Redaction_RemovesMatchedText_OnUntagged()
    {
        var src = DocWith("Secret amount here.");
        var svc = new RedactService();
        var matches = await svc.SearchAsync(src, "amount", false, false);
        var boxes = matches.GroupBy(m => m.PageIndex)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<NormBox>)g.SelectMany(m => m.Boxes).ToList());

        var outp = TestPdf.TempOutput();
        await svc.ApplyAsync(src, outp, boxes, Array.Empty<Chuvadi.Pdf.Redaction.PatternRule>(),
            glyphOnly: false, overlayHex: "#000000", padding: 1.5);

        Assert.DoesNotContain("amount", PageText(outp), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GlyphOnly_Redaction_RemovesMatchedText_OnUntagged()
    {
        var src = DocWith("Confidential amount value.");
        var svc = new RedactService();
        var matches = await svc.SearchAsync(src, "amount", false, false);
        var boxes = matches.GroupBy(m => m.PageIndex)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<NormBox>)g.SelectMany(m => m.Boxes).ToList());

        var outp = TestPdf.TempOutput();
        await svc.ApplyAsync(src, outp, boxes, Array.Empty<Chuvadi.Pdf.Redaction.PatternRule>(),
            glyphOnly: true, overlayHex: "#000000", padding: 1.5);

        Assert.DoesNotContain("amount", PageText(outp), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pattern_Redaction_IsKnownUnreliable_PendingLibraryFix()
    {
        // The library's pattern engine does not reliably remove regex matches (it removed
        // nothing for a literal, an SSN, and emails in isolation). The regex/preset UI is
        // gated off until the library is fixed. This test documents the current behaviour so
        // we notice if/when a library bump changes it.
        var src = DocWith("Reach me at john@example.com any time.");
        var svc = new RedactService();
        var patterns = RedactService.BuildPatternRules(new[] { Chuvadi.Pdf.Redaction.CommonPatterns.Email });

        var outp = TestPdf.TempOutput();
        await svc.ApplyAsync(src, outp,
            new Dictionary<int, IReadOnlyList<NormBox>>(), patterns,
            glyphOnly: false, overlayHex: "#000000", padding: 1.5);

        // Currently NOT removed — when this starts failing, the library has fixed patterns
        // and we can re-enable the regex/preset UI.
        Assert.Contains("john@example.com", PageText(outp), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_BoxY_IsTopLeft_NotFlipped()
    {
        // Guard against the bottom-left/top-left mixup: a word near the TOP must normalise to a
        // small Y, a word near the BOTTOM to a large Y. (Search returns bottom-left PDF space;
        // SearchAsync must flip it to the app's top-left fractions.)
        double w = 595, h = 842;
        var b = PdfDocumentBuilder.Create();
        var pb = b.AddPage(new PageSize(w, h));
        pb.DrawRectangle(0, 0, w, h, Color.FromHex("#FFFFFF"), Color.FromHex("#FFFFFF"), 0);
        pb.DrawText("Topmark here", 60, 80, "Helvetica-Bold", 20, Color.FromHex("#000000"));
        pb.DrawText("Botmark here", 60, 760, "Helvetica-Bold", 20, Color.FromHex("#000000"));
        var src = TestPdf.WriteTemp(b.ToByteArray());

        var svc = new RedactService();
        var top = (await svc.SearchAsync(src, "Topmark", false, false)).Single();
        var bot = (await svc.SearchAsync(src, "Botmark", false, false)).Single();

        Assert.True(top.Boxes[0].Y < 0.30, $"top word Y should be near top, was {top.Boxes[0].Y:0.000}");
        Assert.True(bot.Boxes[0].Y > 0.60, $"bottom word Y should be near bottom, was {bot.Boxes[0].Y:0.000}");
    }

    [Fact]
    public void Presets_ExposeFinancialAndMedical()
    {
        Assert.Contains("Financial", RedactService.PatternPresets.Keys);
        Assert.Contains("Medical", RedactService.PatternPresets.Keys);
        Assert.NotEmpty(RedactService.PatternPresets["Financial"]);
        Assert.NotEmpty(RedactService.PatternPresets["Medical"]);
    }
}
