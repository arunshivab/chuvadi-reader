using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>Patch 35's smaller fixes from the Windows test (23 Nov): BUG-14/15, HO33, BUG-12, the shared open.</summary>
public class Patch35FixesTests
{
    [Fact]
    public void Markdown_nested_far_too_deep_is_refused_before_it_is_parsed()
    {
        var quotes = new string('>', 5000) + " deep";
        var ex = Assert.Throws<DocumentViewException>(() => MarkdownRenderer.Render(quotes));
        Assert.Equal(MarkdownRenderer.TooDeepText, ex.Message);

        var lists = string.Join("\n", Enumerable.Range(0, 400).Select(i => new string(' ', i * 2) + "- item " + i));
        Assert.Throws<DocumentViewException>(() => MarkdownRenderer.Render(lists));
    }

    [Fact]
    public void Ordinary_Markdown_nesting_still_draws()
    {
        var md = "> quote\n>> inner\n\n- a\n  - b\n    - c\n      1. d\n\n10. ten\n> - in a quote";
        var r = MarkdownRenderer.Render(md);
        Assert.Contains("<blockquote", r.Html);
        Assert.True(MarkdownRenderer.DeepestNesting(md.Split('\n')) < 10);
    }

    [Fact]
    public async Task A_file_the_app_closed_while_opening_comes_back_held_not_opened()
    {
        var storage = new MemoryStorage();
        var path = TestPdf.TextPage("Crashed once");
        var first = new TabsService(new ChuvadiPdfReader(), storage);
        var tab = await first.OpenAsync(path, "crash.pdf", DocumentFormat.Pdf);
        Assert.NotNull(tab.Session);
        await first.MarkOpeningAsync(tab);   // as if the app died while opening it (a viewer tab marks itself like this)

        var next = new TabsService(new ChuvadiPdfReader(), storage);
        await next.RestoreAsync();
        var held = Assert.Single(next.Tabs);
        Assert.True(held.HeldAfterCrash);
        Assert.Null(held.Session);
        Assert.Equal(TabsService.HeldAfterCrashText, held.Error);

        await next.OpenHeldAsync(held.Id);
        Assert.False(held.HeldAfterCrash);
        Assert.NotNull(held.Session);

        // And the start after that one holds nothing.
        var third = new TabsService(new ChuvadiPdfReader(), storage);
        await third.RestoreAsync();
        Assert.False(Assert.Single(third.Tabs).HeldAfterCrash);
    }

    [Fact]
    public async Task A_failed_repair_writes_nothing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "chvd-rec-" + Guid.NewGuid().ToString("N"));
        var junk = TestPdf.WriteTemp(System.Text.Encoding.ASCII.GetBytes("not a pdf at all, just words"));
        var svc = new RecoveryService(new SaveFolderService(new MemoryStorage()));
        await Assert.ThrowsAnyAsync<Exception>(() => svc.RecoverAsync(junk, folder));
        Assert.True(!Directory.Exists(folder) || Directory.GetFiles(folder).Length == 0);
    }

    [Fact]
    public void A_file_another_program_holds_open_for_writing_still_reads()
    {
        var path = TestPdf.TextPage("Held");
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.NotEmpty(SharedRead.AllBytes(path));
        using var doc = PdfFiles.Open(path);
        Assert.Equal(1, doc.PageCount);
    }
}

