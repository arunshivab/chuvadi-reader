using Chuvadi.Docs.Model;
using ChuvadiReader.Platform;

namespace ChuvadiReader.Core.Documents;

// ─────────────────────────────────────────────────────────────────────────────
// Presentation-free render model for a Word document. The Ui renders these with
// Razor (so the app theme + styling stay in the Ui, library usage stays here).
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WordView(
    string Title, string PageSize, bool Landscape,
    IReadOnlyList<WordBlock> Blocks,
    IReadOnlyList<WordBlock> Header, IReadOnlyList<WordBlock> Footer)
{
    /// <summary>What reading the document allocated (WT-19); see <see cref="ViewMemory.Released"/>.</summary>
    public long LoadedBytes { get; init; }

    /// <summary>D-168: what the file refers to but the library could not read and left out, in plain words
    /// ("a header", "2 pictures"), from <c>Document.SkippedParts</c>. Empty for a sound file. The viewer says so
    /// instead of showing a document that silently lacks something.</summary>
    public IReadOnlyList<string> LeftOut { get; init; } = [];
}

public abstract record WordBlock;

/// <summary>Level 0 = document Title; 1–3 = Heading1–3.</summary>
public sealed record WordHeading(int Level, IReadOnlyList<WordRun> Runs, string Align) : WordBlock;

/// <param name="ListLabel">The marker Word shows for a list item ("1.", "2.3.", "iv)", "•"), from the library's
/// numbering (Document.GetListLabels); null when the paragraph isn't a list item.</param>
public sealed record WordPara(IReadOnlyList<WordRun> Runs, string Align, string ListKind, int ListLevel, bool Quote, string? ListLabel = null) : WordBlock;

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
public sealed class DocxViewService(IPlatformInfo? platform = null)
{
    /// <summary>On a shared server (the Web portal): at most 20 MB per part, 64 MB in total (D-136).</summary>
    public const long ServerPartBytes = 20L * 1024 * 1024, ServerTotalBytes = 64L * 1024 * 1024;

    /// <summary>On a person's own computer or phone: at most 100 MB per part, 400 MB in total, so medical and
    /// scanned documents with one large picture still open (D-137).</summary>
    public const long LocalPartBytes = 100L * 1024 * 1024, LocalTotalBytes = 400L * 1024 * 1024;

    /// <summary>B-08, D-168: the library's limits for a file somebody was sent (<c>ForUntrustedInput</c>, Docs 2.1.0:
    /// compression ratio 200:1, 10,000 entries, XML depth 128, spin count 500,000, 1,000,000 elements a read), with
    /// the agreed sizes on top. They are checked against the sizes the zip declares and the bytes actually inflated
    /// (declared sizes can lie), so zip bombs never reach memory. Tighter where many people share one server
    /// (<see cref="PlatformCapabilities.ServerSide"/>, where the library's 50 MB file size also applies), roomier where
    /// people open their own files: there the file itself may be as large as its contents may unpack to.</summary>
    public static DocxReaderOptions ReadOptions(bool serverSide)
    {
        var o = DocxReaderOptions.ForUntrustedInput();
        o.MaxPartBytes = serverSide ? ServerPartBytes : LocalPartBytes;
        o.MaxTotalUncompressedBytes = serverSide ? ServerTotalBytes : LocalTotalBytes;
        if (!serverSide)
        {
            o.MaxPackageBytes = LocalTotalBytes;
        }

        return o;
    }

    private readonly bool _serverSide = platform?.Capabilities.ServerSide ?? false;

