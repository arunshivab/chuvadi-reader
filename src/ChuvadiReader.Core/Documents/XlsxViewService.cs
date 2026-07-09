using System.Globalization;
using Chuvadi.Sheets.Excel;

namespace ChuvadiReader.Core.Documents;

// ─────────────────────────────────────────────────────────────────────────────
// Presentation-free render model for a spreadsheet. One WorkbookView holds the
// sheets; each SheetView is a sparse list of populated cells plus geometry.
// (v1: merged ranges are not exposed by the reader, so cells render unmerged;
//  per-cell styles render only when the reader surfaces them.)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WorkbookView(IReadOnlyList<SheetView> Sheets);

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

public sealed record SheetCell(
    int Row, int Col, string Text, string Align,
    bool Bold, bool Italic, bool Underline,
    string? Color, string? Fill, bool IsNumber,
    string? FontName = null, double? FontSizePt = null, bool Wrap = false,
    string VAlign = "Bottom");

/// <summary>Loads an .xlsx into a <see cref="WorkbookView"/> via <c>Chuvadi.Sheets</c>.</summary>
public sealed class XlsxViewService
{
    public WorkbookView Load(string path, string? password = null)
    {
        Workbook wb;
        try
        {
            wb = string.IsNullOrEmpty(password) ? Workbook.Load(path) : Workbook.Load(path, password);
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
            foreach (var c in raw)
            {
                if (c.Row < minR) minR = c.Row;
                if (c.Row > maxR) maxR = c.Row;
                if (c.Column < minC) minC = c.Column;
                if (c.Column > maxC) maxC = c.Column;
                cells.Add(MapCell(c));
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

    private static SheetCell MapCell(Cell c)
    {
        CellStyle? st = c.Style;
        bool isNum = c.Value is double or float or int or long or decimal;
        string text = FormatValue(c.Value, st?.NumberFormat);
        string align = st is { } s && s.HAlign != HorizontalAlign.General
            ? s.HAlign.ToString()
            : (isNum ? "Right" : "Left");

        return new SheetCell(
            c.Row, c.Column, text, align,
            st?.Bold ?? false, st?.Italic ?? false, st?.Underline ?? false,
            Hex(st?.FontColor), Hex(st?.FillColor), isNum,
            st is { } fs && !string.IsNullOrWhiteSpace(fs.FontName) ? fs.FontName : null,
            st is { } ss && ss.FontSize > 0 ? ss.FontSize : null,
            st?.WrapText ?? false,
            st is { } vs && vs.VAlign != VerticalAlign.Bottom ? vs.VAlign.ToString() : "Bottom");
    }

    private static string FormatValue(object? v, string? fmt) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "TRUE" : "FALSE",
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("d", CultureInfo.CurrentCulture)
                                                     : dt.ToString("g", CultureInfo.CurrentCulture),
        double d => FormatNumber(d, fmt),
        float f => FormatNumber(f, fmt),
        decimal m => FormatNumber((double)m, fmt),
        int i => i.ToString(CultureInfo.CurrentCulture),
        long l => l.ToString(CultureInfo.CurrentCulture),
        _ => v.ToString() ?? "",
    };

    /// <summary>Lightweight number-format handling: percent, thousands, and a fixed/auto
    /// decimal count. Not a full Excel format-code engine (v1).</summary>
    private static string FormatNumber(double d, string? fmt)
    {
        if (!string.IsNullOrWhiteSpace(fmt))
        {
            if (fmt.Contains('%'))
                return (d * 100).ToString(DecimalsFrom(fmt) is int dp ? "N" + dp : "0.##", CultureInfo.CurrentCulture) + "%";

            bool thousands = fmt.Contains("#,##0") || fmt.Contains("#,###");
            int? dec = DecimalsFrom(fmt);
            if (thousands) return d.ToString("N" + (dec ?? 0), CultureInfo.CurrentCulture);
            if (dec is int dd) return d.ToString("F" + dd, CultureInfo.CurrentCulture);
        }

        // Auto: integers without a decimal point, otherwise trim to a sensible precision.
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.CurrentCulture);
        return d.ToString("0.######", CultureInfo.CurrentCulture);
    }

    private static int? DecimalsFrom(string fmt)
    {
        int dot = fmt.IndexOf('.');
        if (dot < 0) return null;
        int n = 0;
        for (int i = dot + 1; i < fmt.Length && fmt[i] == '0'; i++) n++;
        return n;
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
