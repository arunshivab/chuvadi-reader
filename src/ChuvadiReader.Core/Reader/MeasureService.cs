using System.Globalization;
using Chuvadi.Pdf.Documents;
using ChuvadiReader.Core.Documents;

namespace ChuvadiReader.Core.Reader;

/// <summary>A point on a page as the overlays use it: fractions 0..1 of the page as displayed
/// (after the page's /Rotate plus any view rotation), origin top-left, y down.</summary>
public readonly record struct PagePoint(double X, double Y);

/// <summary>Where the scale of a measurement came from.</summary>
public enum MeasureScaleSource
{
    /// <summary>No measure dictionary covers the points: 1:1 of PDF user space (1/72 in × /UserUnit).</summary>
    PdfUserSpace,

    /// <summary>The page's viewport (/VP /Measure) scale, e.g. "1 in = 10 ft".</summary>
    FromFile,
}

/// <summary>Result of measuring the straight line between two points.</summary>
/// <param name="PageIndex">Zero-based page.</param>
/// <param name="PaperPoints">Length on paper in points (1/72 in), /UserUnit applied.</param>
/// <param name="PaperMm">Length on paper in millimetres.</param>
/// <param name="PaperIn">Length on paper in inches.</param>
/// <param name="DistanceMm">Real-world length in mm: the file's scale applied when it has one in a
/// length unit the app knows (mm, cm, m, km, in, ft, yd, mi); otherwise equal to <see cref="PaperMm"/>.</param>
/// <param name="DistanceIn">Real-world length in inches (same rule as <see cref="DistanceMm"/>).</param>
/// <param name="FileValue">The file's measured value in its first unit, when a file scale applied.</param>
/// <param name="FileUnit">That unit's label (e.g. "ft").</param>
/// <param name="FileFormatted">The value formatted as the file asks (e.g. "5 ft 3 in").</param>
/// <param name="AngleDegrees">Direction of the line as displayed, counter-clockwise from the
/// positive x axis (a protractor reading), in (-180, 180].</param>
/// <param name="ScaleLabel">"1 : 1" or the file's ratio, e.g. "1 in = 10 ft (from file)".</param>
/// <param name="ScaleSource">Where the scale came from.</param>
/// <param name="ViewportName">The governing viewport's name, when the file names it.</param>
public sealed record MeasureResult(
    int PageIndex,
    double PaperPoints,
    double PaperMm,
    double PaperIn,
    double DistanceMm,
    double DistanceIn,
    double? FileValue,
    string? FileUnit,
    string? FileFormatted,
    double AngleDegrees,
    string ScaleLabel,
    MeasureScaleSource ScaleSource,
    string? ViewportName)
{
    /// <summary>Headline text for the card/overlay: the file's own formatting when a file scale
    /// applied, otherwise millimetres, e.g. "176.0 mm".</summary>
    public string DistanceLabel => FileFormatted ?? (PaperMm.ToString("0.0", CultureInfo.InvariantCulture) + " mm");

    /// <summary>The angle as text, e.g. "0.0°".</summary>
    public string AngleLabel => AngleDegrees.ToString("0.0", CultureInfo.InvariantCulture) + "°";
}

/// <summary>Result of measuring a polyline (and, when closed, the polygon it encloses).</summary>
public sealed record PolylineMeasureResult(
    int PageIndex,
    int PointCount,
    bool Closed,
    double PaperLengthMm,
    double PaperLengthIn,
    double? PaperAreaMm2,
    double? PaperAreaIn2,
    string? FileLengthFormatted,
    string? FileAreaFormatted,
    string ScaleLabel,
    MeasureScaleSource ScaleSource)
{
    public string LengthLabel => FileLengthFormatted ?? (PaperLengthMm.ToString("0.0", CultureInfo.InvariantCulture) + " mm");

    public string? AreaLabel => FileAreaFormatted
        ?? (PaperAreaMm2 is { } a ? a.ToString("0.0", CultureInfo.InvariantCulture) + " mm²" : null);
}

