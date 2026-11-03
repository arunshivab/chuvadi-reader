using System;
using System.Collections.Generic;
using System.IO;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Documents.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Xps;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering;
using PdfRenderOptions = Chuvadi.Pdf.Rendering.RenderOptions;
using ChuvadiReader.Core.Reader;

namespace ChuvadiReader.Windows.Platform;

/// <summary>
/// Prints through the native Windows print dialog. Pages are rasterised by chuvadi-pdf's
/// <see cref="PageRasterizer"/> at the printer's resolution (clamped to 150–300 dpi to bound
/// memory) and laid out one <see cref="FixedPage"/> per PDF page, scaled to the printable area
/// (or at actual size), centred, and turned 90° when the page's orientation doesn't match the
/// paper's. Pages are produced lazily by a <see cref="DocumentPaginator"/>, one rendered ahead
/// on a background thread, so a long document never holds more than two page bitmaps, and the
/// job is spooled with <see cref="XpsDocumentWriter.WriteAsync(DocumentPaginator, PrintTicket)"/>,
/// which <see cref="XpsDocumentWriter.CancelAsync"/> stops between pages. Layers print as the
/// file sets them. BCL/WPF only — no extra packages.
/// </summary>
public sealed class WpfPrintService : IPrintService
{
    private const double MinDpi = 150;
    private const double MaxDpi = 300;
    private const double DipPerPoint = 96.0 / 72.0;

    public bool IsAvailable => Application.Current?.Dispatcher is not null;

    public async Task<PrintResult> PrintAsync(string pdfPath, PrintRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return new PrintResult(PrintOutcome.Unavailable, 0, "Printing isn't available on this platform");
        }

