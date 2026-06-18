namespace ChuvadiReader.Core.Reader;

/// <summary>
/// A source document loaded onto the Bench shelf. Its <see cref="Session"/> stays
/// open so page thumbnails can be rendered on demand; <see cref="Index"/> drives the
/// per-source colour stripe shown on each page.
/// </summary>
public sealed class BenchSource
{
    public required int Index { get; init; }

    public required string Path { get; init; }

    public required string FileName { get; init; }

    public required IPdfSession Session { get; init; }

    public int PageCount => Session.Document.PageCount;

    /// <summary>Shelf UI state: when true the source's page strip is hidden and only
    /// its header shows (which can still be dragged as a whole file).</summary>
    public bool Collapsed { get; set; }
}

/// <summary>
/// One page sitting in a desk. It usually points back at a source page (by path and
/// original index) and carries a rotation; a <see cref="IsBlank"/> page has no source
/// and is emitted fresh at compose time, optionally with a background colour. The
/// stable <see cref="Id"/> is used for selection and drag.
/// </summary>
public sealed class BenchPage
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string SourcePath { get; init; }

    public required string SourceName { get; init; }

    /// <summary>Which source this came from (for the colour stripe); -1 for a blank.</summary>
    public required int SourceIndex { get; init; }

    /// <summary>Zero-based page index within the source document; -1 for a blank.</summary>
    public required int OriginalIndex { get; init; }

    /// <summary>Clockwise rotation in degrees: 0, 90, 180, or 270.</summary>
    public int Rotation { get; set; }

    /// <summary>A blank inserted page (no source). Emitted fresh at compose time.</summary>
    public bool IsBlank { get; init; }

    /// <summary>Optional background colour for a blank page, as #RRGGBB. Null = white.</summary>
    public string? BackgroundHex { get; init; }

    /// <summary>A raw image dropped straight into a desk. It stays an image until Bind,
    /// where it is converted to a one-page PDF and woven in. No source page number.</summary>
    public bool IsImage { get; init; }

    /// <summary>Path to the raw image on disk for an image page (null otherwise).</summary>
    public string? ImagePath { get; init; }
}

/// <summary>An optional text watermark applied to a desk's output at Bind/Lift time.</summary>
public sealed class DeskWatermark
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Base font family: "Helvetica", "Times", or "Courier".</summary>
    public string FontFamily { get; set; } = "Helvetica";

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public double FontSize { get; set; } = 48;

    /// <summary>Stamp colour as #RRGGBB.</summary>
    public string ColorHex { get; set; } = "#808080";

    /// <summary>0..1 opacity of the stamp.</summary>
    public double Opacity { get; set; } = 0.25;

    /// <summary>Rotation in degrees: 0 horizontal, 90 vertical, 45 diagonal ↗, 315 diagonal ↘.</summary>
    public double RotationDegrees { get; set; } = 45;

    /// <summary>When true the watermark hits every page; otherwise only selected pages.</summary>
    public bool AllPages { get; set; } = true;

    /// <summary>Maps family + bold/italic to a standard PDF base-font name.</summary>
    public string ResolveFontName()
    {
        var fam = FontFamily?.Trim();
        return fam switch
        {
            "Times" => (Bold, Italic) switch
            {
                (true, true) => "Times-BoldItalic",
                (true, false) => "Times-Bold",
                (false, true) => "Times-Italic",
                _ => "Times-Roman",
            },
            "Courier" => (Bold, Italic) switch
            {
                (true, true) => "Courier-BoldOblique",
                (true, false) => "Courier-Bold",
                (false, true) => "Courier-Oblique",
                _ => "Courier",
            },
            _ => (Bold, Italic) switch
            {
                (true, true) => "Helvetica-BoldOblique",
                (true, false) => "Helvetica-Bold",
                (false, true) => "Helvetica-Oblique",
                _ => "Helvetica",
            },
        };
    }
}

/// <summary>
/// Per-desk header/footer configuration, applied to the bound output on Bind/Lift.
/// Each band is Word-style: separate Left / Centre / Right text that may contain tokens
/// ({page}, {total}, {filename}, {filepath}, {date}, {time}, {datetime}).
/// </summary>
public sealed class DeskHeaderFooter
{
    public bool HeaderEnabled { get; set; }
    public string HeaderLeft { get; set; } = string.Empty;
    public string HeaderCenter { get; set; } = string.Empty;
    public string HeaderRight { get; set; } = string.Empty;

    public bool FooterEnabled { get; set; } = true;
    public string FooterLeft { get; set; } = string.Empty;
    public string FooterCenter { get; set; } = string.Empty;
    public string FooterRight { get; set; } = string.Empty;

    public double FontSize { get; set; } = 9;
    public string ColorHex { get; set; } = "#333333";

    public bool BackgroundEnabled { get; set; }
    public string BackgroundHex { get; set; } = "#EEEEEE";

    public double MarginX { get; set; } = 36;
    public double BandHeight { get; set; } = 28;

    /// <summary>"Quick" = per-slot dropdowns; "Custom" = free text. Editor state only.</summary>
    public bool QuickMode { get; set; } = true;

    /// <summary>Page-number style for Quick mode tokens: "" (1,2,3), roman, ROMAN, alpha, ALPHA.</summary>
    public string PageFmt { get; set; } = "";
    public string DateFmt { get; set; } = "yyyy-MM-dd";
    public string TimeFmt { get; set; } = "HH:mm";

    /// <summary>How the band sits relative to page content: "overlay", "reserve", "reserveIfIntruding".</summary>
    public string Fit { get; set; } = "overlay";

    /// <summary>When true, stamp every page; otherwise the inclusive 1-based range From..To.</summary>
    public bool AllPages { get; set; } = true;
    public int FromPage { get; set; } = 1;
    /// <summary>1-based last page of the range; 0 means "through the last page".</summary>
    public int ToPage { get; set; }

    public bool HeaderHasText =>
        HeaderEnabled && (!string.IsNullOrWhiteSpace(HeaderLeft)
            || !string.IsNullOrWhiteSpace(HeaderCenter) || !string.IsNullOrWhiteSpace(HeaderRight));

    public bool FooterHasText =>
        FooterEnabled && (!string.IsNullOrWhiteSpace(FooterLeft)
            || !string.IsNullOrWhiteSpace(FooterCenter) || !string.IsNullOrWhiteSpace(FooterRight));

    public bool HasAnyContent => HeaderHasText || FooterHasText;
}

/// <summary>
/// One desk: an independent, ordered arrangement of pages that binds to its own PDF.
/// Several desks share the same shelf of sources; the same source page may be copied
/// into more than one desk.
/// </summary>
public sealed class Desk
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>True once the user has edited the name, so auto-naming stops touching it.</summary>
    public bool NameLocked { get; set; }

    public List<BenchPage> Pages { get; } = new();

    public DeskWatermark? Watermark { get; set; }

    public DeskHeaderFooter? HeaderFooter { get; set; }

    public bool IsEmpty => Pages.Count == 0;
}
