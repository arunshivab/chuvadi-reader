using Chuvadi.Docs.Word;

namespace ChuvadiReader.Core.Documents;

// ─────────────────────────────────────────────────────────────────────────────
// Presentation-free render model for a Word document. The Ui renders these with
// Razor (so the app theme + styling stay in the Ui, library usage stays here).
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WordView(
    string Title, string PageSize, bool Landscape,
    IReadOnlyList<WordBlock> Blocks,
    IReadOnlyList<WordBlock> Header, IReadOnlyList<WordBlock> Footer);

public abstract record WordBlock;

/// <summary>Level 0 = document Title; 1–3 = Heading1–3.</summary>
public sealed record WordHeading(int Level, IReadOnlyList<WordRun> Runs, string Align) : WordBlock;

public sealed record WordPara(IReadOnlyList<WordRun> Runs, string Align, string ListKind, int ListLevel, bool Quote) : WordBlock;

public sealed record WordTableBlock(IReadOnlyList<WordRow> Rows, IReadOnlyList<double> ColWidthsPt) : WordBlock;

public sealed record WordRow(bool Header, IReadOnlyList<WordCell> Cells);

public sealed record WordCell(IReadOnlyList<WordBlock> Content, int ColSpan, string? ShadeHex);

/// <summary>A run of text with inline formatting, or — when <see cref="ImageDataUrl"/> is set —
/// an inline image (in which case <see cref="Text"/> is empty).</summary>
public sealed record WordRun(
    string Text, bool Bold, bool Italic, bool Underline, bool Strike,
    string? ColorHex, string? Font, double? SizePt, string? Highlight, string? Href, string? ImageDataUrl,
    double ImageWidthPt = 0, double ImageHeightPt = 0);

/// <summary>Loads a .docx into a <see cref="WordView"/> via <c>Chuvadi.Docs</c>.</summary>
public sealed class DocxViewService
{
    public WordView Load(string path, string? password = null)
    {
        Document doc;
        try
        {
            doc = string.IsNullOrEmpty(password) ? Document.Load(path) : Document.Load(path, password);
        }
        catch (DocxPasswordRequiredException ex)
        {
            throw new DocumentViewException(ViewErrorReason.PasswordProtected,
                "This document is password-protected.", ex);
        }
        catch (DocxFormatException ex)
        {
            throw new DocumentViewException(ViewErrorReason.BadFormat,
                "This file couldn't be read as a Word document.", ex);
        }

        var blocks = new List<WordBlock>(doc.Blocks.Count);
        foreach (var b in doc.Blocks)
        {
            if (b is Paragraph p) blocks.Add(MapParagraph(p));
            else if (b is DocTable t) blocks.Add(MapTable(t));
        }

        // Title = first Title paragraph, else first Heading1, else the file name.
        string title =
            FirstText(blocks, h => h.Level == 0)
            ?? FirstText(blocks, h => h.Level == 1)
            ?? Path.GetFileNameWithoutExtension(path);

        bool landscape = doc.Page.Orientation == PageOrientation.Landscape;
        var header = MapHeaderFooter(doc.Header, doc.FirstPageHeader);
        var footer = MapHeaderFooter(doc.Footer, doc.FirstPageFooter);
        return new WordView(title, doc.Page.Size.ToString(), landscape, blocks, header, footer);
    }

    private static IReadOnlyList<WordBlock> MapHeaderFooter(HeaderFooterContent? primary, HeaderFooterContent? fallback)
    {
        var src = (primary is { Count: > 0 }) ? primary : (fallback is { Count: > 0 } ? fallback : null);
        if (src is null) return Array.Empty<WordBlock>();
        var list = new List<WordBlock>(src.Count);
        foreach (var p in src) list.Add(MapParagraph(p));
        return list;
    }

    private static string? FirstText(IEnumerable<WordBlock> blocks, Func<WordHeading, bool> match)
    {
        foreach (var b in blocks)
            if (b is WordHeading h && match(h))
            {
                var s = string.Concat(h.Runs.Select(r => r.Text)).Trim();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        return null;
    }

    private static WordBlock MapParagraph(Paragraph p)
    {
        var runs = new List<WordRun>();
        foreach (var r in p.Runs)
        {
            var mapped = MapRun(r);
            if (mapped is not null) runs.Add(mapped);
        }

        string align = p.Alignment.ToString();
        int level = p.Style switch
        {
            ParagraphStyle.Title => 0,
            ParagraphStyle.Heading1 => 1,
            ParagraphStyle.Heading2 => 2,
            ParagraphStyle.Heading3 => 3,
            _ => -1,
        };
        if (level >= 0) return new WordHeading(level, runs, align);

        string list = p.List switch { ListKind.Bullet => "bullet", ListKind.Number => "number", _ => "" };
        bool quote = p.Style == ParagraphStyle.Quote;
        return new WordPara(runs, align, list, Math.Max(0, p.ListLevel), quote);
    }

    private static WordRun? MapRun(Run r)
    {
        if (r.Image is { Bytes.Length: > 0 } img)
        {
            var url = $"data:{(string.IsNullOrEmpty(img.ContentType) ? "image/png" : img.ContentType)};base64,{Convert.ToBase64String(img.Bytes)}";
            return new WordRun("", false, false, false, false, null, null, null, null, null, url, img.WidthPt, img.HeightPt);
        }

        if (string.IsNullOrEmpty(r.TextContent)) return null;

        var f = r.Format;
        return new WordRun(
            r.TextContent,
            f.Bold, f.Italic, f.Underline, f.Strikethrough,
            Hex(f.ColorHex), NullIfEmpty(f.Font), f.SizePt > 0 ? f.SizePt : null, NullIfEmpty(f.Highlight),
            NullIfEmpty(r.HyperlinkUrl), null);
    }

    private static WordBlock MapTable(DocTable t)
    {
        var rows = new List<WordRow>(t.Rows.Count);
        foreach (var row in t.Rows)
        {
            var cells = new List<WordCell>(row.Cells.Count);
            foreach (var c in row.Cells)
            {
                var content = new List<WordBlock>(c.Paragraphs.Count);
                foreach (var cp in c.Paragraphs) content.Add(MapParagraph(cp));
                cells.Add(new WordCell(content, Math.Max(1, c.ColumnSpan), Hex(c.ShadeHex)));
            }
            rows.Add(new WordRow(row.IsHeader, cells));
        }
        return new WordTableBlock(rows, t.ColumnWidthsPt ?? Array.Empty<double>());
    }

    private static string? Hex(string? hex) =>
        string.IsNullOrWhiteSpace(hex) || hex.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : hex.StartsWith('#') ? hex : "#" + hex;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