/// <summary>
/// Measures distances, angles and areas on a page. The scale is the page viewport's measure
/// dictionary (/VP /Measure, PDF 32000-1 §12.9) when one covers the points — read and
/// converted by the library's <see cref="PdfMeasure"/> — otherwise 1:1 of PDF user space.
/// Points are page fractions, as the app's overlays use; they're mapped to default user
/// space with the same rotation convention as the redaction/stamp mapping.
/// </summary>
public sealed class MeasureService
{
    private const double MmPerInch = 25.4;
    private const double PointsPerInch = 72.0;

    /// <summary>Measures on an open session's document (no re-open). Returns null when the
    /// session isn't backed by chuvadi-pdf (e.g. the design-time placeholder) or the page is out of range.</summary>
    public MeasureResult? Measure(IPdfSession session, int pageIndex, PagePoint a, PagePoint b, int viewRotation = 0)
    {
        ArgumentNullException.ThrowIfNull(session);
        // D-152: never waits for a page that is rendering; the next pointer move measures again.
        if (session is not ChuvadiPdfSession pdf
            || !pdf.TryUseDocument(doc => pageIndex >= 0 && pageIndex < doc.PageCount ? Measure(doc.Pages[pageIndex], a, b, viewRotation) : null, out var result))
        {
            return null;
        }

        return result;
    }

    /// <summary>Measures on a file (opened and closed for the call).</summary>
    public MeasureResult Measure(string pdfPath, int pageIndex, PagePoint a, PagePoint b, int viewRotation = 0, string? password = null)
    {
        using var doc = string.IsNullOrEmpty(password) ? PdfDocument.Open(pdfPath) : PdfDocument.Open(pdfPath, password);
        if (pageIndex < 0 || pageIndex >= doc.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        return Measure(doc.Pages[pageIndex], a, b, viewRotation);
    }

    /// <summary>Measures a polyline on an open session; closed polylines also report the area.</summary>
    public PolylineMeasureResult? MeasurePolyline(IPdfSession session, int pageIndex, IReadOnlyList<PagePoint> points, bool closed, int viewRotation = 0)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is not ChuvadiPdfSession pdf
            || !pdf.TryUseDocument(doc => pageIndex >= 0 && pageIndex < doc.PageCount ? MeasurePolyline(doc.Pages[pageIndex], points, closed, viewRotation) : null, out var result))
        {
            return null;
        }