/// <summary>VW01: merged Word table cells draw as one cell, with no extra column.</summary>
public class WordMergedCellsTests
{
    [Fact]
    public void Merged_cells_skip_their_placeholders_and_span_rows()
    {
        var doc = new Chuvadi.Docs.Model.Document();
        var t = doc.AddTable(columns: 3);
        t.AddRow("Wide", "", "C");
        t.AddRow("Tall", "B2", "C2");
        t.AddRow("", "B3", "C3");
        t.Rows[0].Cell(0).ColumnSpan = 2;
        t.Rows[1].Cell(0).VerticalMerge = Chuvadi.Docs.Model.VerticalMerge.Restart;
        t.Rows[2].Cell(0).VerticalMerge = Chuvadi.Docs.Model.VerticalMerge.Continue;
        var path = Path.Combine(Path.GetTempPath(), "chvd-merge-" + Guid.NewGuid().ToString("N") + ".docx");
        doc.SaveTo(path);

        var view = new DocxViewService().Load(path);
        var table = Assert.IsType<WordTableBlock>(Assert.Single(view.Blocks, b => b is WordTableBlock));
        Assert.Equal(new[] { 2, 3, 2 }, table.Rows.Select(r => r.Cells.Count).ToArray());   // no drawn placeholders
        Assert.Equal(2, table.Rows[0].Cells[0].ColSpan);
        Assert.Equal(2, table.Rows[1].Cells[0].RowSpan);
        // Every row still covers the three grid columns: 2+1, 1+1+1, and (under the tall cell) 1+1.
        Assert.Equal(new[] { 3, 3, 2 }, table.Rows.Select(r => r.Cells.Sum(c => c.ColSpan)).ToArray());
    }
}

/// <summary>D-210 / D-211 (BUG-11, BUG-18): text the saved file can't hold yet is refused, naming the letters; plain
/// Latin is written as typed.</summary>
public class TextNotHeldTests
{
    [Fact]
    public void Plain_Latin_is_held_and_the_rest_is_named()
    {
        Assert.Null(LibraryHolds.TextNotHeld("Café Ñandú, straße 12 × 3 ½"));
        var letters = LibraryHolds.LettersNotHeld("Test தமிழ் 😀 café – €");
        Assert.Contains("😀", letters);
        Assert.Contains("–", letters);
        Assert.Contains("€", letters);
        Assert.DoesNotContain("é", letters);
        Assert.StartsWith("These letters can't be saved into the file yet:", LibraryHolds.TextNotHeld("DRAFT வரைவு"));
    }

    [Fact]
    public async Task A_text_mark_with_letters_not_held_is_refused_before_anything_is_written()
    {
        var src = File.ReadAllBytes(TestPdf.BlankPage(300, 200));
        var texts = new Dictionary<int, PageText> { [0] = new(0, new[] { new TextAnno(0.1, 0.1, 0.8, 0.3, "Test தமிழ் 😀", "Helvetica", false, false, 12, "#000000", "left", 0, 1.2) }) };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new RedactService().FlattenAsync(src, SaveLock.None,
            new Dictionary<int, PageRedaction>(), "#000000", texts, new Dictionary<int, PageImage>(), new Dictionary<int, PageShape>()));
        Assert.Contains("😀", ex.Message);
    }

    [Fact]
    public void A_watermark_in_Tamil_is_refused_and_one_in_plain_Latin_is_written()
    {
        var src = File.ReadAllBytes(TestPdf.BlankPage(300, 200));
        var st = new StampService();
        Assert.Throws<InvalidOperationException>(() => st.ApplyWatermark(src, new DeskWatermark { Text = "DRAFT வரைவு" }, new[] { 0 }));
        Assert.NotEqual(src, st.ApplyWatermark(src, new DeskWatermark { Text = "BROUILLON é" }, new[] { 0 }));
    }
}

/// <summary>VW25 and BUG-03.</summary>
public class TextAndInfoTests
{
    [Fact]
    public void UTF16_with_no_byte_order_mark_reads_as_text_and_binary_stays_refused()
    {
        var be = System.Text.Encoding.BigEndianUnicode.GetBytes("Discharge summary\nNo BOM here.");
        Assert.Equal("Discharge summary\nNo BOM here.", TextViewService.Decode(be, out var name));
        Assert.Equal("UTF-16 BE", name);
        var le = System.Text.Encoding.Unicode.GetBytes("Plain words, little end first.");
        Assert.Equal("Plain words, little end first.", TextViewService.Decode(le, out name));
        Assert.Equal("UTF-16 LE", name);
        var bin = new byte[64]; new Random(3).NextBytes(bin); bin[5] = 0; bin[9] = 0;
        Assert.Throws<DocumentViewException>(() => TextViewService.Decode(bin, out _));
    }

