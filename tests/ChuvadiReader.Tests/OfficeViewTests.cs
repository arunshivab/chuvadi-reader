using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Platform;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>
/// D-136: Word and Excel viewing on Chuvadi.Docs 2.0.0 (now 2.1.0, D-168) and Chuvadi.Sheets 2.0.2 (now 2.0.4, seventeenth patch). Every value comes through as
/// the file holds it. The truth is <c>Fixtures/Office/expected.json</c>, written by
/// <c>tools/OfficeCorpus/make.py</c> from the files' own XML and openpyxl, never from Chuvadi. Plus the
/// libraries' limits (B-08) and cancellation (B-08b).
/// D-161: a cell shows its display text and keeps its stored value; these exact-data checks are on the stored value
/// (<see cref="SheetCell.Value"/>), which copy, export and Find use. The display text is checked in XlsxDisplayTests.
/// </summary>
public sealed class OfficeViewTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Office");

    private static readonly JsonElement Expected =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "expected.json"))).RootElement;

    private static (int Row, int Col) Address(string a)
    {
        int col = 0, i = 0;
        for (; i < a.Length && char.IsLetter(a[i]); i++) col = (col * 26) + (char.ToUpperInvariant(a[i]) - 'A' + 1);
        return (int.Parse(a[i..], CultureInfo.InvariantCulture), col);
    }

    private static Dictionary<(int, int), string> Cells(WorkbookView wb, int sheet) =>
        wb.Sheets[sheet].Cells.ToDictionary(c => (c.Row, c.Col), c => c.Value);

    private static void SameNumber(double want, string shown, string where)
    {
        Assert.True(double.TryParse(shown, NumberStyles.Float, CultureInfo.InvariantCulture, out var got), $"{where}: '{shown}' isn't a number");
        Assert.True(Math.Abs(got - want) <= Math.Abs(want) * 1e-12, $"{where}: file holds {want}, viewer shows {shown}");
    }

    [Theory]
    [InlineData("values.xlsx")]
    [InlineData("dates1904.xlsx")]
    public void Every_kind_of_cell_value_shows_as_the_file_holds_it(string file)
    {
        var cells = Cells(new XlsxViewService().Load(Path.Combine(Dir, file)), 0);
        foreach (var group in Expected.GetProperty(file).EnumerateObject().Where(g => g.Name != "note"))
        {
            foreach (var cell in group.Value.EnumerateObject())
            {
                var where = $"{file} {cell.Name} ({group.Name})";
                Assert.True(cells.TryGetValue(Address(cell.Name), out var shown), $"{where}: missing");
                switch (group.Name)
                {
                    case "numbers":
                        SameNumber(cell.Value.GetDouble(), shown!, where);
                        break;
                    case "dates_1900":
                    case "dates_1904":
                        // Excel stores a date as a day count; the stored value is that number (computed here from the
                        // date openpyxl read, with Excel's own day zero: 1899-12-31 with the fictitious 29 Feb 1900, or 1904-01-01).
                        SameNumber(Serial(cell.Value.GetString()!, group.Name == "dates_1904"), shown!, where);
                        break;
                    case "booleans":
                        Assert.Equal(cell.Value.GetBoolean() ? "TRUE" : "FALSE", shown);
                        break;
                    default: // strings (spaces, Tamil, line breaks, & and < kept), errors, dates in both systems
                        Assert.True(cell.Value.GetString() == shown, $"{where}: file holds '{cell.Value.GetString()}', viewer shows '{shown}'");
                        break;
                }
            }
        }
    }

    private static bool IsoDate(string s) =>
        DateTime.TryParseExact(s, ["yyyy-MM-dd", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static double Serial(string iso, bool date1904)
    {
        var d = DateTime.ParseExact(iso, iso.Length > 10 ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (date1904) return (d - new DateTime(1904, 1, 1)).TotalDays;
        return d < new DateTime(1900, 3, 1) ? (d - new DateTime(1899, 12, 31)).TotalDays : (d - new DateTime(1899, 12, 30)).TotalDays;
    }

    [Fact]
    public void A_200_row_workbook_shows_every_cell_as_the_file_holds_it()
    {
        var wb = new XlsxViewService().Load(Path.Combine(Dir, "realistic.xlsx"));
        var e = Expected.GetProperty("realistic.xlsx");
        var count = 0;
        foreach (var cell in e.GetProperty("cells").EnumerateObject())
        {
            var bang = cell.Name.IndexOf('!');
            var sheet = int.Parse(cell.Name[..bang], CultureInfo.InvariantCulture);
            var shown = Cells(wb, sheet).GetValueOrDefault(Address(cell.Name[(bang + 1)..]));
            Assert.True(shown is not null, $"{cell.Name}: missing");
            if (cell.Value.GetProperty("kind").GetString() == "number")
            {
                SameNumber(cell.Value.GetProperty("value").GetDouble(), shown!, cell.Name);
            }
            else if (IsoDate(cell.Value.GetProperty("value").GetString()!) && double.TryParse(shown, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                // A date cell (make.py writes openpyxl's dates as ISO text): the stored value is its day count.
                SameNumber(Serial(cell.Value.GetProperty("value").GetString()!, date1904: false), shown!, cell.Name);
            }
            else
            {
                Assert.Equal(cell.Value.GetProperty("value").GetString(), shown);
            }

            count++;
        }

        Assert.Equal(1153, count);
        foreach (var empty in e.GetProperty("empty").EnumerateArray().Select(x => x.GetString()!))
        {
            var bang = empty.IndexOf('!');
            Assert.Equal(string.Empty, Cells(wb, 0).GetValueOrDefault(Address(empty[(bang + 1)..]), string.Empty));
        }
    }

    [Fact]
    public void A_stray_cell_at_the_sheets_far_corner_opens_quickly()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var wb = new XlsxViewService().Load(Path.Combine(Dir, "far-cell.xlsx")); // B-09: cell at XFD1048576
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed}");
        Assert.NotEmpty(wb.Sheets);
    }

    private static List<string> Texts(IEnumerable<WordBlock> blocks) => blocks
        .Select(b => b switch { WordHeading h => h.Runs, WordPara p => p.Runs, _ => null })
        .Where(runs => runs is not null && !runs.All(r => r.ImageDataUrl is not null))
        .Select(runs => string.Concat(runs!.Select(r => r.Text)))
        .ToList();

    [Theory]
    [InlineData("discharge.docx")]
    [InlineData("landscape.docx")]
    public void Word_text_tables_headers_and_pages_come_through_as_the_file_holds_them(string file)
    {
        var view = new DocxViewService().Load(Path.Combine(Dir, file));
        var e = Expected.GetProperty(file);

        // Tabs and line breaks stay in the text (\t, \n); double, leading and trailing spaces stay.
        Assert.Equal(e.GetProperty("paragraph_texts_in_order").EnumerateArray().Select(x => x.GetString()), Texts(view.Blocks));
        if (e.TryGetProperty("table", out var table))
        {
            var t = Assert.Single(view.Blocks.OfType<WordTableBlock>());
            var shown = t.Rows.Select(r => r.Cells.Select(c => string.Concat(Texts(c.Content))).ToList()).ToList();
            Assert.Equal(table.EnumerateArray().Select(r => r.EnumerateArray().Select(x => x.GetString()!).ToList()).ToList(), shown);
        }

        if (e.TryGetProperty("header", out var header))
        {
            Assert.Equal(header.GetString(), string.Concat(Texts(view.Header)));
            Assert.Equal(e.GetProperty("footer").GetString(), string.Concat(Texts(view.Footer)));
        }

        if (e.TryGetProperty("landscape", out var landscape))
        {
            Assert.Equal(landscape.GetBoolean(), view.Landscape);
            Assert.Equal(e.GetProperty("images").GetInt32(), view.Blocks.OfType<WordPara>().SelectMany(p => p.Runs).Count(r => r.ImageDataUrl is not null));
        }
    }

    // ── B-08: zip bombs are refused by the libraries' own limits ──

    private static string Bomb(string extension, string part, long bytes, bool likeAPicture = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bomb-{Guid.NewGuid():N}{extension}");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open()))
            {
                w.Write("<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            }

            using var s = zip.CreateEntry(part, likeAPicture ? CompressionLevel.NoCompression : CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1 << 20];
            if (likeAPicture)
            {
                new Random(7).NextBytes(zeros); // a scan or photo doesn't compress; zeros would trip the ratio check instead
            }

            for (long written = 0; written < bytes; written += zeros.Length)
            {
                s.Write(zeros, 0, (int)Math.Min(zeros.Length, bytes - written));
            }
        }

        return path;
    }

    private sealed class Host(bool serverSide) : IPlatformInfo
    {
        public PlatformKind Kind => serverSide ? PlatformKind.Web : PlatformKind.Windows;

        public FormFactor FormFactor => FormFactor.Desktop;

        public event Action? FormFactorChanged { add { } remove { } }

        public InputKinds Input => InputKinds.Mouse | InputKinds.Keyboard;

        public ShellChrome Chrome => new(WindowButtons.Right, false, serverSide, !serverSide, 0);

        public PlatformCapabilities Capabilities => new() { ServerSide = serverSide };

        public IKeyLabels Keys => null!;
    }

    [Fact]
    public void A_word_zip_bomb_is_refused_by_the_library_as_too_large_even_on_a_persons_own_device()
    {
        var path = Bomb(".docx", "word/document.xml", 120L * 1024 * 1024); // 120 MB of zeros: over the 100 MB local part limit
        Assert.True(new FileInfo(path).Length < 2 * 1024 * 1024, "the bomb itself is small");

        var ex = Assert.Throws<DocumentViewException>(() => new DocxViewService(new Host(serverSide: false)).Load(path));

        Assert.Equal(ViewErrorReason.TooLarge, ex.Reason);
        Assert.Contains("too large to open", ex.Message);
    }

    [Fact]
    public void A_document_with_a_30_MB_picture_is_too_large_on_the_shared_server_but_opens_on_a_persons_own_device()
    {
        // D-137: one large picture is realistic in a medical or scanned document. The landscape test document
        // with its picture replaced by 30 MB that doesn't compress, as a scan or photo doesn't.
        var path = Path.Combine(Path.GetTempPath(), $"big-picture-{Guid.NewGuid():N}.docx");
        File.Copy(Path.Combine(Dir, "landscape.docx"), path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("word/media/image1.png")!.Delete();
            using var s = zip.CreateEntry("word/media/image1.png", CompressionLevel.NoCompression).Open();
            var chunk = new byte[1 << 20];
            var random = new Random(7);
            for (var i = 0; i < 30; i++)
            {
                random.NextBytes(chunk);
                s.Write(chunk);
            }
        }

        var server = Assert.Throws<DocumentViewException>(() => new DocxViewService(new Host(serverSide: true)).Load(path));
        Assert.Equal(ViewErrorReason.TooLarge, server.Reason);

        var local = new DocxViewService(new Host(serverSide: false)).Load(path);
        Assert.Equal("Landscape report", Texts(local.Blocks)[0]);
        var picture = local.Blocks.OfType<WordPara>().SelectMany(p => p.Runs).Single(r => r.ImageDataUrl is not null);
        Assert.True(picture.ImageDataUrl!.Length > 30 * 1024 * 1024, "the whole picture came through");
    }

    [Fact]
    public void An_excel_zip_bomb_is_refused_by_the_library_as_too_large()
    {
        var path = Bomb(".xlsx", "xl/worksheets/sheet1.xml", 100L * 1024 * 1024); // compresses far beyond 500:1

        var ex = Assert.Throws<DocumentViewException>(() => new XlsxViewService().Load(path));

        Assert.Equal(ViewErrorReason.TooLarge, ex.Reason);
    }

    [Theory]
    [InlineData(true, 20, 64)]    // the Web portal: many people share one server (D-136)
    [InlineData(false, 100, 400)] // desktop and mobile: a person's own files (D-137)
    public void The_word_limits_are_the_agreed_ones(bool serverSide, long partMb, long totalMb)
    {
        var o = DocxViewService.ReadOptions(serverSide);
        Assert.Equal(partMb * 1024 * 1024, o.MaxPartBytes);
        Assert.Equal(totalMb * 1024 * 1024, o.MaxTotalUncompressedBytes);
    }

    // ── D-168: Docs 2.1.0 ──

    [Theory]
    [InlineData(true, 50)]    // the library's own size for a file somebody was sent
    [InlineData(false, 400)]  // a person's own file may be as large as its contents may unpack to (D-137)
    public void Word_files_open_with_the_librarys_limits_for_files_somebody_was_sent(bool serverSide, long packageMb)
    {
        var o = DocxViewService.ReadOptions(serverSide);
        var library = Chuvadi.Docs.Model.DocxReaderOptions.ForUntrustedInput();
        Assert.Equal(1_000_000, o.MaxElements);
        Assert.Equal(library.MaxElements, o.MaxElements);
        Assert.Equal(library.MaxCompressionRatio, o.MaxCompressionRatio);
        Assert.Equal(library.MaxEntries, o.MaxEntries);
        Assert.Equal(library.MaxXmlDepth, o.MaxXmlDepth);
        Assert.Equal(library.MaxSpinCount, o.MaxSpinCount);
        Assert.Equal(packageMb * 1024 * 1024, o.MaxPackageBytes);
    }

    [Fact]
    public void A_60_MB_scan_still_opens_on_a_persons_own_device()
    {
        // Larger than the library's 50 MB file size for a shared server; D-137 keeps such files opening locally.
        var path = Path.Combine(Path.GetTempPath(), $"scan-{Guid.NewGuid():N}.docx");
        File.Copy(Path.Combine(Dir, "landscape.docx"), path);
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                zip.GetEntry("word/media/image1.png")!.Delete();
                using var s = zip.CreateEntry("word/media/image1.png", CompressionLevel.NoCompression).Open();
                var chunk = new byte[1 << 20];
                var random = new Random(11);
                for (var i = 0; i < 60; i++)
                {
                    random.NextBytes(chunk);
                    s.Write(chunk);
                }
            }

            Assert.True(new FileInfo(path).Length > 50L * 1024 * 1024);
            Assert.NotEmpty(new DocxViewService().Load(path).Blocks);
            var server = Assert.Throws<DocumentViewException>(() => new DocxViewService(new Host(serverSide: true)).Load(path));
            Assert.Equal(ViewErrorReason.TooLarge, server.Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_sound_document_has_nothing_left_out() =>
        Assert.Empty(new DocxViewService().Load(Path.Combine(Dir, "discharge.docx")).LeftOut);

    [Fact]
    public void A_document_with_a_damaged_header_and_a_missing_picture_says_what_is_left_out()
    {
        var header = Path.Combine(Path.GetTempPath(), $"bad-header-{Guid.NewGuid():N}.docx");
        var picture = Path.Combine(Path.GetTempPath(), $"no-picture-{Guid.NewGuid():N}.docx");
        File.Copy(Path.Combine(Dir, "discharge.docx"), header);
        File.Copy(Path.Combine(Dir, "landscape.docx"), picture);
        try
        {
            using (var zip = ZipFile.Open(header, ZipArchiveMode.Update))
            {
                zip.GetEntry("word/header1.xml")!.Delete();
                using var w = new StreamWriter(zip.CreateEntry("word/header1.xml").Open());
                w.Write("<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p>");
            }

            using (var zip = ZipFile.Open(picture, ZipArchiveMode.Update))
            {
                zip.GetEntry("word/media/image1.png")!.Delete();
            }

            var view = new DocxViewService().Load(header);
            Assert.Equal(["a header"], view.LeftOut);
            Assert.NotEmpty(view.Blocks);   // the rest of the document is all there
            Assert.Equal(["a picture"], new DocxViewService().Load(picture).LeftOut);
        }
        finally
        {
            File.Delete(header);
            File.Delete(picture);
        }
    }

    [Fact]
    public void A_damaged_protected_file_is_damaged_not_a_wrong_password()
    {
        // The start of a protected Office file (a compound file), then nothing usable.
        var path = Path.Combine(Path.GetTempPath(), $"damaged-protected-{Guid.NewGuid():N}.docx");
        var bytes = new byte[4096];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);
        try
        {
            var ex = Assert.Throws<DocumentViewException>(() => new DocxViewService().Load(path, "secret"));
            Assert.Equal(ViewErrorReason.BadFormat, ex.Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── D-137: list markers as Word shows them (numbering through paragraph styles included) ──

    [Fact]
    public void List_items_show_the_markers_word_shows()
    {
        var items = new DocxViewService().Load(Path.Combine(Dir, "discharge.docx")).Blocks.OfType<WordPara>()
            .Where(p => p.ListKind != "").Select(p => (Text: string.Concat(p.Runs.Select(r => r.Text)), p.ListKind, p.ListLabel)).ToList();

        Assert.Equal(
            [("Paracetamol 500 mg", "bullet", "\u2022"), ("Pantoprazole 40 mg", "bullet", "\u2022"),
             ("Review in 7 days", "number", "1."), ("CBC before review", "number", "2.")],
            items);
    }

    [Theory]
    [InlineData("\uF0B7", "\u2022")] // Word's standard bullet, Symbol font
    [InlineData("\uF0A7", "\u25AA")] // Wingdings small square
    [InlineData("\uF0D8", "\u27A2")] // Wingdings arrowhead
    [InlineData("\uF0E0", "\u2022")] // any other symbol-font bullet
    [InlineData("1.", "1.")]          // numbers are never changed
    [InlineData("iv)", "iv)")]
    [InlineData("o", "o")]            // Courier New "o", a real letter
    [InlineData("\u2013", "\u2013")]
    public void Symbol_font_bullets_get_a_shape_every_device_can_draw(string label, string shown) =>
        Assert.Equal(shown, DocxViewService.MarkerText(label));

    // ── B-08b: the read's cancellation reaches the library ──

    [Fact]
    public void A_cancelled_word_read_stops_in_the_library()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new DocxViewService().Load(Path.Combine(Dir, "discharge.docx"), null, cancelled.Token));
    }

    [Fact]
    public void A_cancelled_excel_read_stops_in_the_library()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new XlsxViewService().Load(Path.Combine(Dir, "realistic.xlsx"), null, cancelled.Token));
        Assert.Equal(cancelled.Token, XlsxViewService.ReadOptions(cancelled.Token).CancellationToken);
    }
}
