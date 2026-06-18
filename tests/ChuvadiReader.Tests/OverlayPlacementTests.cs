using ChuvadiReader.Core.Documents;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>Regression guards for the v40 overlay pipeline. These encode the empirical findings
/// from building Add Image + shapes: top-left/y-down placement, multiple items coexisting on one
/// page (the "PageStamper can't stack" guard), and lines drawn at the angle they were given.</summary>
public class OverlayPlacementTests
{
    private static readonly Dictionary<int, PageRedaction> NoRedactions = new();
    private static readonly Dictionary<int, PageText> NoText = new();
    private static readonly Dictionary<int, PageImage> NoImages = new();
    private static readonly Dictionary<int, PageShape> NoShapes = new();

    [Fact]
    public async Task FilledRectangle_LandsWhereItIsPlaced()
    {
        var src = TestPdf.BlankPage(200, 200);
        var outp = TestPdf.TempOutput();

        var shapes = new Dictionary<int, PageShape>
        {
            [0] = new PageShape(0, new[]
            {
                new ShapeAnno(0.25, 0.25, 0.50, 0.50, ShapeKind.Rectangle, "#FF0000", "#FF0000", 1, 0),
            }),
        };

        await new RedactService().FlattenToFileAsync(src, outp, NoRedactions, "#000000", NoText, NoImages, shapes);

        var centre = TestPdf.SamplePixel(outp, 0, 0.50, 0.50);
        var corner = TestPdf.SamplePixel(outp, 0, 0.05, 0.05);

        Assert.True(TestPdf.IsReddish(centre), $"centre should be red but was {centre}");
        Assert.True(TestPdf.IsWhitish(corner), $"corner should be white but was {corner}");
    }

    [Fact]
    public async Task TwoRectangles_BothSurvive_NoStampClobber()
    {
        // The core regression: stamping the second item must not drop the first.
        var src = TestPdf.BlankPage(200, 200);
        var outp = TestPdf.TempOutput();

        var shapes = new Dictionary<int, PageShape>
        {
            [0] = new PageShape(0, new[]
            {
                new ShapeAnno(0.05, 0.05, 0.35, 0.35, ShapeKind.Rectangle, "#FF0000", "#FF0000", 1, 0), // top-left
                new ShapeAnno(0.60, 0.60, 0.35, 0.35, ShapeKind.Rectangle, "#0000FF", "#0000FF", 1, 0), // bottom-right
            }),
        };

        await new RedactService().FlattenToFileAsync(src, outp, NoRedactions, "#000000", NoText, NoImages, shapes);

        var topLeft = TestPdf.SamplePixel(outp, 0, 0.22, 0.22);
        var bottomRight = TestPdf.SamplePixel(outp, 0, 0.77, 0.77);

        Assert.True(TestPdf.IsReddish(topLeft), $"first (red) rect missing — was {topLeft}");
        Assert.True(TestPdf.IsBluish(bottomRight), $"second (blue) rect missing — was {bottomRight}");
    }

    [Fact]
    public async Task Line_IsDrawnAtItsAngle_NotAxisAligned()
    {
        var src = TestPdf.BlankPage(200, 200);
        var outp = TestPdf.TempOutput();

        // Diagonal top-left → bottom-right, thick enough to sample reliably.
        var shapes = new Dictionary<int, PageShape>
        {
            [0] = new PageShape(0, new[]
            {
                new ShapeAnno(0.10, 0.10, 0.80, 0.80, ShapeKind.Line, null, "#000000", 6, 0),
            }),
        };

        await new RedactService().FlattenToFileAsync(src, outp, NoRedactions, "#000000", NoText, NoImages, shapes);

        var onDiagonal = TestPdf.SamplePixel(outp, 0, 0.50, 0.50);  // midpoint of the diagonal
        var offDiagonal = TestPdf.SamplePixel(outp, 0, 0.50, 0.15); // above the diagonal → white

        Assert.True(TestPdf.IsDark(onDiagonal), $"diagonal midpoint should be dark — was {onDiagonal}");
        Assert.True(TestPdf.IsWhitish(offDiagonal), $"off-diagonal should be white — was {offDiagonal}");
    }

    [Fact]
    public async Task Image_LandsWhereItIsPlaced()
    {
        var src = TestPdf.BlankPage(200, 200);
        var outp = TestPdf.TempOutput();
        var greenPng = TestPdf.SolidPng(40, 40, 0, 180, 0);

        var images = new Dictionary<int, PageImage>
        {
            [0] = new PageImage(0, new[]
            {
                new ImageAnno(0.55, 0.55, 0.35, 0.35, greenPng, 0, 1.0),
            }),
        };

        await new RedactService().FlattenToFileAsync(src, outp, NoRedactions, "#000000", NoText, images, NoShapes);

        var inImage = TestPdf.SamplePixel(outp, 0, 0.72, 0.72);
        var outside = TestPdf.SamplePixel(outp, 0, 0.10, 0.10);

        Assert.True(TestPdf.IsGreenish(inImage), $"image area should be green — was {inImage}");
        Assert.True(TestPdf.IsWhitish(outside), $"outside the image should be white — was {outside}");
    }

    [Fact]
    public async Task ShapeAndImage_CoexistOnSamePage()
    {
        var src = TestPdf.BlankPage(200, 200);
        var outp = TestPdf.TempOutput();
        var greenPng = TestPdf.SolidPng(40, 40, 0, 180, 0);

        var shapes = new Dictionary<int, PageShape>
        {
            [0] = new PageShape(0, new[]
            {
                new ShapeAnno(0.05, 0.05, 0.35, 0.35, ShapeKind.Rectangle, "#FF0000", "#FF0000", 1, 0),
            }),
        };
        var images = new Dictionary<int, PageImage>
        {
            [0] = new PageImage(0, new[]
            {
                new ImageAnno(0.60, 0.60, 0.35, 0.35, greenPng, 0, 1.0),
            }),
        };

        await new RedactService().FlattenToFileAsync(src, outp, NoRedactions, "#000000", NoText, images, shapes);

        var rectPixel = TestPdf.SamplePixel(outp, 0, 0.22, 0.22);
        var imagePixel = TestPdf.SamplePixel(outp, 0, 0.77, 0.77);

        Assert.True(TestPdf.IsReddish(rectPixel), $"rect missing when combined with image — was {rectPixel}");
        Assert.True(TestPdf.IsGreenish(imagePixel), $"image missing when combined with rect — was {imagePixel}");
    }

    [Fact]
    public async Task NothingToSave_Throws()
    {
        var src = TestPdf.BlankPage();
        var outp = TestPdf.TempOutput();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RedactService().FlattenToFileAsync(src, outp, NoRedactions, "#000000", NoText, NoImages, NoShapes));
    }
}