        return result;
    }

    /// <summary>Measures a polyline on a file.</summary>
    public PolylineMeasureResult MeasurePolyline(string pdfPath, int pageIndex, IReadOnlyList<PagePoint> points, bool closed, int viewRotation = 0, string? password = null)
    {
        using var doc = string.IsNullOrEmpty(password) ? PdfDocument.Open(pdfPath) : PdfDocument.Open(pdfPath, password);
        if (pageIndex < 0 || pageIndex >= doc.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        return MeasurePolyline(doc.Pages[pageIndex], points, closed, viewRotation);
    }

    // ── core ─────────────────────────────────────────────────────────────────

    private static MeasureResult Measure(PdfPage page, PagePoint a, PagePoint b, int viewRotation)
    {
        var geo = PageGeometry.Of(page, viewRotation);
        var (x1, y1) = geo.ToUserSpace(a);
        var (x2, y2) = geo.ToUserSpace(b);

        double userLen = Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));
        double paperPt = userLen * page.UserUnit;
        double paperIn = paperPt / PointsPerInch;
        double paperMm = paperIn * MmPerInch;

        // Angle as the user sees it: displayed axes, y up.
        double dx = (b.X - a.X) * geo.DisplayWidth;
        double dy = (a.Y - b.Y) * geo.DisplayHeight;
        double angle = (dx == 0 && dy == 0) ? 0 : Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle <= -180)
        {
            angle += 360;
        }

        double? fileValue = null;
        string? fileUnit = null, fileFormatted = null, viewportName = null;
        string scaleLabel = "1 : 1";
        var source = MeasureScaleSource.PdfUserSpace;
        double distMm = paperMm;

        var vp = GoverningViewport(page, (x1, y1), (x2, y2));
        if (vp?.Measure is { IsRectilinear: true } measure)
        {
            try
            {
                var m = measure.Distance(x1, y1, x2, y2);
                fileValue = m.Value;
                fileUnit = m.Unit;
                fileFormatted = string.IsNullOrWhiteSpace(m.Formatted) ? FormatValue(m.Value, m.Unit) : m.Formatted;
                viewportName = vp.Name;
                source = MeasureScaleSource.FromFile;
                scaleLabel = ScaleLabelFor(measure);
                if (UnitToMm(m.Unit) is { } mmPerUnit)
                {
                    distMm = m.Value * mmPerUnit;
                }
            }
            catch (InvalidOperationException)
            {
                // Not convertible after all — stay at 1:1.
            }
        }

        return new MeasureResult(
            page.Index, paperPt, paperMm, paperIn, distMm, distMm / MmPerInch,
            fileValue, fileUnit, fileFormatted, angle, scaleLabel, source, viewportName);
    }

    private static PolylineMeasureResult MeasurePolyline(PdfPage page, IReadOnlyList<PagePoint> points, bool closed, int viewRotation)
    {
        ArgumentNullException.ThrowIfNull(points);
        var geo = PageGeometry.Of(page, viewRotation);
        var user = points.Select(geo.ToUserSpace).ToList();

        double lenUser = 0;
        for (int i = 1; i < user.Count; i++)
        {
            lenUser += Dist(user[i - 1], user[i]);
        }

        bool isPolygon = closed && user.Count >= 3;
        if (isPolygon)
        {
            lenUser += Dist(user[^1], user[0]);
        }

        double areaUser = 0;
        if (isPolygon)
        {
            for (int i = 0; i < user.Count; i++)
            {
                var p = user[i];
                var q = user[(i + 1) % user.Count];
                areaUser += (p.X * q.Y) - (q.X * p.Y);
            }

            areaUser = Math.Abs(areaUser) / 2.0;
        }

        double uu = page.UserUnit;
        double lenIn = lenUser * uu / PointsPerInch;
        double? areaIn2 = isPolygon ? areaUser * uu * uu / (PointsPerInch * PointsPerInch) : null;

        string? fileLen = null, fileArea = null;
        string scaleLabel = "1 : 1";
        var source = MeasureScaleSource.PdfUserSpace;
        if (user.Count >= 2)
        {
            var vps = page.Viewports;
            int first = LastContaining(vps, user[0]);
            var vp = first >= 0 && user.All(p => LastContaining(vps, p) == first) ? vps[first] : null;
            if (vp?.Measure is { IsRectilinear: true } measure)
            {
                try
                {
                    double total = 0;
                    string? unit = null;
                    for (int i = 1; i < user.Count; i++)
                    {
                        var m = measure.Distance(user[i - 1].X, user[i - 1].Y, user[i].X, user[i].Y);
                        total += m.Value;
                        unit = m.Unit;
                    }

                    if (isPolygon)
                    {
                        var m = measure.Distance(user[^1].X, user[^1].Y, user[0].X, user[0].Y);
                        total += m.Value;
                        unit = m.Unit;
                        var am = measure.Area(areaUser);
                        fileArea = string.IsNullOrWhiteSpace(am.Formatted) ? FormatValue(am.Value, am.Unit) : am.Formatted;
                    }

                    fileLen = FormatValue(total, unit);
                    scaleLabel = ScaleLabelFor(measure);
                    source = MeasureScaleSource.FromFile;
                }
                catch (InvalidOperationException)
                {
                    fileLen = null;
                    fileArea = null;
                }
            }
        }

        return new PolylineMeasureResult(
            page.Index, points.Count, isPolygon,
            lenIn * MmPerInch, lenIn,
            areaIn2 * MmPerInch * MmPerInch, areaIn2,
            fileLen, fileArea, scaleLabel, source);
    }

    private static double Dist((double X, double Y) p, (double X, double Y) q) =>
        Math.Sqrt(((q.X - p.X) * (q.X - p.X)) + ((q.Y - p.Y) * (q.Y - p.Y)));

    /// <summary>The viewport that governs both points (the spec's "last one containing it"),
    /// or null when they fall under different viewports or none.</summary>
    private static PdfViewport? GoverningViewport(PdfPage page, (double X, double Y) p, (double X, double Y) q)
    {
        // The spec's rule is "the last viewport containing the point", so compare by position in
        // the page's list (the library reads it once per page since chuvadi-pdf 3.32.0).
        var vps = page.Viewports;
        int ip = LastContaining(vps, p), iq = LastContaining(vps, q);
        return ip >= 0 && ip == iq ? vps[ip] : null;
    }

    private static int LastContaining(IReadOnlyList<PdfViewport> vps, (double X, double Y) pt)
    {
        for (int i = vps.Count - 1; i >= 0; i--)
        {
            if (vps[i].Contains(pt.X, pt.Y)) return i;
        }

        return -1;
    }

    private static string ScaleLabelFor(PdfMeasure measure) =>
        (string.IsNullOrWhiteSpace(measure.ScaleRatio) ? "custom scale" : measure.ScaleRatio.Trim()) + " (from file)";

    private static string FormatValue(double value, string? unit) =>
        value.ToString("0.##", CultureInfo.InvariantCulture) + (string.IsNullOrWhiteSpace(unit) ? string.Empty : " " + unit.Trim());

    /// <summary>Millimetres per one of <paramref name="unit"/>, for the length units a drawing's
    /// /NumberFormat commonly uses; null for anything else (the file's own text is still shown).</summary>
    internal static double? UnitToMm(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "mm" or "millimetre" or "millimetres" or "millimeter" or "millimeters" => 1,
        "cm" or "centimetre" or "centimetres" or "centimeter" or "centimeters" => 10,
        "m" or "metre" or "metres" or "meter" or "meters" => 1000,
        "km" or "kilometre" or "kilometres" or "kilometer" or "kilometers" => 1_000_000,
        "in" or "inch" or "inches" or "\"" => 25.4,
        "ft" or "foot" or "feet" or "'" => 304.8,
        "yd" or "yard" or "yards" => 914.4,
        "mi" or "mile" or "miles" => 1_609_344,
        "pt" or "points" => 25.4 / 72,
        _ => null,
    };

    /// <summary>Maps displayed-page fractions to default user space: undo the display rotation
    /// (same convention as RedactService.MapToPdf), then scale to the shown box (D-160: the CropBox within the
    /// MediaBox, what the page shows) and flip y.</summary>
    private readonly record struct PageGeometry(double X0, double Y0, double W, double H, int Rotation)
    {
        public static PageGeometry Of(PdfPage page, int viewRotation)
        {
            var shown = ShownBox.Of(page);
            int rot = (((page.Rotate + viewRotation) % 360) + 360) % 360;
            return new PageGeometry(shown.X0, shown.Y0, shown.W, shown.H, rot);
        }

        public double DisplayWidth => Rotation is 90 or 270 ? H : W;

        public double DisplayHeight => Rotation is 90 or 270 ? W : H;

        public (double X, double Y) ToUserSpace(PagePoint p)
        {
            var (ux, uy) = Rotation switch
            {
                90 => (p.Y, 1 - p.X),
                180 => (1 - p.X, 1 - p.Y),
                270 => (1 - p.Y, p.X),
                _ => (p.X, p.Y),
            };
            return (X0 + (ux * W), Y0 + ((1 - uy) * H));
        }
    }
}
