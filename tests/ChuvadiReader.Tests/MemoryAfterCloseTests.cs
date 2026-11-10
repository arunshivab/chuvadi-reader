using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using ChuvadiReader.Core.Reader;
using Xunit;
using Path = System.IO.Path;

namespace ChuvadiReader.Tests;

[CollectionDefinition(nameof(MemoryAfterCloseTests), DisableParallelization = true)]
public sealed class MemoryMeasurementCollection;

/// <summary>
/// Sixteenth patch (Linux 36c): closing a heavy document gives its memory back. Measured on its own (no other test
/// runs alongside), with a large-format page of noise, which takes the raster path the CAD sheets take.
/// </summary>
[Collection(nameof(MemoryAfterCloseTests))]
public sealed class MemoryAfterCloseTests
{
    public MemoryAfterCloseTests()
    {
        // No waiting in tests: the relief runs at once, back to back, and its one-minute second pass is off.
        // The tests that measure memory after a close put the settle back (MeasuredAsShipped).
        MemoryRelief.Settle = TimeSpan.Zero;
        MemoryRelief.FollowUp = TimeSpan.Zero;
        MemoryRelief.MinGap = TimeSpan.Zero;
        (MemoryRelief.Last ?? Task.CompletedTask).Wait(TimeSpan.FromSeconds(30));
        MemoryRelief.ResetForTests();
        MemoryRelief.MinGap = TimeSpan.Zero;
    }

    private static string HeavyLargeFormatPage(int pages = 1)
    {
        // 3125 × 2917 random pixels (one per drawn pixel) on a 1500 × 1400 pt page (larger than A2, so it is drawn as one PNG at 150 dpi):
        // the drawn page is ~96 MB of SVG text, above MemoryRelief.HeavyBytes.
        var pixels = new PixelBuffer(3125, 2917);
        var noise = new Random(7);
        for (var y = 0; y < 2917; y++)
        {
            for (var x = 0; x < 3125; x++)
            {
                pixels.SetPixel(x, y, ColorF.FromRgb((float)noise.NextDouble(), (float)noise.NextDouble(), (float)noise.NextDouble(), 1f));
            }
        }
        using var png = new MemoryStream();
        PngEncoder.Encode(new ImageFrame(pixels, ImageColorFormat.Rgba32), png, false);
        var b = PdfDocumentBuilder.Create();
        var image = png.ToArray();
        for (var i = 0; i < pages; i++)
        {
            b.AddPage(new PageSize(1500, 1400)).DrawImage(image, 0, 0, 1500, 1400);
        }

        return TestPdf.WriteTemp(b.ToByteArray());
    }

    /// <summary>
    /// For a test that measures memory after a close: the relief settles as shipped before it collects. With no
    /// settle, the relief's collections and this test's own ran back to back from the instant of the close, and on the
    /// 4-core Windows runner they kept suspending a thread that still had the page on its stack for a moment (the
    /// drawn page alive at the measure, the document already gone, the page gone 3 to 13 ms later; found with
    /// platform/diag-b, 8 of 10 full-suite runs). The app always waits; so does the measurement.
    /// </summary>
    private static void MeasuredAsShipped() => MemoryRelief.Settle = MemoryRelief.ShippedSettle;