    [Fact]
    public async Task Marks_keep_the_files_title_author_subject_and_keywords()
    {
        byte[] src;
        using (var doc = Chuvadi.Pdf.Documents.PdfDocument.Open(TestPdf.TextPage("Report")))
        using (var ms = new MemoryStream())
        {
            Chuvadi.Pdf.Operations.DocumentInfo.Apply(ms, doc, "Discharge 4471", "Dr. A", "Ward 3", "oncology");
            src = ms.ToArray();
        }

        var texts = new Dictionary<int, PageText> { [0] = new(0, new[] { new TextAnno(0.1, 0.1, 0.5, 0.2, "Seen", "Helvetica", false, false, 12, "#000000", "left", 0, 1.2) }) };
        var outBytes = await new RedactService().FlattenAsync(src, SaveLock.None, new Dictionary<int, PageRedaction>(), "#000000", texts, new Dictionary<int, PageImage>(), new Dictionary<int, PageShape>());
        using var o = Chuvadi.Pdf.Documents.PdfDocument.Open(new MemoryStream(outBytes));
        Assert.Equal(("Discharge 4471", "Dr. A", "Ward 3", "oncology"), (o.Title, o.Author, o.Subject, o.Keywords));
    }
}

/// <summary>CR70 / CR71: damaged or enormous images are refused at the desk, in a sentence.</summary>
public class DeskImageCheckTests
{
    [Fact]
    public void A_whole_small_png_is_taken_and_cut_short_or_enormous_ones_are_refused()
    {
        var png = TestPdf.SolidPng(20, 10, 200, 30, 30);
        Assert.Null(ImageCheck.Refusal(TestPdf.WriteTemp(png)));
        Assert.Contains("cut short", ImageCheck.Refusal(TestPdf.WriteTemp(png[..(png.Length - 20)])));
        var huge = (byte[])png.Clone();
        huge[16] = 0; huge[17] = 0; huge[18] = 0xEA; huge[19] = 0x60;   // 60,000 px wide
        Assert.Contains("too large", ImageCheck.Refusal(TestPdf.WriteTemp(huge)));
        Assert.Contains("isn't an image", ImageCheck.Refusal(TestPdf.WriteTemp(System.Text.Encoding.ASCII.GetBytes("hello, not an image at all"))));
    }
}

/// <summary>PR21, with the Windows test's own files (23 Nov package): F08 signed by another tool (pyHanko, field
/// "Sig1") takes a second signature and keeps the first.</summary>
public class CountersignOtherToolTests
{
    [Fact]
    public async Task A_file_signed_elsewhere_is_countersigned_and_its_signature_stays()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Signing");
        var output = TestPdf.TempOutput();
        await TestSigning.SignWithPfxAsync(Path.Combine(dir, "F08-signed-valid.pdf"), output, Path.Combine(dir, "S01-test-rsa-pw-sign123.pfx"), "sign123",
            new CertificateSignOptions { Reason = "Countersigned" });

        var report = await new SignatureCheckService().CheckAsync(output);
        Assert.Equal(2, report.Signatures.Count);
        Assert.Equal("Sig1", report.Signatures[0].FieldName);
        Assert.True(report.Signatures[0].IntegrityVerified);   // the first signature's bytes are untouched
        Assert.True(report.Signatures[1].IntegrityVerified);
        var original = File.ReadAllBytes(Path.Combine(dir, "F08-signed-valid.pdf"));
        Assert.True(File.ReadAllBytes(output).AsSpan(0, original.Length).SequenceEqual(original));   // appended, not rewritten
    }
}

/// <summary>RD47: a new desk made from the Reader before Arrange was used survives the kept desks coming back.</summary>
public class SendToNewDeskTests
{
    private sealed class Folder(string root) : ChuvadiReader.Platform.IAppPaths
    {
        public string AppDataFolder { get; } = Path.Combine(root, "data");
        public string WorkingFolder { get; } = Path.Combine(root, "work");
        public string? DefaultSaveFolder => null;
    }

