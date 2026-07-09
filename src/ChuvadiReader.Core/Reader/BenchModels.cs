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

    /// <summary>Optional shelf page filter (0-based page indices, in display order). When set,
    /// only these pages show on the shelf and can be dragged; null = every page. Non-destructive —
    /// the full document stays open. Powers page-range (#19) and "show only matches" (#25).</summary>
    public IReadOnlyList<int>? PageFilter { get; set; }

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
    public Guid Id { get; init; } = Guid.NewGuid();

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

    /// <summary>Optional crop window, normalised top-left / y-down fractions (0..1) of the
    /// page as it is displayed (i.e. after <see cref="Rotation"/>). Null = no crop.
    /// Applied at compose time via the library's PageComposer.</summary>
    public CropRect? Crop { get; set; }

    /// <summary>How <see cref="Crop"/> is realised at compose time. Defaults to
    /// <see cref="CropFit.ToSize"/> (page shrinks to the rectangle).</summary>
    public CropFit CropMode { get; set; } = CropFit.ToSize;

    /// <summary>Optional content margins (PDF points) applied at compose time: the page's
    /// content scales to fit inside the page minus these margins; the page size is unchanged.
    /// Null = no margins.</summary>
    public MarginSet? Margins { get; set; }

    /// <summary>A full, identity-preserving deep copy — same <see cref="Id"/> and every field,
    /// including rotation, crop and margins. Used by the undo/redo history so a restore brings
    /// back the exact same page objects (selection, which is keyed by Id, stays valid).
    /// <see cref="CropRect"/> and <see cref="MarginSet"/> are immutable records, so sharing the
    /// reference is safe.</summary>
    public BenchPage Clone() => new()
    {
        Id = Id,
        SourcePath = SourcePath,
        SourceName = SourceName,
        SourceIndex = SourceIndex,
        OriginalIndex = OriginalIndex,
        Rotation = Rotation,
        IsBlank = IsBlank,
        BackgroundHex = BackgroundHex,
        IsImage = IsImage,
        ImagePath = ImagePath,
        Crop = Crop,
        CropMode = CropMode,
        Margins = Margins,
    };
}

/// <summary>A normalised crop window (top-left origin, y-down, fractions 0..1).</summary>
public sealed record CropRect(double X, double Y, double W, double H);

/// <summary>How a crop rectangle is realised when the desk is bound.</summary>
public enum CropFit
{
    /// <summary>Page size unchanged; everything outside the rectangle is painted white.</summary>
    Mask,

    /// <summary>Page shrinks to exactly the crop rectangle (the original behaviour).</summary>
    ToSize,

    /// <summary>New page keeps the source page's own size; the cropped region scales,
    /// centered and aspect-preserved, to fill it (white letterbox bars if aspect differs).</summary>
    FitPage,

    /// <summary>Like <see cref="FitPage"/>, but the cropped region fits inside the page
    /// minus a default margin on every side.</summary>
    FitMargin,
}

/// <summary>Per-side content margins in PDF points (1 in = 72 pt, 1 cm ≈ 28.3465 pt).</summary>
public sealed record MarginSet(double Top, double Right, double Bottom, double Left);

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

    /// <summary>1-based first page of the range (Reader scope; the Bench uses page selection instead).</summary>
    public int FromPage { get; set; } = 1;
    /// <summary>1-based last page of the range; 0 means "through the last page".</summary>
    public int ToPage { get; set; }

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

    /// <summary>Deep copy for desk duplication.</summary>
    public DeskWatermark Clone() => new()
    {
        Text = Text, FontFamily = FontFamily, Bold = Bold, Italic = Italic, FontSize = FontSize,
        ColorHex = ColorHex, Opacity = Opacity, RotationDegrees = RotationDegrees,
        AllPages = AllPages, FromPage = FromPage, ToPage = ToPage,
    };
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

    /// <summary>Deep copy for desk duplication.</summary>
    public DeskHeaderFooter Clone() => new()
    {
        HeaderEnabled = HeaderEnabled, HeaderLeft = HeaderLeft, HeaderCenter = HeaderCenter, HeaderRight = HeaderRight,
        FooterEnabled = FooterEnabled, FooterLeft = FooterLeft, FooterCenter = FooterCenter, FooterRight = FooterRight,
        FontSize = FontSize, ColorHex = ColorHex,
        BackgroundEnabled = BackgroundEnabled, BackgroundHex = BackgroundHex,
        MarginX = MarginX, BandHeight = BandHeight, QuickMode = QuickMode,
        PageFmt = PageFmt, DateFmt = DateFmt, TimeFmt = TimeFmt, Fit = Fit,
        AllPages = AllPages, FromPage = FromPage, ToPage = ToPage,
    };
}

/// <summary>Number presentation: plain page number, page-of-total, or Bates (prefix + padded).</summary>
public enum NumberStyle { PageOnly, PageOfTotal, Bates }

/// <summary>Where the number sits on each page.</summary>
public enum NumberPosition { BottomLeft, BottomCenter, BottomRight, TopLeft, TopRight }

