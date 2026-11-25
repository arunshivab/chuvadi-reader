using System.Globalization;
using Chuvadi.Sheets.Model;

namespace ChuvadiReader.Core.Documents;

// ─────────────────────────────────────────────────────────────────────────────
// Presentation-free render model for a spreadsheet. One WorkbookView holds the
// sheets; each SheetView is a sparse list of populated cells plus geometry.
// D-161: each cell shows its display text (the number format, in the system culture) and keeps
// its stored value underneath.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WorkbookView(IReadOnlyList<SheetView> Sheets)
{
    /// <summary>What reading the workbook allocated (WT-19). The viewer hands it to <see cref="ViewMemory.Released"/>
    /// when the workbook is closed, so a heavy one gives its memory back as a heavy PDF does.</summary>
    public long LoadedBytes { get; init; }
}

public sealed record SheetView(
    string Name,
    int FirstRow, int LastRow, int FirstCol, int LastCol,
    IReadOnlyList<SheetCell> Cells,
    IReadOnlyDictionary<int, double> ColWidths,
    int FreezeRows, int FreezeCols)
{
    public bool IsEmpty => Cells.Count == 0;
    public int RowCount => LastRow - FirstRow + 1;
    public int ColCount => LastCol - FirstCol + 1;

    /// <summary>D-172 / D-214: the sheet is hidden (or very hidden) in the file. Left out of the sheet tabs, as in
    /// Excel, unless "Show hidden rows, columns and sheets" is on; then it is shown dimmed and marked.</summary>
    public bool Hidden { get; init; }

    /// <summary>D-214: the columns the file hides, with the width each has when shown.</summary>
    public IReadOnlyDictionary<int, double> HiddenColPx { get; init; } = new Dictionary<int, double>();

    /// <summary>D-214: the rows the file hides, with the height each has when shown.</summary>
    public IReadOnlyDictionary<int, double> HiddenRowPx { get; init; } = new Dictionary<int, double>();

    /// <summary>D-214: this is the sheet with its hidden rows and columns shown (greyed, view only).</summary>
    public bool HiddenShown { get; init; }

    public bool IsHiddenCell(int row, int col) => HiddenShown && (HiddenRowPx.ContainsKey(row) || HiddenColPx.ContainsKey(col));

    /// <summary>D-214: the same sheet with its hidden rows and columns given room, for the switch.</summary>
    public SheetView WithHiddenShown()
    {
        if (HiddenShown || (HiddenColPx.Count == 0 && HiddenRowPx.Count == 0)) return this with { HiddenShown = true };
        var cols = new Dictionary<int, double>(ColPx);
        foreach (var (c, w) in HiddenColPx) { if (w == DefaultColPx) cols.Remove(c); else cols[c] = w; }
        var rows = new Dictionary<int, double>(RowPx);
        foreach (var (r, h) in HiddenRowPx) { if (h == DefaultRowPx) rows.Remove(r); else rows[r] = h; }
        return this with { ColPx = cols, RowPx = rows, HiddenShown = true };
    }

    /// <summary>E-08: the width of a column the file gives no width for, in pixels at 100 % (Excel's rule for the
    /// workbook's default font, 7 px a digit).</summary>
    public double DefaultColPx { get; init; } = 64;

    /// <summary>E-08: the height of a row the file gives no height for, in pixels at 100 %.</summary>
    public double DefaultRowPx { get; init; } = 20;

    /// <summary>E-06, E-08: columns whose width differs from the default, in pixels at 100 %; 0 for a hidden column.</summary>
    public IReadOnlyDictionary<int, double> ColPx { get; init; } = new Dictionary<int, double>();

    /// <summary>E-06, E-08: rows whose height differs from the default, in pixels at 100 %; 0 for a hidden row.</summary>
    public IReadOnlyDictionary<int, double> RowPx { get; init; } = new Dictionary<int, double>();

    /// <summary>E-07: merged ranges.</summary>
    public IReadOnlyList<SheetMerge> Merges { get; init; } = [];

    /// <summary>E-16: the sheet's own settings.</summary>
    public bool ShowGridLines { get; init; } = true;

    public bool RightToLeft { get; init; }

    /// <summary>The zoom the file stores for the sheet, in percent (Excel opens the sheet at it).</summary>
    public int Zoom { get; init; } = 100;

    /// <summary>E-15: pictures, shapes and charts on the sheet. They aren't drawn yet; the viewer says how many.</summary>
    public int Drawings { get; init; }

    /// <summary>D-200: the sheet has conditional formats whose colours are not drawn until the next update
    /// (<see cref="LibraryHolds.ConditionalFormatsHeld"/>). The cells and their text are as the file holds them.</summary>
    public bool ConditionalColoursHeld { get; init; }

    public double ColWidthPx(int col) => ColPx.TryGetValue(col, out var w) ? w : DefaultColPx;

    public double RowHeightPx(int row) => RowPx.TryGetValue(row, out var h) ? h : DefaultRowPx;
}

