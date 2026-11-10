using Chuvadi.Pdf.Documents;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>What chuvadi-pdf 3.32.0 unlocked: bookmarks that land on their heading, and layers
/// that share a name kept apart.</summary>
public class Unlock332Tests
{
    /// <summary>Two pages (200 × 400, the second turned 90° and cropped) and four bookmarks:
    /// /XYZ half-way down page 1, /Fit on page 1, /FitH near the top of page 1, /XYZ on page 2.</summary>
    private static string OutlinePdf() => LayersMeasureSignatureTests.RawPdf(
        "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 400] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 400] /CropBox [50 100 150 300] /Rotate 90 >>",
        "<< /Type /Outlines /First 6 0 R /Last 9 0 R /Count 4 >>",
        "<< /Title (Half way) /Parent 5 0 R /Next 7 0 R /Dest [3 0 R /XYZ 0 200 0] >>",
        "<< /Title (Whole page) /Parent 5 0 R /Prev 6 0 R /Next 8 0 R /Dest [3 0 R /Fit] >>",
        "<< /Title (Near the top) /Parent 5 0 R /Prev 7 0 R /Next 9 0 R /Dest [3 0 R /FitH 360] >>",
        "<< /Title (Turned page) /Parent 5 0 R /Prev 8 0 R /Dest [4 0 R /XYZ 75 300 null] >>");

    [Fact]
    public void A_bookmark_lands_on_the_position_the_file_gives()
    {
        var nodes = new OutlineService().Read(OutlinePdf());

        Assert.Equal(new[] { "Half way", "Whole page", "Near the top", "Turned page" }, nodes.Select(n => n.Title));
        Assert.Equal(new[] { 0, 0, 0, 1 }, nodes.Select(n => n.PageIndex));

        Assert.Equal(0.5, nodes[0].TopFraction!.Value, 3);   // y = 200 of 400
        Assert.Null(nodes[1].TopFraction);                    // /Fit: no position → top of the page
        Assert.Equal(0.1, nodes[2].TopFraction!.Value, 3);   // y = 360 of 400
        // Page 2 is turned 90°: the displayed top is the CropBox's left edge (x = 50..150), so x = 75 is a quarter down.
        Assert.Equal(0.25, nodes[3].TopFraction!.Value, 3);

        Assert.Equal(0.5, nodes[0].Clone().TopFraction);
    }

    /// <summary>Two layers both named "Notes" (one shown, one hidden by default) and one locked layer.</summary>
    private static string SameNamedLayersPdf()
    {
        const string content =
            "/OC /L1 BDC 1 0 0 rg 10 10 80 80 re f EMC " +
            "/OC /L2 BDC 0 1 0 rg 110 10 80 80 re f EMC " +
            "/OC /L3 BDC 0 0 1 rg 10 110 80 80 re f EMC";
        return LayersMeasureSignatureTests.RawPdf(
            "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [4 0 R 5 0 R 6 0 R] /D << /ON [4 0 R 6 0 R] /OFF [5 0 R] /Locked [6 0 R] >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Properties << /L1 4 0 R /L2 5 0 R /L3 6 0 R >> >> /Contents 7 0 R >>",
            "<< /Type /OCG /Name (Notes) >>",
            "<< /Type /OCG /Name (Notes) >>",
            "<< /Type /OCG /Name (Frame) >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
    }

    [Fact]
    public async Task Layers_that_share_a_name_are_listed_and_toggled_separately()
    {
        var path = SameNamedLayersPdf();
        var before = File.ReadAllBytes(path);
        using var session = await new ChuvadiPdfReader().OpenAsync(path, "layers.pdf");

        var layers = session.Layers;
        Assert.Equal(3, layers.Count);
        var notes = layers.Where(l => l.Name == "Notes").ToList();
        Assert.Equal(2, notes.Count);
        Assert.NotEqual(notes[0].Id, notes[1].Id);
        Assert.All(notes, l => Assert.Equal(1, l.GroupCount));
        Assert.True(notes[0].IsVisible);
        Assert.False(notes[1].IsVisible);
        Assert.True(layers.Single(l => l.Name == "Frame").IsLocked);

        string shownOnly = await session.RenderPageSvgAsync(0);

        // Showing the second "Notes" changes the page and leaves the first "Notes" as it was.
        session.SetLayerVisible(notes[1].Id, true);
        var after = session.Layers.Where(l => l.Name == "Notes").ToList();
        Assert.True(after[0].IsVisible);
        Assert.True(after[1].IsVisible);
        Assert.False(after[0].IsOverridden);
        Assert.True(after[1].IsOverridden);
        string bothShown = await session.RenderPageSvgAsync(0);
        Assert.NotEqual(shownOnly, bothShown);

        // Hiding the first one alone gives a third, different page.
        session.SetLayerVisible(notes[0].Id, false);
        string secondOnly = await session.RenderPageSvgAsync(0);
        Assert.NotEqual(bothShown, secondOnly);
        Assert.NotEqual(shownOnly, secondOnly);

        // The locked layer can't be toggled, and the file is never written to.
        var frame = session.Layers.Single(l => l.Name == "Frame");
        session.SetLayerVisible(frame.Id, false);
        Assert.True(session.Layers.Single(l => l.Name == "Frame").IsVisible);

        session.ResetLayerVisibility();
        Assert.Equal(shownOnly, await session.RenderPageSvgAsync(0));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