        PdfDocument? doc = null;
        try
        {
            doc = await Task.Run(
                () => string.IsNullOrEmpty(request.Password) ? PdfDocument.Open(pdfPath) : PdfDocument.Open(pdfPath, request.Password),
                ct).ConfigureAwait(false);

            if (!PageRangeParser.TryParse(request.PageRange, doc.PageCount, request.CurrentPage, out var pages, out var rangeError))
            {
                return new PrintResult(PrintOutcome.Failed, 0, rangeError);
            }

            ct.ThrowIfCancellationRequested();
            string jobName = string.IsNullOrWhiteSpace(request.JobName) ? Path.GetFileName(pdfPath) : request.JobName!;

            var job = await dispatcher.InvokeAsync(() => ShowDialog(request)).Task.ConfigureAwait(false);
            if (job is null)
            {
                return new PrintResult(PrintOutcome.Cancelled, 0, "Printing cancelled");
            }

            ct.ThrowIfCancellationRequested();
            var openDoc = doc;
            int sent = await (await dispatcher.InvokeAsync(
                    () => SpoolAsync(openDoc, pages, job, request.FitToPage, jobName, dispatcher, ct)).Task.ConfigureAwait(false))
                .ConfigureAwait(false);

            return new PrintResult(PrintOutcome.Printed, sent,
                sent == 1 ? "Sent 1 page to the printer" : $"Sent {sent} pages to the printer");
        }
        catch (OperationCanceledException)
        {
            return new PrintResult(PrintOutcome.Cancelled, 0, "Printing cancelled");
        }
        catch (Exception ex)
        {
            return new PrintResult(PrintOutcome.Failed, 0, "Couldn't print: " + ex.Message);
        }
        finally
        {
            doc?.Dispose();
        }
    }

    // ── dialog ───────────────────────────────────────────────────────────────

    private sealed record PrintJobSetup(PrintQueue Queue, PrintTicket Ticket, Size Paper, Rect Imageable, double Dpi);

    /// <summary>Runs on the UI thread. Returns null when the user cancels.</summary>
    private static PrintJobSetup? ShowDialog(PrintRequest request)
    {
        var dlg = new PrintDialog { UserPageRangeEnabled = false, CurrentPageEnabled = false, SelectedPagesEnabled = false };
        if (request.Copies is > 0 && dlg.PrintTicket is not null)
        {
            dlg.PrintTicket.CopyCount = request.Copies.Value;
        }

        // PrintDialog parents itself to the active window.
        bool? ok = dlg.ShowDialog();
        if (ok != true || dlg.PrintQueue is null)
        {
            return null;
        }

        var ticket = dlg.PrintTicket ?? dlg.PrintQueue.DefaultPrintTicket ?? new PrintTicket();

        // PrintableAreaWidth/Height are the media size in DIPs, already swapped for landscape.
        var paper = new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
        if (paper.Width <= 0 || paper.Height <= 0)
        {
            paper = new Size(8.27 * 96, 11.69 * 96); // A4
        }

        var imageable = new Rect(paper);
        try
        {
            var area = dlg.PrintQueue.GetPrintCapabilities(ticket).PageImageableArea;
            if (area is not null)
            {
                bool landscape = ticket.PageOrientation is PageOrientation.Landscape or PageOrientation.ReverseLandscape;
                var r = landscape
                    ? new Rect(area.OriginHeight, area.OriginWidth, area.ExtentHeight, area.ExtentWidth)
                    : new Rect(area.OriginWidth, area.OriginHeight, area.ExtentWidth, area.ExtentHeight);
                if (r.Width > 0 && r.Height > 0 && r.Right <= paper.Width + 1 && r.Bottom <= paper.Height + 1)
                {
                    imageable = r;
                }
            }
        }
        catch (PrintQueueException)
        {
            // Some drivers can't report capabilities; print to the full sheet.
        }

        double dpi = ticket.PageResolution?.X is int x && x > 0 ? x : MaxDpi;
        dpi = Math.Clamp(dpi, MinDpi, MaxDpi);
        return new PrintJobSetup(dlg.PrintQueue, ticket, paper, imageable, dpi);
    }

    // ── spooling ─────────────────────────────────────────────────────────────

    /// <summary>Runs on the UI thread (XpsDocumentWriter is dispatcher-affine). Completes with the
    /// number of pages written, or throws <see cref="OperationCanceledException"/>.</summary>
    private static Task<int> SpoolAsync(
        PdfDocument doc,
        IReadOnlyList<int> pages,
        PrintJobSetup job,
        bool fitToPage,
        string jobName,
        Dispatcher dispatcher,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var paginator = new RasterPaginator(doc, pages, job.Paper, job.Imageable, job.Dpi, fitToPage, ct);
        var writer = PrintQueue.CreateXpsDocumentWriter(job.Queue);
        try
        {
            job.Queue.CurrentJobSettings.Description = jobName; // the name shown in the print queue
        }
        catch (PrintQueueException)
        {
            // Cosmetic only.
        }

        CancellationTokenRegistration reg = default;
        bool finished = false;
        void Finish(Action complete)
        {
            if (finished) return;
            finished = true;
            reg.Dispose();
            // Let any render-ahead finish before the caller disposes the document.
            _ = paginator.DrainAsync().ContinueWith(_ => complete(), TaskScheduler.Default);
        }

        writer.WritingCompleted += (_, e) =>
        {
            if (e.Cancelled || ct.IsCancellationRequested)
                Finish(() => tcs.TrySetCanceled(ct.IsCancellationRequested ? ct : CancellationToken.None));
            else if (e.Error is not null)
                Finish(() => tcs.TrySetException(e.Error));
            else
                Finish(() => tcs.TrySetResult(pages.Count));
        };
        writer.WritingCancelled += (_, e) =>
            Finish(() =>
            {
                if (e.Error is not null && !ct.IsCancellationRequested) tcs.TrySetException(e.Error);
                else tcs.TrySetCanceled(ct.IsCancellationRequested ? ct : CancellationToken.None);
            });

        reg = ct.Register(() => dispatcher.BeginInvoke(() =>
        {
            try
            {
                writer.CancelAsync();
            }
            catch (InvalidOperationException)
            {
                // Already finished.
            }
        }));

        try
        {
            writer.WriteAsync(paginator, job.Ticket);
        }
        catch (Exception ex)
        {
            Finish(() => tcs.TrySetException(ex));
        }

        return tcs.Task;
    }

    /// <summary>Produces one <see cref="FixedPage"/> per requested PDF page on demand. The bitmap
    /// for page n+1 is rendered on a background thread while page n spools; rendering is
    /// serialised because one <see cref="PdfDocument"/> isn't safe for concurrent use.</summary>
    private sealed class RasterPaginator : DocumentPaginator
    {
        private readonly PdfDocument _doc;
        private readonly IReadOnlyList<int> _pages;
        private readonly Size _paper;
        private readonly Rect _imageable;
        private readonly double _dpi;
        private readonly bool _fit;
        private readonly CancellationToken _ct;
        private readonly SemaphoreSlim _renderLock = new(1, 1);
        private readonly Dictionary<int, Task<BitmapSource>> _pending = new();

        public RasterPaginator(PdfDocument doc, IReadOnlyList<int> pages, Size paper, Rect imageable, double dpi, bool fit, CancellationToken ct)
        {
            _doc = doc;
            _pages = pages;
            _paper = paper;
            _imageable = imageable;
            _dpi = dpi;
            _fit = fit;
            _ct = ct;
            PageSize = paper;
        }

        public override bool IsPageCountValid => true;

        public override int PageCount => _pages.Count;

        public override Size PageSize { get; set; }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            _ct.ThrowIfCancellationRequested();
            var bitmapTask = Request(pageNumber);
            if (pageNumber + 1 < _pages.Count)
            {
                Request(pageNumber + 1); // render ahead
            }

            // Free bitmaps of pages already spooled.
            foreach (var k in new List<int>(_pending.Keys))
            {
                if (k < pageNumber) _pending.Remove(k);
            }

            var bitmap = bitmapTask.GetAwaiter().GetResult();
            var fixedPage = BuildFixedPage(bitmap);
            var rect = new Rect(_paper);
            return new DocumentPage(fixedPage, _paper, rect, rect);
        }

        private Task<BitmapSource> Request(int pageNumber)
        {
            if (!_pending.TryGetValue(pageNumber, out var task))
            {
                int pdfIndex = _pages[pageNumber];
                task = Task.Run(() => Render(pdfIndex), _ct);
                _pending[pageNumber] = task;
            }

            return task;
        }

        /// <summary>Background thread: rasterise one page upright (its /Rotate applied), turned
        /// a further 90° when its orientation doesn't match the paper's, as a frozen bitmap.</summary>
        private BitmapSource Render(int pdfIndex)
        {
            _renderLock.Wait(_ct);
            try
            {
                var page = _doc.Pages[pdfIndex];
                var (w, h) = page.EffectiveSize;
                bool pageLandscape = w > h;
                bool paperLandscape = _imageable.Width > _imageable.Height;
                int extra = (w != h && pageLandscape != paperLandscape) ? 90 : 0;

                var buffer = new PageRasterizer(_doc.Objects, new PdfRenderOptions
                {
                    Dpi = _dpi,
                    Background = Chuvadi.Pdf.Graphics.ColorF.White,
                    AntiAlias = true,
                    ApplyPageRotation = true,
                    Rotation = extra,
                }).Rasterize(page, _ct);

                var bmp = BitmapSource.Create(
                    buffer.Width, buffer.Height, _dpi, _dpi, PixelFormats.Bgra32, null,
                    buffer.Pixels.ToArray(), buffer.Stride);
                bmp.Freeze();
                return bmp;
            }
            finally
            {
                _renderLock.Release();
            }
        }

        /// <summary>UI thread: one sheet with the page image placed in the printable area.</summary>
        private FixedPage BuildFixedPage(BitmapSource bitmap)
        {
            // The bitmap's natural size in DIPs (its dpi is the render dpi).
            double naturalW = bitmap.PixelWidth * 96.0 / _dpi;
            double naturalH = bitmap.PixelHeight * 96.0 / _dpi;

            double scale = _fit
                ? Math.Min(_imageable.Width / naturalW, _imageable.Height / naturalH)
                : 1.0;
            double drawW = naturalW * scale;
            double drawH = naturalH * scale;

            // Centre in the printable area; at actual size the overflow is clipped to it.
            double left = _imageable.X + ((_imageable.Width - drawW) / 2);
            double top = _imageable.Y + ((_imageable.Height - drawH) / 2);

            var image = new Image { Source = bitmap, Width = drawW, Height = drawH, Stretch = Stretch.Fill };
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            FixedPage.SetLeft(image, left);
            FixedPage.SetTop(image, top);

            var fixedPage = new FixedPage
            {
                Width = _paper.Width,
                Height = _paper.Height,
                Background = Brushes.White,
                Clip = new RectangleGeometry(_imageable),
            };
            fixedPage.Children.Add(image);
            var size = new Size(_paper.Width, _paper.Height);
            fixedPage.Measure(size);
            fixedPage.Arrange(new Rect(size));
            fixedPage.UpdateLayout();
            return fixedPage;
        }

        /// <summary>Completes when every started render has finished (successfully or not).</summary>
        public async Task DrainAsync()
        {
            var tasks = new List<Task>(_pending.Values);
            _pending.Clear();
            foreach (var t in tasks)
            {
                try
                {
                    await t.ConfigureAwait(false);
                }
                catch
                {
                    // Cancelled or failed renders are already reported through the writer.
                }
            }
        }
    }

    /// <summary>Points to DIPs (1/96 in), for callers sizing previews.</summary>
    internal static double PointsToDip(double points) => points * DipPerPoint;
}