    [Fact]
    public async Task A_page_sent_to_a_new_desk_stays_when_kept_desks_come_back()
    {
        var paths = new Folder(Path.Combine(Path.GetTempPath(), "chvd-rd47-" + Guid.NewGuid().ToString("N")));
        var src = TestPdf.TextPage("Kept");
        BenchService Bench() => new(new PlaceholderPdfReader(), new BenchComposer(), new ExportService(), paths, new SessionService(paths));

        // A previous run kept one desk with a page.
        var before = Bench();
        await before.EnsureRestoredAsync();
        before.EnsureDesk();
        var i0 = await before.EnsureSourceAsync(src, "kept.pdf");
        before.AddPageToDesk(before.Desks[0].Id, i0, 0);
        await before.KeepAsync();

        // This run, the Reader's way: restore first, then add the new desk and the page.
        var now = Bench();
        await now.EnsureRestoredAsync();
        var desk = now.AddDesk();
        var idx = await now.EnsureSourceAsync(src, "kept.pdf");
        now.AddPageToDesk(desk.Id, idx, 0);
        Assert.Contains(now.Desks, d => d.Id == desk.Id && d.Pages.Count == 1);
        Assert.Contains(now.Desks, d => d.Id != desk.Id && d.Pages.Count == 1);   // the kept one is back too
    }
}

/// <summary>RD35: a desk's working copy is told from the person's own files.</summary>
public class WorkingCopyTests
{
    [Fact]
    public void Only_a_desk_working_copy_in_the_scratch_folder_counts()
    {
        var scratch = ChuvadiReader.Core.Storage.ScratchFiles.Folder(null);
        Assert.True(ChuvadiReader.Core.Storage.ScratchFiles.IsWorkingCopy(null, Path.Combine(scratch, "Desk 1-" + Guid.NewGuid().ToString("N") + ".pdf")));
        Assert.False(ChuvadiReader.Core.Storage.ScratchFiles.IsWorkingCopy(null, Path.Combine(scratch, "report.pdf")));
        Assert.False(ChuvadiReader.Core.Storage.ScratchFiles.IsWorkingCopy(null, Path.Combine(Path.GetTempPath(), "elsewhere", "Desk 1-" + Guid.NewGuid().ToString("N") + ".pdf")));
    }
}

/// <summary>BUG-20: a PDF tab keeps the drawn pages near the one in view, not every page ever drawn.</summary>
public class DrawnPagesKeptTests
{
    [Fact]
    public async Task Scrolling_through_a_long_file_keeps_only_pages_near_the_view()
    {
        var b = Chuvadi.Pdf.Authoring.PdfDocumentBuilder.Create();
        for (int i = 0; i < 60; i++)
            b.AddPage(new Chuvadi.Pdf.Authoring.PageSize(300, 400)).DrawText($"Page {i + 1}", 40, 40, "Helvetica", 14, Chuvadi.Pdf.Authoring.Color.FromHex("#000000"));
        var path = TestPdf.WriteTemp(b.ToByteArray());
        using var s = (ChuvadiPdfSession)await new ChuvadiPdfReader().OpenAsync(path, "long.pdf", null);
        for (int n = 0; n < 60; n++) await s.RenderPageSvgAsync(n);
        Assert.True(s.CachedPageCountForTests <= ChuvadiPdfSession.KeepPages, $"{s.CachedPageCountForTests} kept");
        Assert.True(s.TryGetRenderedSvg(59, out _));    // the page just drawn
        Assert.True(s.TryGetRenderedSvg(50, out _));    // its neighbours
        Assert.False(s.TryGetRenderedSvg(0, out _));    // far behind: dropped, drawn again when it comes back
        Assert.False(string.IsNullOrEmpty(await s.RenderPageSvgAsync(0)));
    }
}