/// <summary>A merged range (1-based, inclusive). Its top-left cell holds the value and the style.</summary>
public readonly record struct SheetMerge(int Row, int Col, int LastRow, int LastCol)
{
    public bool Contains(int row, int col) => row >= Row && row <= LastRow && col >= Col && col <= LastCol;
}

/// <summary>One run of rich text in a cell (E-12): its text and what differs from the cell's font.</summary>
public sealed record SheetRun(string Text, bool? Bold, bool? Italic, bool? Underline, bool? Strike,
    string? Color, string? FontName, double? FontSizePt, string? VPos);

/// <summary>A conditional format's data bar (E-13): how long, which colour, and where it starts.</summary>
public sealed record SheetBar(double Percent, string Color, bool Gradient, string? Border, bool FromRight, bool ShowValue);

/// <summary>A conditional format's icon (E-13): Excel's set name, which icon (0 = lowest) of how many.</summary>
public sealed record SheetIcon(string Set, int Index, int Count, bool ShowValue);

/// <summary>One cell. D-161: <see cref="Text"/> is what Excel shows for it — the stored value through the cell's number
/// format (and a conditional format's), in the system culture, by Chuvadi.Sheets; <see cref="Value"/> is the value the file
/// stores, unchanged (copy, export, search and the value shown on hover or selection use it).</summary>
public sealed record SheetCell(
    int Row, int Col, string Text, string Align,
    bool Bold, bool Italic, bool Underline,
    string? Color, string? Fill, bool IsNumber,
    string? FontName = null, double? FontSizePt = null, bool Wrap = false,
    string VAlign = "Bottom")
{
    /// <summary>The stored value as text: numbers and dates as the number the file stores (dates and times are serial
    /// numbers in Excel's date system), booleans TRUE/FALSE, text and errors as written.</summary>
    public string Value { get; init; } = Text;

    /// <summary>Where a "*x" format fills the cell with x (the index in <see cref="Text"/>), or null.</summary>
    public int? FillIndex { get; init; }

    /// <summary>The character a "*x" format repeats to fill the cell, or null.</summary>
    public char? FillCharacter { get; init; }

    /// <summary>E-11: each edge as a CSS border ("1px solid #000000"), or null for none.</summary>
    public string? BorderTop { get; init; }

    public string? BorderRight { get; init; }

    public string? BorderBottom { get; init; }

    public string? BorderLeft { get; init; }

    /// <summary>E-11: a pattern or gradient fill as a CSS background (over <see cref="Fill"/>), or null.</summary>
    public string? FillCss { get; init; }

    public bool Strike { get; init; }

    public bool DoubleUnderline { get; init; }

    /// <summary>"super", "sub" or null.</summary>
    public string? VPos { get; init; }

    public int Indent { get; init; }

    /// <summary>Text rotation in degrees, counter-clockwise positive (−90…90); 255 = letters stacked.</summary>
    public int Rotation { get; init; }

    public bool Shrink { get; init; }

    /// <summary>E-12: the cell's rich text, or null for plain text.</summary>
    public IReadOnlyList<SheetRun>? Runs { get; init; }

    public SheetBar? Bar { get; init; }

    public SheetIcon? Icon { get; init; }

    /// <summary>E-15: the cell's comment ("Author: text"), or null.</summary>
    public string? Note { get; init; }

    /// <summary>E-15: where the cell's hyperlink points (a web address, a file, or a place in the workbook), or null.</summary>
    public string? Link { get; init; }

    /// <summary>True for a cell with nothing to show but a style (a border or a fill on an empty cell).</summary>
    public bool StyleOnly { get; init; }
}