    /// <param name="cancellationToken">Stops the read (B-08b): "Stop opening", a closed tab, or the viewer's
    /// time limit. The library checks it on every XML node, so it takes effect within a moment.</param>
    public WordView Load(string path, string? password = null, CancellationToken cancellationToken = default)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            return LoadCore(path, password, cancellationToken) with { LoadedBytes = GC.GetAllocatedBytesForCurrentThread() - before };
        }
        catch
        {
            ViewMemory.Released(GC.GetAllocatedBytesForCurrentThread() - before);   // WT-19: a stopped heavy open
            throw;
        }
    }

    private WordView LoadCore(string path, string? password, CancellationToken cancellationToken)
    {
        Document doc;
        try
        {
            doc = Document.Load(path, string.IsNullOrEmpty(password) ? null : password, ReadOptions(_serverSide), cancellationToken);
        }
        catch (DocxSecurityLimitException ex)
        {
            throw new DocumentViewException(ViewErrorReason.TooLarge,
                "This Word document is too large to open: its contents unpack to more than Chuvadi allows. It may be damaged.", ex);
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

        // Every list item's label as Word shows it, numbering through paragraph styles included (D-137).
        var labels = doc.GetListLabels();
        var blocks = new List<WordBlock>(doc.Blocks.Count);
        foreach (var b in doc.Blocks)
        {
            if (b is Paragraph p) blocks.Add(MapParagraph(p, labels));
            else if (b is DocTable t) blocks.Add(MapTable(t, labels));
        }

        // Title = first Title paragraph, else first Heading1, else the file name.
        string title =
            FirstText(blocks, h => h.Level == 0)
            ?? FirstText(blocks, h => h.Level == 1)
            ?? Path.GetFileNameWithoutExtension(path);

        bool landscape = doc.Page.Orientation == PageOrientation.Landscape;
        var header = MapHeaderFooter(doc.Header, doc.FirstPageHeader, labels);
        var footer = MapHeaderFooter(doc.Footer, doc.FirstPageFooter, labels);
        return new WordView(title, doc.Page.Size.ToString(), landscape, blocks, header, footer) { LeftOut = LeftOut(doc.SkippedParts) };
    }

    /// <summary>The skipped parts in plain words, grouped by kind and counted: "a header", "2 pictures".</summary>
    internal static IReadOnlyList<string> LeftOut(IReadOnlyList<DocxSkippedPart> skipped)
    {
        if (skipped.Count == 0)
        {
            return [];
        }

        return [.. skipped.GroupBy(s => Kind(s.PartName)).Select(g => g.Count() == 1 ? g.Key.One : $"{g.Count()} {g.Key.Many}")];
    }

    private static (string One, string Many) Kind(string partName)
    {
        string name = partName[(partName.LastIndexOf('/') + 1)..];
        bool Is(string start) => name.StartsWith(start, StringComparison.OrdinalIgnoreCase);
        if (partName.Contains("/media/", StringComparison.OrdinalIgnoreCase)) return ("a picture", "pictures");
        if (partName.Contains("/fonts/", StringComparison.OrdinalIgnoreCase)) return ("an embedded font", "embedded fonts");
        if (Is("header")) return ("a header", "headers");
        if (Is("footer")) return ("a footer", "footers");
        if (Is("footnotes")) return ("the footnotes", "footnotes");
        if (Is("endnotes")) return ("the endnotes", "endnotes");
        if (Is("comments")) return ("the comments", "comments");
        if (Is("styles")) return ("the styles", "styles");
        if (Is("numbering")) return ("the list numbering", "list numbering");
        if (Is("theme")) return ("the theme", "themes");
        if (Is("settings")) return ("the settings", "settings");
        if (Is("fontTable")) return ("the font list", "font lists");
        if (partName.Contains("docProps", StringComparison.OrdinalIgnoreCase)) return ("the document properties", "document properties");
        return ("a part", "parts");
    }

    private static IReadOnlyList<WordBlock> MapHeaderFooter(HeaderFooterContent? primary, HeaderFooterContent? fallback, IReadOnlyDictionary<Paragraph, ListLabel> labels)
    {
        var src = (primary is { Count: > 0 }) ? primary : (fallback is { Count: > 0 } ? fallback : null);
        if (src is null) return Array.Empty<WordBlock>();
        var list = new List<WordBlock>(src.Count);
        foreach (var p in src) list.Add(MapParagraph(p, labels));
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

    private static WordBlock MapParagraph(Paragraph p, IReadOnlyDictionary<Paragraph, ListLabel> labels)
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

        bool quote = p.Style == ParagraphStyle.Quote;
        if (labels.TryGetValue(p, out var label))
        {
            var kind = label.LevelDefinition.Format == NumberFormat.Bullet ? "bullet" : "number";
            return new WordPara(runs, align, kind, Math.Max(0, label.Level), quote, MarkerText(label.Text));
        }

        string list = p.List switch { ListKind.Bullet => "bullet", ListKind.Number => "number", _ => "" };
        return new WordPara(runs, align, list, Math.Max(0, p.ListLevel), quote);
    }

    /// <summary>
    /// A list marker as text any device can draw. Word writes its bullets as private-use characters of the
    /// Symbol and Wingdings fonts (U+F0B7 in Symbol is Word's standard "•"), which only Windows' own fonts
    /// draw; elsewhere they show as an empty box. Those few are given their standard Unicode shape. Only the
    /// marker glyph is mapped; numbers ("1.", "iv)") and the document's own text are never changed.
    /// </summary>
    public static string MarkerText(string label)
    {
        if (label.Length != 1 || label[0] < '\uF000' || label[0] > '\uF0FF')
        {
            return label;
        }

        return label[0] switch
        {
            '\uF0B7' => "\u2022", // Symbol: bullet •
            '\uF0A7' => "\u25AA", // Wingdings: small square ▪
            '\uF06E' => "\u25A0", // Wingdings: square ■
            '\uF071' => "\u2751", // Wingdings: shadowed square ❑
            '\uF076' => "\u2756", // Wingdings: diamond ❖
            '\uF0D8' => "\u27A2", // Wingdings: arrowhead ➢
            '\uF0FC' => "\u2714", // Wingdings: check ✔
            _ => "\u2022",           // any other symbol-font bullet: •
        };
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

    private static WordBlock MapTable(DocTable t, IReadOnlyDictionary<Paragraph, ListLabel> labels)
    {
        var rows = new List<WordRow>(t.Rows.Count);
        foreach (var row in t.Rows)
        {
            var cells = new List<WordCell>(row.Cells.Count);
            foreach (var c in row.Cells)
            {
                // D-136: a loaded cell's content is TableCell.Blocks (paragraphs and nested tables, in order);
                // Paragraphs stayed empty for loaded files, so cells showed blank.
                var content = new List<WordBlock>(c.Blocks.Count);
                foreach (var cb in c.Blocks)
                {
                    if (cb is Paragraph cp) content.Add(MapParagraph(cp, labels));
                    else if (cb is DocTable nested) content.Add(MapTable(nested, labels));
                }
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
