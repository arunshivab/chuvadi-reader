using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Graphics;
using ChuvadiReader.Core.Reader;
using Xunit;
using Path = System.IO.Path;

namespace ChuvadiReader.Tests;

/// <summary>
/// D-152 (Android Test AT-13): pages render on a worker thread, one at a time per document, so a heavy page never
/// holds up the Ui or the next open. Stall tests, like B-24's: the hook holds a render open; on the old code, which
/// rendered on the caller's thread, each of these would hang the caller (they fail on the 5 s guard instead).
/// </summary>
public sealed class RenderOffUiThreadTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private static string ThreePages() => Pages(3);

    private static string Pages(int count)
    {
        var b = PdfDocumentBuilder.Create();
        for (var i = 1; i <= count; i++)
        {
            b.AddPage(new PageSize(200, 200)).DrawText("Page " + i, 20, 90, "Helvetica", 18, Color.FromHex("#000000"));
        }

        return TestPdf.WriteTemp(b.ToByteArray());
    }

    private static async Task<ChuvadiPdfSession> Open(string path) =>
        (ChuvadiPdfSession)await new ChuvadiPdfReader().OpenAsync(path, Path.GetFileName(path));

    /// <summary>Starts a call on another thread, so a call that blocks fails the guard instead of hanging the test.</summary>
    private static Task<T> Call<T>(Func<T> call) => Task.Run(call).WaitAsync(Guard);

    [Fact]
    public async Task A_render_runs_off_the_callers_thread_and_the_caller_is_not_held()
    {
        using var session = await Open(ThreePages());
        using var release = new ManualResetEventSlim();
        session.BeforeRenderForTests = _ => release.Wait(Guard);

        // On the old code this call drew the page before returning, so it hung here (the guard fails it).
        var render = await Call(() => session.RenderPageSvgAsync(0));

        Assert.False(render.IsCompleted);            // the caller got its task back while the page is still rendering
        Assert.False(session.TryGetRenderedSvg(0, out _));
        release.Set();
        var svg = await render.WaitAsync(Guard);

        Assert.Contains("<svg", svg);
        Assert.True(session.TryGetRenderedSvg(0, out var cached));
        Assert.Equal(svg, cached);
    }

    [Fact]
    public async Task One_document_renders_one_page_at_a_time_in_the_order_asked()
    {
        using var session = await Open(ThreePages());
        int running = 0, most = 0;
        var order = new List<int>();
        session.BeforeRenderForTests = page =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            lock (order) order.Add(page);
            Thread.Sleep(80);
            Interlocked.Decrement(ref running);
        };

        var renders = new[] { session.RenderPageSvgAsync(2), session.RenderPageSvgAsync(0), session.RenderPageSvgAsync(1), session.RenderPageSvgAsync(2) };
        await Task.WhenAll(renders).WaitAsync(Guard);

        Assert.Equal(1, most);                       // never two renders on one document at once
        Assert.Equal([2, 0, 1], order);              // in the order asked; the repeat of page 2 came from the cache
        Assert.All(renders, r => Assert.Contains("<svg", r.Result));
    }

    [Fact]
    public async Task Two_documents_render_at_the_same_time()
    {
        using var a = await Open(ThreePages());
        using var b = await Open(ThreePages());
        using var both = new CountdownEvent(2);
        void Meet(int _) { both.Signal(); Assert.True(both.Wait(Guard), "the other document's render never started"); }
        a.BeforeRenderForTests = Meet;
        b.BeforeRenderForTests = Meet;

        await Task.WhenAll(a.RenderPageSvgAsync(0), b.RenderPageSvgAsync(0)).WaitAsync(Guard);
    }

    [Fact]
    public async Task Closing_while_a_page_renders_never_waits_and_the_document_closes_when_the_render_ends()
    {
        var session = await Open(ThreePages());
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        session.BeforeRenderForTests = _ => { started.Set(); release.Wait(Guard); };

        var render = session.RenderPageSvgAsync(0);
        Assert.True(started.Wait(Guard));
        await Call(() => { session.Dispose(); return true; }); // a tab closed mid-render: returns at once

        Assert.False(session.TryUseDocument(_ => true, out _)); // nothing new can start
        release.Set();
        await render.WaitAsync(Guard);                           // the render ends cleanly

        Assert.False(session.TryGetRenderedSvg(0, out _));       // and isn't kept for a closed document
        Assert.Equal(string.Empty, await session.RenderPageSvgAsync(1).WaitAsync(Guard));
    }

    [Fact]
    public async Task Measuring_never_waits_for_a_page_that_is_rendering()
    {
        using var session = await Open(ThreePages());
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        session.BeforeRenderForTests = _ => { started.Set(); release.Wait(Guard); };
        var measure = new MeasureService();

        var render = session.RenderPageSvgAsync(0);
        Assert.True(started.Wait(Guard));
        var during = await Call(() => measure.Measure(session, 0, new PagePoint(0, 0), new PagePoint(72, 0)));
        release.Set();
        await render.WaitAsync(Guard);
        var after = measure.Measure(session, 0, new PagePoint(0, 0), new PagePoint(72, 0));

        Assert.Null(during);                 // skipped, not waited for: the next pointer move measures again
        Assert.NotNull(after);
    }

    [Fact]
    public async Task A_page_rendered_for_the_old_layers_is_not_kept_after_a_layer_change()
    {
        using var session = await Open(LayersMeasureSignatureTests.LayeredPdf());
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        session.BeforeRenderForTests = _ => { started.Set(); release.Wait(Guard); };

        var render = session.RenderPageSvgAsync(0);
        Assert.True(started.Wait(Guard));
        session.SetLayerVisible(LayersMeasureSignatureTests.LayerId(session, "Green layer"), true); // hidden by default; shown while the old view renders
        release.Set();
        await render.WaitAsync(Guard);

        Assert.False(session.TryGetRenderedSvg(0, out _)); // the stale page is dropped; the Reader asks again
        session.BeforeRenderForTests = null;
        var fresh = await session.RenderPageSvgAsync(0).WaitAsync(Guard);
        Assert.True(session.TryGetRenderedSvg(0, out var cached));
        Assert.Equal(fresh, cached);
    }

    // ── No wait cycle with a Ui dispatcher (coordinator 9 Nov 22:28) ─────────────────────────────────────────────

    /// <summary>One thread that runs posted work in order, as the WPF, Mac Catalyst, GTK and Android Ui threads do.</summary>
    private sealed class UiThread : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _work = new();
        private readonly Thread _thread;

        public UiThread()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _work.GetConsumingEnumerable())
                {
                    callback(state);
                }
            }) { IsBackground = true, Name = "test Ui thread" };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => _work.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("the Ui thread is never blocked on");

        public Task Run(Func<Task> work)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try { await work(); done.SetResult(); }
                catch (Exception e) { done.SetException(e); }
            }, null);
            return done.Task;
        }

        public void Dispose() => _work.CompleteAdding();
    }

    [Fact]
    public async Task The_Ui_thread_keeps_working_while_pages_render_and_every_render_comes_back_to_it()
    {
        // What the Reader does on its dispatcher: ask for pages, keep answering the person while they render
        // (measure, look up the cache, toggle a layer, drop the cache), and redraw when each render comes back.
        // A wait cycle between the render queue and the Ui thread would hang here; the guard fails it instead.
        using var session = await Open(LayersMeasureSignatureTests.LayeredPdf());
        using var ui = new UiThread();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        session.BeforeRenderForTests = _ => { started.Set(); release.Wait(Guard); };
        var measure = new MeasureService();
        var backOnUi = 0;
        Task<string>? first = null, second = null;
        try
        {
            await ui.Run(async () =>
            {
                first = session.RenderPageSvgAsync(0);
                second = session.RenderPageSvgAsync(0);           // the same page asked again while it renders
                Assert.True(await Task.Run(() => started.Wait(Guard)));  // the worker has the page (the Ui thread awaits, never blocks)
                Assert.Null(measure.Measure(session, 0, new PagePoint(0, 0), new PagePoint(72, 0)));
                Assert.False(session.TryGetRenderedSvg(0, out _));
                session.SetLayerVisible(LayersMeasureSignatureTests.LayerId(session, "Green layer"), true);
                session.DropRenderCache();
                await Task.Yield();                                 // the Ui thread goes back to its queue
            }).WaitAsync(Guard);

            release.Set();
            await ui.Run(async () =>
            {
                await first!;
                Interlocked.Increment(ref backOnUi);                // continuations come back to the Ui thread
                Assert.Equal("test Ui thread", Thread.CurrentThread.Name);
                await second!;
                Interlocked.Increment(ref backOnUi);
                Assert.NotNull(measure.Measure(session, 0, new PagePoint(0, 0), new PagePoint(72, 0)));
            }).WaitAsync(Guard);
        }
        finally
        {
            release.Set(); // a failure never leaves a render held
        }

        Assert.Equal(2, backOnUi);
    }

    [Fact]
    public async Task Closing_a_tab_on_the_Ui_thread_mid_render_never_blocks_it()
    {
        var session = await Open(ThreePages());
        using var ui = new UiThread();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        session.BeforeRenderForTests = _ => { started.Set(); release.Wait(Guard); };
        Task<string>? render = null;

        try
        {
            await ui.Run(() => { render = session.RenderPageSvgAsync(1); return Task.CompletedTask; }).WaitAsync(Guard);
            Assert.True(started.Wait(Guard));
            await ui.Run(() => { session.Dispose(); return Task.CompletedTask; }).WaitAsync(Guard); // returns while the render holds the document
        }
        finally
        {
            release.Set();
        }

        await ui.Run(async () => await render!).WaitAsync(Guard);
    }

    // ── Fast paging (fifteenth patch, Linux on the fourteenth) ───────────────────────────────────────────────────

    [Fact]
    public async Task After_150_fast_page_downs_the_page_you_land_on_is_drawn_next_not_after_the_backlog()
    {
        // What the Reader does while PageDown is held: each page entering the view is asked for, and each page that
        // leaves it is cancelled. Every render takes 40 ms here; the old queue drew all 150 in order (6 s) before the
        // page landed on. Now the ones that left are dropped and the page in view goes first.
        using var session = await Open(Pages(150));
        var drawn = new List<int>();
        session.BeforeRenderForTests = page => { lock (drawn) drawn.Add(page); Thread.Sleep(40); };
        var asked = new Dictionary<int, (CancellationTokenSource Cts, Task<string> Render)>();

        for (var page = 0; page < 150; page++)
        {
            var cts = new CancellationTokenSource();
            asked[page] = (cts, session.RenderPageSvgAsync(page, cts.Token));
            if (page >= 2)
            {
                asked[page - 2].Cts.Cancel();                // two pages back has scrolled out of the render zone
            }

            session.SetFocusPage(page);
        }

        var landed = await asked[149].Render.WaitAsync(Guard);
        Assert.Contains("<svg", landed);
        int drawnBeforeLanding;
        lock (drawn) drawnBeforeLanding = drawn.IndexOf(149);

        Assert.InRange(drawnBeforeLanding, 0, 2);            // at most the one already drawing and its neighbour first
        await Task.Delay(200);
        lock (drawn) Assert.InRange(drawn.Count, 1, 4);      // nothing else of the backlog was drawn
        foreach (var page in Enumerable.Range(0, 148))
        {
            var r = asked[page].Render;
            Assert.True(r.IsCanceled || r.IsCompletedSuccessfully, $"page {page} is still waiting");
        }
    }

    [Fact]
    public async Task Waiting_pages_are_drawn_nearest_the_page_in_view_first()
    {
        using var session = await Open(Pages(9));
        using var release = new ManualResetEventSlim();
        var drawn = new List<int>();
        session.BeforeRenderForTests = page => { lock (drawn) drawn.Add(page); if (page == 0) release.Wait(Guard); };

        var first = session.RenderPageSvgAsync(0);           // drawing while the others queue
        await WaitUntil(() => { lock (drawn) return drawn.Count == 1; });
        var rest = new[] { 1, 2, 3, 4, 5, 6, 7, 8 }.Select(p => session.RenderPageSvgAsync(p)).ToList();
        session.SetFocusPage(6);
        release.Set();
        await Task.WhenAll(rest.Append(first)).WaitAsync(Guard);

        Assert.Equal([0, 6, 5, 7, 4, 8, 3, 2, 1], drawn);    // nearest page 6 first; equal distance: the one asked first
    }

    [Fact]
    public async Task A_cancelled_render_that_never_started_draws_nothing_and_reports_cancelled()
    {
        using var session = await Open(ThreePages());
        using var release = new ManualResetEventSlim();
        var drawn = new List<int>();
        session.BeforeRenderForTests = page => { lock (drawn) drawn.Add(page); if (page == 0) release.Wait(Guard); };

        var first = session.RenderPageSvgAsync(0);
        await WaitUntil(() => { lock (drawn) return drawn.Count == 1; });
        using var cts = new CancellationTokenSource();
        var left = session.RenderPageSvgAsync(1, cts.Token);
        cts.Cancel();
        release.Set();
        await first.WaitAsync(Guard);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => left.WaitAsync(Guard));
        await Task.Delay(100);
        lock (drawn) Assert.Equal([0], drawn);
        Assert.False(session.TryGetRenderedSvg(1, out _));
        Assert.Contains("<svg", await session.RenderPageSvgAsync(1).WaitAsync(Guard)); // asked again when it comes back
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Guard;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "timed out");
            await Task.Delay(5);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
