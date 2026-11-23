using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Web.WebView2.Core;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Windows.Platform;

namespace ChuvadiReader.Windows;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;

        ShowNewWebView();
        StateChanged += OnStateChanged;

        // Drop a PDF (or doc) anywhere on the window to open it in the reader.
        AllowDrop = true;
        DragOver += OnFileDragOver;
        Drop += OnFileDrop;
    }

    // ── D-190: ask before the window closes with unsaved marks ─────────────────────────────────
    private bool _closeConfirmed;
    private bool _closeAsking;

    /// <summary>The close button, Alt F4 and the in-app close all arrive here. With nothing unsaved the close
    /// goes on at once, exactly as before. Otherwise it is held, the Ui asks about each file, and the window
    /// closes only when the person has answered for all of them.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeConfirmed)
        {
            return;
        }

        if (_services.GetService(typeof(ChuvadiReader.Platform.IUnsavedWork)) is not ChuvadiReader.Platform.IUnsavedWork work
            || !work.HasUnsavedWork)
        {
            return;
        }

        e.Cancel = true;
        // Out of the Closing event first: the answer may close the window, which can't happen inside it.
        Dispatcher.BeginInvoke(() => AskThenClose(work));
    }

    /// <summary>Signing out or shutting down with unsaved marks: Windows is told to wait and the question is
    /// shown (Windows then says this app is holding the shutdown, as it does for any app with unsaved work).</summary>
    internal void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (_closeConfirmed
            || _services.GetService(typeof(ChuvadiReader.Platform.IUnsavedWork)) is not ChuvadiReader.Platform.IUnsavedWork work
            || !work.HasUnsavedWork)
        {
            return;
        }

        e.Cancel = true;
        AskThenClose(work);
    }

    private async void AskThenClose(ChuvadiReader.Platform.IUnsavedWork work)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        if (_closeAsking)
        {
            return; // the question is already on screen
        }

        if (_webLost)
        {
            // BUG-21, BUG-25: the page that would ask has stopped. Ask here instead; never wait on it.
            AskWithoutPage(work);
            return;
        }

        _closeAsking = true;
        bool go;
        try
        {
            var asked = work.ConfirmCloseAsync();
            var lost = _webLostSignal.Task;
            if (await Task.WhenAny(asked, lost) != asked)
            {
                // The page stopped while its question was on screen: it will never answer.
                _closeAsking = false;
                AskWithoutPage(work);
                return;
            }

            go = await asked;
        }
        catch (Exception)
        {
            // The question could not be shown. Never trap the person in the app: the next close goes through.
            _closeConfirmed = true;
            return;
        }
        finally
        {
            _closeAsking = false;
        }

        if (go)
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>Closing while the page has stopped, with unsaved work: the page can't show its question, so a
    /// plain Windows one asks instead. Reopening the window brings everything back, so it is the first choice.</summary>
    private void AskWithoutPage(ChuvadiReader.Platform.IUnsavedWork work)
    {
        if (!work.HasUnsavedWork)
        {
            _closeConfirmed = true;
            Close();
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Chuvadi's window has stopped working, so it can't show you the files with unsaved changes.\n\n" +
            "Your changes are still kept. Reopen the window to save them?\n\n" +
            "Yes: reopen the window, then save.\n" +
            "No: close Chuvadi now and lose the unsaved changes.\n" +
            "Cancel: leave things as they are.",
            "Chuvadi",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        switch (answer)
        {
            case MessageBoxResult.Yes:
                ReopenWebView();
                break;
            case MessageBoxResult.No:
                _closeConfirmed = true;
                Close();
                break;
        }
    }

    // Same list as the Open dialog (Core: DocumentFormatExtensions.OpenableExtensions).
    private static bool IsSupportedFile(string path)
        => ChuvadiReader.Core.Documents.DocumentFormatExtensions.IsOpenable(path);

    private static bool HasSupportedFile(DragEventArgs e)
        => e.Data.GetDataPresent(DataFormats.FileDrop)
           && e.Data.GetData(DataFormats.FileDrop) is string[] files
           && files.Any(IsSupportedFile);

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasSupportedFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            OpenDropped(files);
        }
    }

    // W-02: a drop on the page (almost the whole window) arrives from the page itself: host-drop.js hands the
    // dropped File objects over with postMessageWithAdditionalObjects, and WebView2 gives the host their paths.
    // (WebView2's native window covers the WPF window, so the window's own Drop only fires on its edges; the
    // earlier fix, turning WebView2's drop off, left drops on the page doing nothing.)
    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.WebMessageAsJson != "\"chvd:files-dropped\"" || e.AdditionalObjects is not { Count: > 0 } objects)
        {
            return;
        }

        var paths = objects.OfType<Microsoft.Web.WebView2.Core.CoreWebView2File>()
            .Select(f => f.Path)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();
        OpenDropped(paths);
    }

    private void OpenDropped(IReadOnlyList<string> files)
    {
        var supported = files.Where(IsSupportedFile).ToArray();
        if (supported.Length == 0)
        {
            return;
        }

        // The shared router (ChuvadiReader.Hosting.IncomingFiles) sends the files to the Bench shelf
        // when the Bench is open, otherwise each to its own Reader tab (approved 7 Nov: every
        // dropped file opens, not just the first).
        if (_services.GetService(typeof(ChuvadiReader.Hosting.IncomingFiles)) is ChuvadiReader.Hosting.IncomingFiles incoming)
        {
            incoming.Receive(supported);
            Activate();
        }
    }

    private void OnBlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    {
        // Stable per-build user-data folder: WebView2 reuses one profile across
        // launches of the same build (no first-run setup, so no white flash), and a
        // rebuild lands on a fresh folder so edited CSS/JS can't be served stale.
        e.UserDataFolder = WebViewCache.ResolveAndPrune();

        // HTTP cache stays ON for fast startup. The build-keyed folder above means a
        // new build always starts from an empty cache, so there's nothing stale to
        // serve — we no longer need --disable-http-cache (which forced a full asset
        // reload, and the latency of that was the start-up delay).
    }

    private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        var core = e.WebView.CoreWebView2;
        var settings = core.Settings;
        // Each on its own: an older WebView2 runtime may lack one, and the rest must still be set.
        TrySet(() => settings.IsZoomControlEnabled = false); // no Ctrl+scroll / Ctrl +/- zoom
        TrySet(() => settings.IsPinchZoomEnabled = false);   // no trackpad / touch pinch zoom
        // BUG-21: the browser's own keys (F5 and Ctrl R reload, Ctrl P, Ctrl F, F3, F12, Back and Forward) are
        // off. The page still sees every key, so Chuvadi's own shortcuts work as before.
        TrySet(() => settings.AreBrowserAcceleratorKeysEnabled = false);
        // A two-finger swipe on a touch screen or trackpad would go "back" and leave the app the same way.
        TrySet(() => settings.IsSwipeNavigationEnabled = false);

        // BUG-21, BUG-25: the page's browser process can stop (out of memory, a crash). Without this the window
        // stays dead and can't be closed.
        core.ProcessFailed += OnWebProcessFailed;

        // W-02: dropped files come from the page (host-drop.js); WebView2 keeps its own drop handling on.
        core.WebMessageReceived += OnWebMessageReceived;

        // Interface size (Settings) zooms the whole WebView; attach now that it exists.
        if (_services.GetService(typeof(ChuvadiReader.Core.Window.IUiScale)) is ChuvadiReader.Windows.Platform.WpfUiScale scale)
        {
            scale.Attach(e.WebView, e.WebView.Dispatcher);
        }
    }

    private static void TrySet(Action set)
    {
        try
        {
            set();
        }
        catch
        {
            // An older WebView2 runtime without this setting: non-fatal.
        }
    }

    // ── BUG-21, BUG-25: the page's browser process stops ──────────────────────────────────────────
    // Everything the person has open or changed (tabs, marks, bookmark edits, redaction areas, Arrange's
    // desks, Recents) lives in the app's own services, not in the page. So a new page shows it all again,
    // exactly as a reload does on the web portal. Only something half-typed into a box at that moment is lost.

    private bool _webLost;
    private TaskCompletionSource _webLostSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<DateTime> _losses = new();

    /// <summary>Creates a page, puts it in the window and starts it. Its services are the app's own, so it
    /// shows the same tabs and work as the page before it.</summary>
    private void ShowNewWebView()
    {
        var web = new ChuvadiWebView { HostPage = "wwwroot/index.html", Services = _services };
        web.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(ChuvadiReader.Ui.App),
        });
        web.BlazorWebViewInitializing += OnBlazorWebViewInitializing;
        web.BlazorWebViewInitialized += OnBlazorWebViewInitialized;

        _webLost = false;
        _webLostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WebHost.Content = web;
        WebHost.Visibility = Visibility.Visible;
        Recovery.Visibility = Visibility.Collapsed;
    }

    private void OnWebProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                OnWebViewLost(unresponsive: false);
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                OnWebViewLost(unresponsive: true);
                break;
            default:
                // GPU, utility and frame processes: WebView2 starts them again itself and the page carries on.
                break;
        }
    }

    /// <summary>The page has stopped, or stopped responding. Also called by the app when a call into the page
    /// fails because its browser process is gone. Safe to call more than once.</summary>
    internal void OnWebViewLost(bool unresponsive = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnWebViewLost(unresponsive));
            return;
        }

        if (_webLost)
        {
            return;
        }

        _webLost = true;
        _webLostSignal.TrySetResult();
        _losses.Add(DateTime.UtcNow);
        _losses.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(10));

        if (!unresponsive)
        {
            DiscardWebView(); // a stopped page keeps using the processor while it stays (PF16)
        }

        ShowRecovery(unresponsive);
    }

    /// <summary>A call into a page failed because its browser process is gone. It may have been the page before
    /// this one, still being let go of; so the page now on screen is checked first, and only a dead one is
    /// replaced.</summary>
    internal void OnPageCallFailed()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnPageCallFailed);
            return;
        }

        if (_webLost || WebHost.Content is not ChuvadiWebView current)
        {
            return;
        }

        try
        {
            _ = current.WebView.CoreWebView2?.BrowserProcessId; // throws once its browser process has stopped
        }
        catch (InvalidOperationException)
        {
            OnWebViewLost();
        }
    }

    private void ShowRecovery(bool unresponsive)
    {
        // The page is a native window: nothing WPF can draw over it, so it is hidden while this shows.
        WebHost.Visibility = Visibility.Collapsed;

        bool dark = Background is SolidColorBrush b && (0.299 * b.Color.R + 0.587 * b.Color.G + 0.114 * b.Color.B) < 128;
        Recovery.Background = Background;
        var ink = new SolidColorBrush(dark ? Color.FromRgb(0xF2, 0xEA, 0xD6) : Color.FromRgb(0x2B, 0x21, 0x1A));
        RecoveryTitle.Foreground = ink;
        RecoveryBody.Foreground = ink;

        RecoveryTitle.Text = unresponsive ? "Chuvadi's window isn't responding" : "Chuvadi's window stopped working";
        var body = unresponsive
            ? "It may be busy with a very large file. You can keep waiting, or reopen the window now. "
            : "Its display stopped, often because a file needed more memory than it had. ";
        body += "Your open files and unsaved changes are kept: reopening the window shows them again, starting at Home. "
                + "Only something you were typing into a box at that moment may be lost.";
        if (_losses.Count >= 2)
        {
            body += "\n\nThis has happened " + _losses.Count + " times in the last few minutes. If it happens again when you go back "
                    + "to the same file or to Arrange, close that file first.";
        }

        RecoveryBody.Text = body;
        RecoveryWait.Visibility = unresponsive ? Visibility.Visible : Visibility.Collapsed;
        Recovery.Visibility = Visibility.Visible;
        Activate();
        RecoveryReopen.Focus();
    }

    private void OnRecoveryReopen(object sender, RoutedEventArgs e) => ReopenWebView();

    /// <summary>Not responding, and the person chose to wait: the page comes back as it was.</summary>
    private void OnRecoveryWait(object sender, RoutedEventArgs e)
    {
        _webLost = false;
        _webLostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Recovery.Visibility = Visibility.Collapsed;
        WebHost.Visibility = Visibility.Visible;
    }

    private void OnRecoveryClose(object sender, RoutedEventArgs e) => Close();

    private void ReopenWebView()
    {
        DiscardWebView();
        ShowNewWebView();
    }

    /// <summary>Takes the current page out of the window and lets it go. Disposing a page whose browser has
    /// stopped can fail; that is ignored, because a new page doesn't depend on it.</summary>
    private void DiscardWebView()
    {
        if (WebHost.Content is not ChuvadiWebView old)
        {
            return;
        }

        WebHost.Content = null;
        try
        {
            old.WebView.CoreWebView2.ProcessFailed -= OnWebProcessFailed;
            old.WebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        catch
        {
            // Its browser is already gone.
        }

        try
        {
            _ = old.DisposeAsync().AsTask().ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
        }
        catch
        {
            // As above.
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // A custom-chrome window clips its content by the resize-border thickness
        // when maximized, which hides the top of the title bar. Compensate with a
        // matching margin so the whole title bar stays visible.
        Root.Margin = WindowState == WindowState.Maximized
            ? new Thickness(
                SystemParameters.WindowResizeBorderThickness.Left,
                SystemParameters.WindowResizeBorderThickness.Top,
                SystemParameters.WindowResizeBorderThickness.Right,
                SystemParameters.WindowResizeBorderThickness.Bottom)
            : new Thickness(0);
    }

    // A borderless / custom-chrome window maximizes to the full monitor, covering
    // the taskbar, unless we clamp the maximized bounds to the monitor work area.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);

        // Maximize only after the hook is installed, so the first maximize is
        // clamped to the work area instead of spilling under the taskbar.
        WindowState = WindowState.Maximized;
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            const int MONITOR_DEFAULTTONEAREST = 0x00000002;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var work = info.rcWork;
                    var mon = info.rcMonitor;
                    mmi.ptMaxPosition.X = Math.Abs(work.Left - mon.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(work.Top - mon.Top);
                    mmi.ptMaxSize.X = Math.Abs(work.Right - work.Left);
                    mmi.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