    private static (long Live, long Committed) Measure()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return (GC.GetTotalMemory(false), GC.GetGCMemoryInfo().TotalCommittedBytes);
    }

    [Fact]
    public async Task Letting_go_of_a_heavy_Excel_or_Word_view_asks_for_the_same_relief()
    {
        // WT-19: the viewers report what their view cost when they let it go.
        var relieved = 0;
        void Count() => Interlocked.Increment(ref relieved);
        MemoryReliefNotice.Relieved += Count;
        try
        {
            var last = MemoryRelief.Last;
            ChuvadiReader.Core.Documents.ViewMemory.Released(8L * 1024 * 1024);   // an ordinary file: nothing
            await Task.Delay(200);
            Assert.Same(last, MemoryRelief.Last);
            Assert.Equal(0, relieved);

            ChuvadiReader.Core.Documents.ViewMemory.Released(MemoryRelief.HeavyBytes);
            Assert.NotNull(MemoryRelief.Last);
            await MemoryRelief.Last!.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(relieved >= 1);
        }
        finally
        {
            MemoryReliefNotice.Relieved -= Count;
        }
    }

    [Fact]
    public async Task Closing_a_heavy_workbook_brings_memory_back_near_where_it_was()
    {
        // WT-19, measured: 120,000 rows × 6 columns, written with Chuvadi.Sheets, read as the viewer reads it.
        var path = Path.Combine(Path.GetTempPath(), "chvd-heavy-" + Guid.NewGuid().ToString("N") + ".xlsx");
        WriteHeavyWorkbook(path);
        MeasuredAsShipped();
        try
        {
            var before = Measure();
            var held = await Task.Run(() => LoadAndRelease(path));
            await Task.Yield();   // off the helper's frame (it held the view)
            Assert.True(held >= MemoryRelief.HeavyBytes, $"the workbook should count as heavy: read {held / (1024 * 1024)} MB");
            Assert.NotNull(MemoryRelief.Last);
            await MemoryRelief.Last!.WaitAsync(TimeSpan.FromSeconds(60));
            await Task.Yield();
            var after = Measure();
            Assert.True(after.Live - before.Live < 24L * 1024 * 1024,
                $"live memory grew by {(after.Live - before.Live) / (1024 * 1024)} MB after the workbook was let go");
            Assert.True(after.Committed - before.Committed < held / 2,
                $"committed memory grew by {(after.Committed - before.Committed) / (1024 * 1024)} MB; reading took {held / (1024 * 1024)} MB");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteHeavyWorkbook(string path)
    {
        var wb = new Chuvadi.Sheets.Model.Workbook();
        var sheet = wb.AddSheet("Rows");
        for (var r = 1; r <= 120_000; r++)
        {
            for (var c = 1; c <= 6; c++)
            {
                sheet.Cell(r, c).Value = c % 2 == 0 ? r * 1.5 + c : $"row {r} column {c}";
            }
        }

        wb.SaveTo(path);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static long LoadAndRelease(string path)
    {
        var view = new ChuvadiReader.Core.Documents.XlsxViewService().Load(path);
        var bytes = view.LoadedBytes;
        view = null;
        ChuvadiReader.Core.Documents.ViewMemory.Released(bytes);   // what the viewer does when the tab closes
        return bytes;
    }

    [Fact]
    public void Reading_a_workbook_or_a_document_reports_what_it_cost()
    {
        var office = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Office");
        Assert.True(new ChuvadiReader.Core.Documents.XlsxViewService().Load(Path.Combine(office, "realistic.xlsx")).LoadedBytes > 100_000);
        var docx = Directory.GetFiles(office, "*.docx").First();
        Assert.True(new ChuvadiReader.Core.Documents.DocxViewService().Load(docx).LoadedBytes > 10_000);
    }

    [Fact]
    public async Task Closing_a_heavy_document_brings_memory_back_near_where_it_was()
    {
        var path = HeavyLargeFormatPage();
        MemoryRelief.ResetForTests();
        MeasuredAsShipped();
        var before = Measure();

        var drawnChars = await OpenDrawClose(path);

        // PR #13's first Windows run measured 72 MB still live, once: this method had carried on inline, on top of
        // OpenDrawClose's own frame, which still held the drawn page (gcroot: a stack slot of that frame). Leave
        // the helper's frame before measuring. (That alone was not enough on 4 cores: see MeasuredAsShipped.)
        await Task.Yield();
        await (MemoryRelief.Last ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Yield();
        var after = Measure();

        const long mb = 1024 * 1024;
        Assert.True(drawnChars * sizeof(char) >= MemoryRelief.HeavyBytes, $"the test page is too light ({drawnChars * 2 / mb} MB)");
        Assert.True(after.Live - before.Live < 16 * mb, $"live memory after close: {before.Live / mb} → {after.Live / mb} MB");
        Assert.True(after.Committed - before.Committed < 64 * mb, $"memory kept by the runtime after close: {before.Committed / mb} → {after.Committed / mb} MB");
    }

    [Fact]
    public async Task A_second_heavy_close_inside_the_gap_waits_for_it_instead_of_being_dropped()
    {
        // Linux 36b then 36c: two drawings closed within the gap. The second must still give its memory back.
        MemoryRelief.ResetForTests();
        MemoryRelief.MinGap = TimeSpan.FromMilliseconds(400);
        try
        {
            MemoryRelief.Request();
            var first = MemoryRelief.Last!;
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            var firstDone = System.Diagnostics.Stopwatch.StartNew();

            MemoryRelief.Request();                      // inside the gap: before this change it was dropped
            var second = MemoryRelief.Last!;
            Assert.NotSame(first, second);
            MemoryRelief.Request();                      // while the second waits: it shares that relief
            Assert.Same(second, MemoryRelief.Last);

            await second.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(firstDone.ElapsedMilliseconds >= 300, $"ran {firstDone.ElapsedMilliseconds} ms after the first: the gap wasn't kept");
        }
        finally
        {
            MemoryRelief.ResetForTests();
        }
    }

    [Fact]
    public async Task A_light_document_never_asks_for_a_collection()
    {
        MemoryRelief.ResetForTests();
        var last = MemoryRelief.Last;
        var session = await new ChuvadiPdfReader().OpenAsync(TestPdf.TextPage("light"), "light.pdf");
        await session.RenderPageSvgAsync(0);
        session.Dispose();

        Assert.Same(last, MemoryRelief.Last);
    }

    [Fact]
    public void The_drawn_page_text_is_exactly_what_it_was_before_character_for_character()
    {
        var png = new byte[1000];
        new Random(3).NextBytes(png);
        foreach (var length in new[] { 0, 1, 2, 3, 998, 999, 1000 })
        {
            var (w, h) = ("595.276", "841.89");
            var before = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}pt\" height=\"{h}pt\" " +
                         $"viewBox=\"0 0 {w} {h}\" preserveAspectRatio=\"xMidYMid meet\">" +
                         $"<image x=\"0\" y=\"0\" width=\"{w}\" height=\"{h}\" preserveAspectRatio=\"none\" " +
                         $"href=\"{"data:image/png;base64," + Convert.ToBase64String(png, 0, length)}\"/></svg>";

            Assert.Equal(before, ChuvadiPdfSession.RasterSvg(w, h, png, length));
        }
    }

    // ── Sixteenth patch, part 2 (Linux step 47): the drawing stops, and a costly drawing counts as heavy ──────────

    private static string BlankA1Sheets(int count)
    {
        var b = PdfDocumentBuilder.Create();
        for (var i = 0; i < count; i++)
        {
            b.AddPage(new PageSize(2384, 1684)).DrawText("Sheet " + (i + 1), 100, 100, "Helvetica", 48, Chuvadi.Pdf.Authoring.Color.FromHex("#000000"));
        }

        return TestPdf.WriteTemp(b.ToByteArray());
    }

    private static string StrokedSheets(int pages, int strokes)
    {
        var b = PdfDocumentBuilder.Create();
        for (var p = 0; p < pages; p++)
        {
            var rnd = new Random(p + 1);
            var page = b.AddPage(new PageSize(2384, 1684));
            for (var i = 0; i < strokes; i++)
            {
                double x = 30 + rnd.NextDouble() * 2324, y = 30 + rnd.NextDouble() * 1624;
                page.DrawLine(x, y, x + (rnd.NextDouble() * 80 - 40), y + (rnd.NextDouble() * 80 - 40), Chuvadi.Pdf.Authoring.Color.FromHex("#000099"), 0.2);
            }
        }

        return TestPdf.WriteTemp(b.ToByteArray());
    }

    [Fact]
    public async Task A_document_whose_drawing_cost_a_lot_gives_memory_back_even_when_its_pages_are_small()
    {
        // A vector CAD sheet keeps a small SVG but costs hundreds of MB to draw (Linux step 47: the relief never ran).
        var path = BlankA1Sheets(2);
        MemoryRelief.ResetForTests();
        MeasuredAsShipped();
        var before = Measure();
        var last = MemoryRelief.Last;

        var session = (ChuvadiPdfSession)await new ChuvadiPdfReader().OpenAsync(path, "sheets.pdf");
        long kept = (await session.RenderPageSvgAsync(0)).Length + (await session.RenderPageSvgAsync(1)).Length;
        var drawing = session.RenderAllocatedBytes;
        session.Dispose();
        await (MemoryRelief.Last ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(30));
        var after = Measure();

        const long mb = 1024 * 1024;
        Assert.True(kept * sizeof(char) < MemoryRelief.HeavyBytes, $"the pages keep {kept * 2 / mb} MB");
        Assert.True(drawing >= MemoryRelief.HeavyDrawingBytes, $"drawing allocated only {drawing / mb} MB");
        Assert.NotSame(last, MemoryRelief.Last);
        Assert.True(after.Committed - before.Committed < 64 * mb, $"memory kept by the runtime after close: {before.Committed / mb} → {after.Committed / mb} MB");
    }

    [Fact]
    public async Task Closing_a_document_while_a_sheet_draws_stops_the_drawing_part_way()
    {
        var path = StrokedSheets(pages: 1, strokes: 12_000);
        var whole = Stopwatch.StartNew();
        using (var full = await new ChuvadiPdfReader().OpenAsync(path, "full.pdf"))
        {
            await full.RenderPageSvgAsync(0);
        }

        whole.Stop();

        var session = (ChuvadiPdfSession)await new ChuvadiPdfReader().OpenAsync(path, "closed.pdf");
        using var started = new ManualResetEventSlim();
        session.BeforeRenderForTests = _ => started.Set();
        var render = session.RenderPageSvgAsync(0);
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        var stop = Stopwatch.StartNew();
        session.Dispose();
        var result = await render.WaitAsync(TimeSpan.FromSeconds(30));
        stop.Stop();

        Assert.Equal(string.Empty, result);
        Assert.True(stop.Elapsed < whole.Elapsed / 2, $"stopped after {stop.ElapsedMilliseconds} ms; the whole sheet takes {whole.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task A_sheet_that_left_the_view_while_drawing_stops_and_the_next_page_starts()
    {
        // Two vector sheets (A1, many strokes: drawing them is the rasteriser's work, as Linux's CAD stand-in).
        // Sheet 1 starts drawing, then leaves the view; sheet 2 is asked for. Stopped, sheet 2 is ready about one
        // sheet's time later; not stopped, it would wait for the rest of sheet 1 first.
        var path = StrokedSheets(pages: 2, strokes: 12_000);
        var whole = new Stopwatch();
        using (var full = await new ChuvadiPdfReader().OpenAsync(path, "full.pdf"))
        {
            await full.RenderPageSvgAsync(0);   // warms up
            whole.Start();
            await full.RenderPageSvgAsync(1);   // one whole sheet, warm
            whole.Stop();
        }

        using var session = (ChuvadiPdfSession)await new ChuvadiPdfReader().OpenAsync(path, "left.pdf");
        using var started = new ManualResetEventSlim();
        session.BeforeRenderForTests = _ => started.Set();
        using var left = new CancellationTokenSource();
        var first = session.RenderPageSvgAsync(0, left.Token);
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        await Task.Delay(whole.Elapsed / 4);   // a quarter of the way into sheet 1
        var stop = Stopwatch.StartNew();
        left.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(30)));
        session.BeforeRenderForTests = null;
        var next = await session.RenderPageSvgAsync(1).WaitAsync(TimeSpan.FromSeconds(60));
        stop.Stop();

        Assert.Contains("<svg", next);
        Assert.False(session.TryGetRenderedSvg(0, out _));   // the stopped sheet isn't kept
        Assert.True(stop.Elapsed < whole.Elapsed * 1.4, $"sheet 2 was ready {stop.ElapsedMilliseconds} ms after sheet 1 left; one whole sheet takes {whole.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Paging_never_runs_a_collection_only_closing_a_heavy_document_does()
    {
        // Part 3 (Linux, 64 MB drawing line): everything paging does (draw, drop a page that left the view, drop the
        // cache of a tab in the background, toggle a layer) leaves the relief alone, however heavy the drawing.
        var session = (ChuvadiPdfSession)await new ChuvadiPdfReader().OpenAsync(BlankA1Sheets(3), "paging.pdf");
        var last = MemoryRelief.Last;

        for (var round = 0; round < 2; round++)
        {
            for (var page = 0; page < 3; page++)
            {
                using var left = new CancellationTokenSource();
                var waiting = session.RenderPageSvgAsync((page + 1) % 3, left.Token);
                await session.RenderPageSvgAsync(page);
                left.Cancel();
                try { await waiting; } catch (OperationCanceledException) { }
                session.SetFocusPage(page);
            }

            session.DropRenderCache();
            session.ResetLayerVisibility();
        }

        Assert.True(session.RenderAllocatedBytes >= MemoryRelief.HeavyDrawingBytes, "the drawing wasn't heavy");
        Assert.Same(last, MemoryRelief.Last);

        session.Dispose();
        Assert.NotSame(last, MemoryRelief.Last);   // closing it is what asks
        await MemoryRelief.Last!.WaitAsync(TimeSpan.FromSeconds(30));
    }

    // ── Eighteenth patch: B-30 (nothing holds a closed document) and B-29 (the next file isn't held up) ─────────

    [Fact]
    public async Task Nothing_holds_a_closed_document_its_pages_or_its_queued_renders()
    {
        // B-30 on 64c9c16: the managed heap stayed at 680 MB after a full collection. Here, the way a tab is used:
        // pages drawn and cached, more queued, one drawing mid-sheet, then the tab closed through TabsService.
        var path = StrokedSheets(pages: 3, strokes: 12_000);
        var before = Measure();

        var (session, document, firstPage) = await OpenUseAndClose(path);
        for (var i = 0; i < 50 && (session.IsAlive || document.IsAlive || firstPage.IsAlive); i++)
        {
            await Task.Delay(100);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        var after = Measure();
        const long mb = 1024 * 1024;
        Assert.False(session.IsAlive, "the closed session is still referenced");
        Assert.False(document.IsAlive, "the closed PdfDocument is still referenced");
        Assert.False(firstPage.IsAlive, "a drawn page of the closed document is still referenced");
        Assert.True(after.Live - before.Live < 16 * mb, $"live memory after close: {before.Live / mb} → {after.Live / mb} MB");
    }

    /// <summary>Kept out of the test method so no local of the test holds what it closes.</summary>
    private static async Task<(WeakReference Session, WeakReference Document, WeakReference FirstPage)> OpenUseAndClose(string path)
    {
        var tabs = new ChuvadiReader.Core.Reader.TabsService(new ChuvadiPdfReader(), new NullStorage());
        var tab = await tabs.OpenAsync(path, Path.GetFileName(path), ChuvadiReader.Core.Documents.DocumentFormat.Pdf);
        var session = (ChuvadiPdfSession)tab.Session!;
        WeakReference document = null!;
        session.TryUseDocument(d => { document = new WeakReference(d); return 0; }, out _);
        var first = await session.RenderPageSvgAsync(0);
        var firstRef = new WeakReference(first);

        using var started = new ManualResetEventSlim();
        session.BeforeRenderForTests = page => { if (page == 1) started.Set(); };
        var drawing = session.RenderPageSvgAsync(1);
        var queued = session.RenderPageSvgAsync(2);
        Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
        await tabs.CloseAsync(tab.Id);             // mid-sheet, with one more queued
        await Task.WhenAll(drawing, queued).WaitAsync(TimeSpan.FromSeconds(30));
        return (new WeakReference(session), document, firstRef);
    }

    [Fact]
    public async Task Closing_mid_sheet_never_holds_up_the_next_file()
    {
        // B-29: after a CAD tab closed mid-sheet, the next file's page 1 sometimes took 3.7–4.6 s.
        var light = TestPdf.TextPage("next file");
        var idle = new Stopwatch();
        using (var warm = await new ChuvadiPdfReader().OpenAsync(light, "warm.pdf"))
        {
            idle.Start();
            await warm.RenderPageSvgAsync(0);
            idle.Stop();
        }

        var heavy = (ChuvadiPdfSession)await new ChuvadiPdfReader().OpenAsync(StrokedSheets(pages: 1, strokes: 12_000), "cad.pdf");
        using var started = new ManualResetEventSlim();
        heavy.BeforeRenderForTests = _ => started.Set();
        var sheet = heavy.RenderPageSvgAsync(0);
        Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
        await Task.Delay(300);                     // as B-29: closed 0.3 s into the sheet
        heavy.Dispose();

        var next = Stopwatch.StartNew();
        using var nextFile = await new ChuvadiPdfReader().OpenAsync(light, "five-pages.pdf");
        await nextFile.RenderPageSvgAsync(0);
        next.Stop();
        var stoppedAt = Stopwatch.StartNew();
        await sheet.WaitAsync(TimeSpan.FromSeconds(30));
        stoppedAt.Stop();

        Assert.True(next.ElapsedMilliseconds < idle.ElapsedMilliseconds + 1000, $"the next file's page took {next.ElapsedMilliseconds} ms (alone: {idle.ElapsedMilliseconds} ms)");
        Assert.True(stoppedAt.ElapsedMilliseconds < 1000, $"the closed sheet was still drawing {stoppedAt.ElapsedMilliseconds} ms after the next file was ready");
    }

    private sealed class NullStorage : ChuvadiReader.Core.Storage.IAppStorage
    {
        private readonly Dictionary<string, string> _d = new();

        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_d.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string value, CancellationToken ct = default) { _d[key] = value; return Task.CompletedTask; }
    }

    // ── Twentieth patch (D-158): the pass waits for what finalizers let go ──────────────────────────────────────

    /// <summary>Stands in for the shared byte pool: drops its big buffer from a finalizer, after a collection.</summary>
    private sealed class DropsOnFinalize
    {
        internal static byte[]? Held;

        ~DropsOnFinalize() => Held = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference HoldUntilFinalized()
    {
        DropsOnFinalize.Held = new byte[96 * 1024 * 1024];
        DropsOnFinalize.Held.AsSpan().Fill(1);
        _ = new DropsOnFinalize();   // garbage at once; its finalizer lets the buffer go
        return new WeakReference(DropsOnFinalize.Held);
    }

    [Fact]
    public void A_relief_pass_also_frees_what_finalizers_release_during_it()
    {
        // The pool trims in a finalizer-thread callback after a full collection. A pass that collects again without
        // waiting for that thread leaves the trimmed buffers as garbage until some later collection (Linux 36c).
        var buffer = HoldUntilFinalized();

        MemoryRelief.Pass();

        Assert.False(buffer.IsAlive, "the buffer a finalizer let go during the pass is still in memory after it");
    }

    private static async Task<long> OpenDrawClose(string path)
    {
        var session = await new ChuvadiPdfReader().OpenAsync(path, Path.GetFileName(path));
        var chars = (await session.RenderPageSvgAsync(0)).Length;
        session.Dispose();
        return chars;
    }
}
