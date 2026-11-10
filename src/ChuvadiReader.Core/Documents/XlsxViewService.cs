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
}

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

    private WorkbookView LoadCore(string path, string? password, CancellationToken cancellationToken)
    {
        Workbook wb;
        try
        {
            wb = Workbook.Load(path, string.IsNullOrEmpty(password) ? null : password, ReadOptions(cancellationToken));
        }
        catch (XlsxLimitExceededException ex)
        {
            throw new DocumentViewException(ViewErrorReason.TooLarge,
                "This Excel workbook is too large to open: it goes beyond what Chuvadi allows. It may be damaged.", ex);
        }
        catch (XlsxPasswordRequiredException ex)
        {
            throw new DocumentViewException(ViewErrorReason.PasswordProtected,
                "This workbook is password-protected.", ex);
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
            var raw = s.PopulatedCells.ToList();
            if (raw.Count == 0)
            {
                sheets.Add(new SheetView(s.Name, 1, 1, 1, 1, Array.Empty<SheetCell>(),
                    new Dictionary<int, double>(), s.FreezeRowCount, s.FreezeColumnCount));
                continue;
            }

            int minR = int.MaxValue, maxR = 0, minC = int.MaxValue, maxC = 0;
            var cells = new List<SheetCell>(raw.Count);
            var conditional = s.ConditionalFormats.Count == 0 ? null : s.EvaluateConditionalFormats(new ConditionalFormatOptions { Date1904 = wb.Date1904 });
            foreach (var c in raw)
            {
                if (c.Row < minR) minR = c.Row;
                if (c.Row > maxR) maxR = c.Row;
                if (c.Column < minC) minC = c.Column;
                if (c.Column > maxC) maxC = c.Column;
                cells.Add(MapCell(c, wb, format, conditional?.Evaluate(c.Row, c.Column)));
            }

            var widths = new Dictionary<int, double>();
            for (int col = minC; col <= maxC; col++)
            {
                try
                {
                    var w = s.Columns[col].Width;
                    if (w > 0) widths[col] = w;
                }
                catch { /* column not explicitly sized */ }
            }

            sheets.Add(new SheetView(s.Name, minR, maxR, minC, maxC, cells, widths,
                s.FreezeRowCount, s.FreezeColumnCount));
        }

        return new WorkbookView(sheets);
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

    private static string? Hex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.TrimStart('#');
        // xlsx ARGB → drop leading alpha if present.
        if (hex.Length == 8) hex = hex[2..];
        return hex.Length == 6 ? "#" + hex : null;
    }
}
