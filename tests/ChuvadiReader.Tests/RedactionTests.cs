using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Text;
using ChuvadiReader.Core.Documents;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>Redaction guards. Authored fixtures are untagged, so true removal should succeed and
/// the verify-after-save must pass (no <see cref="RedactionNotRemovedException"/>).</summary>
public class RedactionTests
{
    private static readonly Dictionary<int, PageText> NoText = new();

    [Fact]
    public async Task Redaction_RemovesText_OnUntaggedPdf()
    {
        const string secret = "TOPSECRET";
        var src = TestPdf.TextPage(secret);
        var outp = TestPdf.TempOutput();

        // Cover the whole page so the text certainly falls inside the rectangle.
        var pages = new Dictionary<int, PageRedaction>
        {
            [0] = new PageRedaction(0, new[] { new NormBox(0, 0, 1, 1) }),
        };

        // Should not throw (untagged → removal verified clean).
        await new RedactService().RedactToFileAsync(src, outp, pages, "#000000");

        // And the text must be gone from the output.
        using var doc = PdfDocument.Open(outp);
        var extractor = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);
        var text = string.Concat(extractor.ExtractFragments(doc.Pages[0]).Select(f => f.Text));
        Assert.DoesNotContain("SECRET", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsTagged_IsFalse_ForAuthoredPdf()
    {
        var src = TestPdf.TextPage("hello");
        Assert.False(new RedactService().IsTagged(src));
    }
}