/// <summary>How the first (cover) page is treated. <see cref="Number"/> stamps it like any
/// other; <see cref="SkipKeepCount"/> leaves it unstamped but still counted (so page 2 reads
/// "2"); <see cref="SkipRenumber"/> excludes it from the count entirely (so page 2 reads "1").</summary>
public enum FirstPageMode { Number, SkipKeepCount, SkipRenumber }

/// <summary>Per-desk page/Bates numbering (#40), applied at Bind as a per-page corner stamp.</summary>
public sealed class DeskNumbering
{
    public NumberStyle Style { get; set; } = NumberStyle.PageOnly;
    public string Prefix { get; set; } = "";
    public int Start { get; set; } = 1;
    public int PadWidth { get; set; }
    public NumberPosition Position { get; set; } = NumberPosition.BottomRight;
    public double FontSize { get; set; } = 10;
    public string ColorHex { get; set; } = "#333333";
    public bool AllPages { get; set; } = true;
    public int FromPage { get; set; } = 1;
    public int ToPage { get; set; }
    public FirstPageMode FirstPage { get; set; } = FirstPageMode.Number;

    /// <summary>Formats the printed text for a given printed value and total.</summary>
    public string Format(int value, int total)
    {
        var num = PadWidth > 0 ? value.ToString().PadLeft(PadWidth, '0') : value.ToString();
        return Style switch
        {
            NumberStyle.Bates => Prefix + num,
            NumberStyle.PageOfTotal => $"Page {num} of {total}",
            _ => $"Page {num}",
        };
    }

    /// <summary>The complete per-page plan: which 0-based pages get a stamp and the exact text.
    /// Honours the first-page mode, the All/Range scope, the Start offset and the style. This is
    /// the single source of truth — the stamp service just executes the list.</summary>
    public IReadOnlyList<(int PageIndex, string Text)> Stamps(int total)
    {
        var list = new List<(int, string)>();
        if (total <= 0)
        {
            return list;
        }

        var skipFirst = FirstPage is FirstPageMode.SkipKeepCount or FirstPageMode.SkipRenumber;
        var renumber = FirstPage == FirstPageMode.SkipRenumber;
        var denom = renumber ? Math.Max(1, total - 1) : total; // the "of Y"

        var from = AllPages ? 1 : Math.Max(1, FromPage);
        var to = AllPages ? total : (ToPage <= 0 ? total : Math.Min(total, ToPage));

        for (var i = 0; i < total; i++)
        {
            if (skipFirst && i == 0)
            {
                continue; // cover left unstamped
            }

            var pageNo = i + 1;
            if (pageNo < from || pageNo > to)
            {
                continue; // outside the chosen range
            }

            // Printed value: Number / SkipKeepCount → Start + i; SkipRenumber → Start + (i-1).
            var value = renumber ? Start + (i - 1) : Start + i;
            list.Add((i, Format(value, denom)));
        }
        return list;
    }

    public DeskNumbering Clone() => new()
    {
        Style = Style, Prefix = Prefix, Start = Start, PadWidth = PadWidth,
        Position = Position, FontSize = FontSize, ColorHex = ColorHex,
        AllPages = AllPages, FromPage = FromPage, ToPage = ToPage, FirstPage = FirstPage,
    };
}

/// <summary>
/// One desk: an independent, ordered arrangement of pages that binds to its own PDF.
/// Several desks share the same shelf of sources; the same source page may be copied
/// into more than one desk.
/// </summary>
public sealed class Desk
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>True once the user has edited the name, so auto-naming stops touching it.</summary>
    public bool NameLocked { get; set; }

    public List<BenchPage> Pages { get; } = new();

    public DeskWatermark? Watermark { get; set; }

    public DeskHeaderFooter? HeaderFooter { get; set; }

    /// <summary>Optional per-desk page / Bates numbering (#40), applied at Bind.</summary>
    public DeskNumbering? Numbering { get; set; }

    /// <summary>Write a per-source-section outline into the bound PDF (#47). Default on.</summary>
    public bool AddBookmarks { get; set; } = true;

    /// <summary>Optional page-size normalisation applied at Bind: "A4" or "Letter".
    /// Null = leave each page at its own size. Every page is scaled to fit the target
    /// (aspect preserved) and top-anchored, via the library's PageComposer.</summary>
    public string? NormalizeSize { get; set; }

    /// <summary>Optional desk colour tag (hex, e.g. "#C2693B"), shown on the desk card and its
    /// page accents. Null = the default neutral accent.</summary>
    public string? ColorHex { get; set; }

    public bool IsEmpty => Pages.Count == 0;

    /// <summary>A full, identity-preserving deep copy — same <see cref="Id"/>, same page Ids,
    /// and deep copies of every per-desk setting. Used by the undo/redo history.</summary>
    public Desk Clone()
    {
        var copy = new Desk
        {
            Id = Id,
            Name = Name,
            NameLocked = NameLocked,
            Watermark = Watermark?.Clone(),
            HeaderFooter = HeaderFooter?.Clone(),
            Numbering = Numbering?.Clone(),
            AddBookmarks = AddBookmarks,
            NormalizeSize = NormalizeSize,
            ColorHex = ColorHex,
        };
        foreach (var p in Pages)
        {
            copy.Pages.Add(p.Clone());
        }
        return copy;
    }
}