/// <summary>Loads an .xlsx into a <see cref="WorkbookView"/> via <c>Chuvadi.Sheets</c>.</summary>
public sealed class XlsxViewService
{
    /// <summary>B-08 (D-136): the library's limits for files from outside (256 MB per part, 1 GB in total,
    /// 500:1 compression, Excel's row maximum), checked against declared and actually inflated sizes; plus
    /// the read's cancellation (B-08b), which the library checks on every row.</summary>
    public static XlsxReaderOptions ReadOptions(CancellationToken cancellationToken)
    {
        var options = XlsxReaderOptions.ForUntrustedInput();
        options.CancellationToken = cancellationToken;
        options.IncludeStyledEmptyCells = true;   // E-11: an empty cell's border or fill is part of what the file shows
        return options;
    }

    /// <summary>D-161: the culture whose separators, grouping, date order and names Excel takes from the system (Excel
    /// shows a file by the computer's settings). Null: the system's (<see cref="CultureInfo.CurrentCulture"/>).</summary>
    public CultureInfo? Culture { get; init; }

    /// <param name="cancellationToken">Stops the read (B-08b): "Stop opening", a closed tab, or the viewer's time limit.</param>
    public WorkbookView Load(string path, string? password = null, CancellationToken cancellationToken = default)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            return LoadCore(path, password, cancellationToken) with { LoadedBytes = GC.GetAllocatedBytesForCurrentThread() - before };
        }
        catch
        {
            // WT-19: a stopped or refused open of a heavy workbook gives back what it had read so far.
            ViewMemory.Released(GC.GetAllocatedBytesForCurrentThread() - before);
            throw;
        }
    }

    private const string TooLargeText =
        "This Excel workbook is too large to open: it goes beyond what Chuvadi allows. It may be damaged.";

    /// <summary>WT-18: true when the sizes the file itself declares (its zip table of contents, read with .NET's
    /// own zip reader) go beyond the library's limits: a part's size, the total, the number of parts, or the
    /// compression ratio. Asked only after the library has refused the file, to word that refusal; it never
    /// refuses a file by itself. A file that is not a zip (or whose table cannot be read) is not "too large".</summary>
    internal static bool BeyondPackageLimits(string path, XlsxReaderOptions limits)
    {
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(SharedRead.Open(path), System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
            if (zip.Entries.Count > limits.MaxPackageEntries) return true;
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.Length > limits.MaxPartBytes) return true;
                if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > limits.MaxCompressionRatio) return true;
                total += entry.Length;
                if (total > limits.MaxTotalUncompressedBytes) return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private WorkbookView LoadCore(string path, string? password, CancellationToken cancellationToken)
    {
        Workbook wb;
        try
        {
            using var file = SharedRead.Open(path);   // open in Excel too: shared, never "used by another process"
            wb = Workbook.Load(file, string.IsNullOrEmpty(password) ? null : password, ReadOptions(cancellationToken));
        }
        catch (XlsxLimitExceededException ex)
        {
            throw new DocumentViewException(ViewErrorReason.TooLarge, TooLargeText, ex);
        }
        catch (XlsxPasswordRequiredException ex)
        {
            throw new DocumentViewException(ViewErrorReason.PasswordProtected,
                "This workbook is password-protected.", ex);
        }
        catch (InvalidDataException ex) when (BeyondPackageLimits(path, ReadOptions(default)))
        {
            // WT-18: the library refuses a part that is too big with a plain "invalid data" error (library item
            // SH-L2), which reads as "damaged". The file's own table of contents says whether it is a size refusal.
            throw new DocumentViewException(ViewErrorReason.TooLarge, TooLargeText, ex);
        }
        catch (XlsxFormatException ex)
        {
            throw new DocumentViewException(ViewErrorReason.BadFormat,
                "This file couldn't be read as an Excel workbook.", ex);
        }

        var sheets = new List<SheetView>(wb.Sheets.Count);
        var format = new NumberFormatOptions { Culture = Culture ?? CultureInfo.CurrentCulture, Date1904 = wb.Date1904 };

        foreach (var s in wb.Sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sheets.Add(MapSheet(s, wb, format));
        }

        return new WorkbookView(sheets);
    }

    /// <summary>Excel's width-to-pixel rule for a column width as the file stores it (characters of the default font,
    /// padding included; 7 px a digit at 100 %): truncate((256 × width + truncate(128 / 7)) / 256 × 7). The default
    /// column, stored as 9.140625, is 64 px.</summary>
    internal static double WidthPx(double characters) =>
        characters <= 0 ? 0 : Math.Truncate(((256 * characters) + Math.Truncate(128.0 / 7)) / 256 * 7);

    /// <summary>A height in points as pixels at 100 % (96 px an inch), to the whole pixel as Excel draws rows.</summary>
    internal static double HeightPx(double points) => points <= 0 ? 0 : Math.Round(points * 96 / 72);

    private static SheetView MapSheet(Sheet s, Workbook wb, NumberFormatOptions format)
    {
        var raw = s.PopulatedCells.ToList();

        // Comments and hyperlinks by cell (E-15).
        Dictionary<(int, int), string>? notes = null;
        foreach (var c in s.Comments)
        {
            string text = string.IsNullOrEmpty(c.Author) || c.Text.StartsWith(c.Author + ":", StringComparison.Ordinal) ? c.Text : c.Author + ": " + c.Text;
            (notes ??= [])[(c.Row, c.Column)] = text;
        }

        var links = s.Hyperlinks.Where(h => h.FirstRow > 0).ToList();

        int minR = int.MaxValue, maxR = 0, minC = int.MaxValue, maxC = 0;
        var cells = new List<SheetCell>(raw.Count);
        var conditional = s.ConditionalFormats.Count == 0 || LibraryHolds.ConditionalFormatsHeld ? null : s.EvaluateConditionalFormats(new ConditionalFormatOptions { Date1904 = wb.Date1904 });
        foreach (var c in raw)
        {
            var cell = MapCell(c, wb, format, conditional?.Evaluate(c.Row, c.Column));
            if (notes is not null && notes.TryGetValue((c.Row, c.Column), out var note))
            {
                cell = cell with { Note = note };
            }

            if (links.Count > 0 && links.FirstOrDefault(h => h.Contains(c.Row, c.Column)) is { } link)
            {
                cell = cell with { Link = LinkText(link) };
            }

            cells.Add(cell);
            if (cell.StyleOnly)
            {
                continue;   // the sheet's extent (copy all, the first and last cell) is where it holds something
            }

            if (c.Row < minR) minR = c.Row;
            if (c.Row > maxR) maxR = c.Row;
            if (c.Column < minC) minC = c.Column;
            if (c.Column > maxC) maxC = c.Column;
        }

        if (maxR == 0)
        {
            minR = maxR = minC = maxC = 1;
        }

        // E-08: widths and heights as the file gives them. E-06: a hidden row or column is 0 px.
        var fmt = s.Format;
        // With no default width in the file, Excel's is the base count of digits (8) plus 5 px of padding, rounded
        // up to a multiple of 8 px: 64 px for the default font.
        int baseDigits = fmt.BaseColumnWidth > 0 ? fmt.BaseColumnWidth : 8;
        double defaultColPx = fmt.DefaultColumnWidth is > 0 and var given
            ? WidthPx(given)
            : Math.Ceiling(((baseDigits * 7) + 5) / 8.0) * 8;
        double defaultRowPx = fmt.ZeroHeight ? 0 : HeightPx(fmt.DefaultRowHeight > 0 ? fmt.DefaultRowHeight : 15);
        var colPx = new Dictionary<int, double>();
        var widths = new Dictionary<int, double>();
        var hiddenColPx = new Dictionary<int, double>();
        var hiddenRowPx = new Dictionary<int, double>();
        foreach (var range in s.ColumnRanges)
        {
            double px = range.Hidden ? 0 : range.Width > 0 ? WidthPx(range.Width) : defaultColPx;
            double shownPx = range.Width > 0 ? WidthPx(range.Width) : defaultColPx;
            if (shownPx <= 0) shownPx = defaultColPx;
            for (int col = Math.Max(1, range.Min); col <= Math.Min(range.Max, MaxColumns); col++)
            {
                if (range.Hidden) hiddenColPx[col] = shownPx;
                if (px != defaultColPx)
                {
                    colPx[col] = px;
                }

                if (range.Width > 0)
                {
                    widths[col] = range.Width;
                }
            }
        }

        var rowPx = new Dictionary<int, double>();
        foreach (var row in s.DefinedRows)
        {
            double px = row.Hidden ? 0 : row.Height > 0 ? HeightPx(row.Height) : defaultRowPx;
            if (row.Hidden) hiddenRowPx[row.Number] = row.Height > 0 ? HeightPx(row.Height) : (defaultRowPx > 0 ? defaultRowPx : HeightPx(15));
            if (px != defaultRowPx)
            {
                rowPx[row.Number] = px;
            }
        }

        var merges = new List<SheetMerge>(s.MergeRanges.Count);
        foreach (var range in s.MergeRanges)
        {
            if (ParseRange(range) is { } m && (m.LastRow > m.Row || m.LastCol > m.Col))
            {
                merges.Add(m);
            }
        }

        return new SheetView(s.Name, minR, maxR, minC, maxC, cells, widths, s.FreezeRowCount, s.FreezeColumnCount)
        {
            Hidden = s.Visibility != SheetVisibility.Visible,
            DefaultColPx = defaultColPx,
            DefaultRowPx = defaultRowPx,
            ColPx = colPx,
            RowPx = rowPx,
            HiddenColPx = hiddenColPx,
            HiddenRowPx = hiddenRowPx,
            Merges = merges,
            ShowGridLines = s.ShowGridLines,
            RightToLeft = s.RightToLeft,
            Zoom = s.ZoomScale is >= 10 and <= 400 ? s.ZoomScale : 100,
            Drawings = s.Drawings.Count,
            ConditionalColoursHeld = LibraryHolds.ConditionalFormatsHeld && s.ConditionalFormats.Count > 0,
        };
    }

    private const int MaxColumns = 16384;

    /// <summary>Where a hyperlink points, as the file stores it: the target, with the place in it after "#".</summary>
    private static string LinkText(Hyperlink h) =>
        string.IsNullOrEmpty(h.Target) ? h.Location ?? "" : string.IsNullOrEmpty(h.Location) ? h.Target : h.Target + "#" + h.Location;

    /// <summary>"B3" or "B3:D7" (with or without $) as a range; null when it isn't one.</summary>
    internal static SheetMerge? ParseRange(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var parts = reference.Split(':');
        if (parts.Length is < 1 or > 2 || ParseCell(parts[0]) is not { } a)
        {
            return null;
        }

        var b = parts.Length == 2 ? ParseCell(parts[1]) : a;
        return b is { } z
            ? new SheetMerge(Math.Min(a.Row, z.Row), Math.Min(a.Col, z.Col), Math.Max(a.Row, z.Row), Math.Max(a.Col, z.Col))
            : null;
    }

    private static (int Row, int Col)? ParseCell(string a1)
    {
        int col = 0, row = 0, i = 0;
        for (; i < a1.Length; i++)
        {
            char ch = a1[i];
            if (ch == '$') continue;
            if (ch is >= 'A' and <= 'Z') col = (col * 26) + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') col = (col * 26) + (ch - 'a' + 1);
            else break;
        }

        for (; i < a1.Length; i++)
        {
            char ch = a1[i];
            if (ch == '$') continue;
            if (ch is < '0' or > '9') return null;
            row = (row * 10) + (ch - '0');
        }

        return col is > 0 and <= MaxColumns && row is > 0 and <= 1048576 ? (row, col) : null;
    }

    internal static SheetCell MapCell(Cell c, Workbook wb, NumberFormatOptions format, ConditionalFormatResult? conditional)
    {
        CellStyle? own = c.Style;
        // A conditional format changes the style Excel shows (fill, font, number format) while its rule holds.
        CellStyle? st = conditional is { IsEmpty: false } && own is { } o ? conditional.ApplyTo(o) : own;
        var value = c.Value;
        bool isNum = value is double or float or int or long or decimal or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan;

        // D-161: the text is Chuvadi.Sheets' display text. A conditional number format goes through the same formatter.
        string? conditionalCode = conditional is { IsEmpty: false } ? conditional.NumberFormat : null;
        string text = conditionalCode is null ? c.GetDisplayText(format) : NumberFormatter.Format(value, conditionalCode, format);
        string? code = conditionalCode ?? own?.NumberFormat
            ?? (own?.NumberFormatId is int id ? wb.StyleTable.GetNumberFormatCode(id) : null);
        FormattedValue? detail = code is null || value is null ? null : NumberFormatter.FormatDetailed(value, code, format);

        string align = st is { } s && s.HAlign != HorizontalAlign.General
            ? s.HAlign.ToString()
            : (isNum ? "Right" : value is bool ? "Center" : "Left");

        // The colour a format code names ([Red], [Color10]) is the one Excel shows; a conditional format's own font colour wins.
        string? fontColour = conditional is { IsEmpty: false } && own is { } before && st?.FontColor != before.FontColor
            ? Hex(st?.FontColor)
            : (detail?.Color is { } named ? "#" + named : Hex(st?.FontColor));

        var bar = conditional?.DataBar;
        var icon = conditional?.Icon;
        var rich = c.RichText is { Runs.Count: > 0 } rt && text == rt.Text ? rt : null;   // the runs are the text as stored

        return new SheetCell(
            c.Row, c.Column, text, align,
            st?.Bold ?? false, st?.Italic ?? false, st?.Underline ?? false,
            fontColour, Hex(st?.FillColor), isNum,
            st is { } fs && !string.IsNullOrWhiteSpace(fs.FontName) ? fs.FontName : null,
            st is { } ss && ss.FontSize > 0 ? ss.FontSize : null,
            st?.WrapText ?? false,
            st is { } vs && vs.VAlign != VerticalAlign.Bottom ? vs.VAlign.ToString() : "Bottom")
        {
            Value = StoredValue(value, wb.Date1904),
            FillIndex = detail is { FillCharacter: not null } d ? d.FillIndex : null,
            FillCharacter = detail?.FillCharacter,
            BorderTop = BorderCss(st?.BorderTop),
            BorderRight = BorderCss(st?.BorderRight),
            BorderBottom = BorderCss(st?.BorderBottom),
            BorderLeft = BorderCss(st?.BorderLeft),
            FillCss = st is { } fill ? FillCssOf(fill, wb) : null,
            Strike = st?.Strikethrough ?? false,
            DoubleUnderline = st?.UnderlineKind is UnderlineStyle.Double or UnderlineStyle.DoubleAccounting,
            VPos = VPosOf(st?.FontVerticalPosition),
            Indent = Math.Max(0, st?.Indent ?? 0),
            Rotation = st?.TextRotation is int turn ? (turn == 255 ? 255 : turn > 90 ? 90 - turn : turn) : 0,
            Shrink = st?.ShrinkToFit ?? false,
            Runs = rich?.Runs.Select(r => new SheetRun(r.Text, r.Font?.Bold, r.Font?.Italic,
                r.Font?.Underline is { } u ? u != UnderlineStyle.None : null, r.Font?.Strikethrough,
                Hex(r.ResolvedColor), string.IsNullOrWhiteSpace(r.Font?.Name) ? null : r.Font!.Name, r.Font?.Size,
                VPosOf(r.Font?.VerticalAlign))).ToList(),
            Bar = bar is null ? null : new SheetBar(Math.Clamp(bar.LengthPercent, 0, 100), Hex(bar.FillColor) ?? "#638EC6",
                bar.Bar.Gradient, Hex(bar.IsNegative ? bar.Bar.NegativeBorderColor ?? bar.Bar.BorderColor : bar.Bar.BorderColor),
                bar.Bar.Direction == DataBarDirection.RightToLeft, bar.Bar.ShowValue),
            Icon = icon is null ? null : new SheetIcon(icon.IconSet, icon.Index, icon.Count, icon.ShowValue),
            StyleOnly = value is null,
        };
    }

    /// <summary>The value the file stores, as text, never formatted: a number in its shortest exact form ("R"), a date or
    /// time as the serial number the file holds (Excel stores them as numbers), TRUE/FALSE, text and errors as written.</summary>
    internal static string StoredValue(object? v, bool date1904) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "TRUE" : "FALSE",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => ((double)f).ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        DateTime dt => NumberFormatter.ToSerial(dt, date1904).ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    private static string? VPosOf(FontVerticalAlign? v) => v switch
    {
        FontVerticalAlign.Superscript => "super",
        FontVerticalAlign.Subscript => "sub",
        _ => null,
    };

    /// <summary>E-11: an edge as CSS. Excel's 13 line styles on the three widths a screen can show.</summary>
    internal static string? BorderCss(Border? border)
    {
        if (border is not { IsNone: false } b)
        {
            return null;
        }

        string line = b.Style switch
        {
            BorderStyle.Hair => "1px dotted",
            BorderStyle.Dotted => "1px dotted",
            BorderStyle.Dashed or BorderStyle.DashDot or BorderStyle.DashDotDot => "1px dashed",
            BorderStyle.Medium => "2px solid",
            BorderStyle.MediumDashed or BorderStyle.MediumDashDot or BorderStyle.MediumDashDotDot or BorderStyle.SlantDashDot => "2px dashed",
            BorderStyle.Thick => "3px solid",
            BorderStyle.Double => "3px double",
            _ => "1px solid",
        };
        return line + " " + (Hex(b.Color) ?? "#000000");
    }

    /// <summary>E-11: a gradient or a pattern fill as a CSS background; null for none or a solid fill (that is
    /// <see cref="SheetCell.Fill"/>).</summary>
    internal static string? FillCssOf(CellStyle st, Workbook wb)
    {
        if (st.FillGradient is { Stops.Count: > 0 } g)
        {
            var stops = string.Join(", ", g.Stops.Select(stop =>
                $"{Hex(wb.StyleTable.ResolveColor(stop.Color)) ?? "#FFFFFF"} {(stop.Position * 100).ToString("0.#", CultureInfo.InvariantCulture)}%"));
            return g.Type == GradientType.Path
                ? $"radial-gradient(circle, {stops})"
                : $"linear-gradient({(90 + g.Degree).ToString("0.#", CultureInfo.InvariantCulture)}deg, {stops})";
        }

        if (st.FillPattern is not { } pattern || pattern is FillPattern.None or FillPattern.Solid)
        {
            return null;
        }

        string fg = Hex(st.FillForegroundColor) ?? "#000000";
        string bg = Hex(st.FillBackgroundColor) ?? "#FFFFFF";
        string Lines(string angle, int every) =>
            $"repeating-linear-gradient({angle}, {fg} 0 1px, transparent 1px {every}px), {bg}";
        return pattern switch
        {
            FillPattern.DarkGray => Mix(fg, bg, 0.75),
            FillPattern.MediumGray => Mix(fg, bg, 0.5),
            FillPattern.LightGray => Mix(fg, bg, 0.25),
            FillPattern.Gray125 => Mix(fg, bg, 0.125),
            FillPattern.Gray0625 => Mix(fg, bg, 0.0625),
            FillPattern.DarkHorizontal => Lines("0deg", 2),
            FillPattern.LightHorizontal => Lines("0deg", 4),
            FillPattern.DarkVertical => Lines("90deg", 2),
            FillPattern.LightVertical => Lines("90deg", 4),
            FillPattern.DarkDown => Lines("45deg", 3),
            FillPattern.LightDown => Lines("45deg", 5),
            FillPattern.DarkUp => Lines("-45deg", 3),
            FillPattern.LightUp => Lines("-45deg", 5),
            FillPattern.DarkGrid or FillPattern.DarkTrellis =>
                $"repeating-linear-gradient(0deg, {fg} 0 1px, transparent 1px 3px), repeating-linear-gradient(90deg, {fg} 0 1px, transparent 1px 3px), {bg}",
            _ => $"repeating-linear-gradient(0deg, {fg} 0 1px, transparent 1px 5px), repeating-linear-gradient(90deg, {fg} 0 1px, transparent 1px 5px), {bg}",
        };
    }

    /// <summary>A grey pattern is its two colours mixed: <paramref name="share"/> of the foreground.</summary>
    private static string Mix(string fg, string bg, double share)
    {
        int Part(int at) => (int)Math.Round(
            (Convert.ToInt32(fg.Substring(at, 2), 16) * share) + (Convert.ToInt32(bg.Substring(at, 2), 16) * (1 - share)));
        return $"#{Part(1):X2}{Part(3):X2}{Part(5):X2}";
    }

    private static string? Hex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.TrimStart('#');
        // xlsx ARGB → drop leading alpha if present.
        if (hex.Length == 8) hex = hex[2..];
        return hex.Length == 6 ? "#" + hex : null;
    }
}
