using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Lifecycle;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Core.Storage;
using ChuvadiReader.Core.Theme;
using ChuvadiReader.Core.Window;
using ChuvadiReader.Windows.Platform;
using ChuvadiReader.Hosting;
using ChuvadiReader.Hosting.Desktop;
using ChuvadiReader.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ChuvadiReader.Windows;

public partial class App : Application
{
    private SplashWindow? _splash;
    private MainWindow? _main;
    private WpfWindowControls? _windowControls;
    private DispatcherTimer? _fallback;
    private DesktopSingleInstance? _instance;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Chuvadi", "startup-error.log");

    public IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance (W-A, F-1): a launch while Chuvadi runs hands its files to the running copy
        // and exits here, before building anything. The first launch pays only for one named mutex.
        _instance = DesktopSingleInstance.Claim(e.Args, beforeHandOff: WpfForeground.AllowAnyProcess);
        if (_instance.Role == InstanceRole.HandedOver)
        {
            Shutdown();
            return;
        }

        // Nothing fails silently any more.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (IsPageGone(args.Exception))
            {
                // BUG-21, BUG-25: a call into a page after its browser process stopped. The window shows its own
                // recovery panel; no error box (there would be one for every call that follows).
                Log(args.Exception);
                _main?.OnPageCallFailed();
                return;
            }

            Report(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Report(ex);
            }
        };

        try
        {
            var services = new ServiceCollection();
            services.AddWpfBlazorWebView();
#if DEBUG
            services.AddBlazorWebViewDeveloperTools();
#endif

            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Chuvadi");

            var storage = new FileAppStorage(appDataDir);

            // Every Core service, registered once for all hosts (ChuvadiReader.Hosting).
            services.AddChuvadiReader(AppStateLifetime.Process);

            // Windows implementations of the platform seams.
            services.AddSingleton<IAppStorage>(storage);
            services.AddSingleton<ChuvadiReader.Core.Window.IUiScale, ChuvadiReader.Windows.Platform.WpfUiScale>();
            services.AddSingleton<ChuvadiReader.Core.Window.IExternalLinks, ChuvadiReader.Windows.Platform.WpfExternalLinks>();
            services.AddSingleton<IPrintService, ChuvadiReader.Windows.Platform.WpfPrintService>();
            services.AddSingleton<IFilePicker, WpfFilePicker>();
            services.AddSingleton<IImageClipboard, WpfImageClipboard>();
            services.AddSingleton<IClipboard, WpfClipboard>();
            services.AddSingleton<IFileManager, WpfFileManager>();
            services.AddSingleton<IAppInstance>(_instance);
            services.AddSingleton<IPlatformInfo>(new FixedPlatformInfo(
                PlatformKind.Windows,
                InputKinds.Mouse | InputKinds.Keyboard | InputKinds.Touch,
                new ShellChrome(ChuvadiReader.Platform.WindowButtons.Right, NativeMenuBar: false, AccountMenu: false, TitleBarDrag: true, CornerRadiusPx: 0),
                FixedPlatformInfo.DesktopCapabilities,
                new CtrlKeyLabels()));

            _windowControls = new WpfWindowControls();
            services.AddSingleton<IWindowControls>(_windowControls);

            Services = services.BuildServiceProvider();
            Services.GetRequiredService<IAppReadySignal>().Ready += () => _started = true;

            _main = new MainWindow(Services);
            _windowControls.Attach(_main);
            MainWindow = _main;

            // Files this copy was started with (Open with, double-click) and files handed over later
            // all go through one router; each opens in its own tab once the Ui has rendered.
            var incoming = Services.GetRequiredService<IncomingFiles>();
            incoming.Delivered += BringToFront;
            incoming.Receive(e.Args);
            var instance = _instance;
            Services.GetRequiredService<IAppReadySignal>().Ready += () => instance.StartListening(RunOnUi);

            // Match the window background to the saved theme so the pale default
            // doesn't flash before the (dark) WebView content paints on startup.
            // Five themes: each gets its surface-mid colour (tokens.css) as the pre-paint background.
            var savedTheme = (storage.GetSync("theme") ?? string.Empty).Trim().ToLowerInvariant();
            _main.Background = savedTheme switch
            {
                "dark" => new SolidColorBrush(Color.FromRgb(0x1B, 0x14, 0x10)),
                "palm" => new SolidColorBrush(Color.FromRgb(0xEB, 0xE5, 0xC9)),
                "indigo" => new SolidColorBrush(Color.FromRgb(0xED, 0xF0, 0xF6)),
                "contrast" => new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF2)),
                _ => _main.Background,
            };

            // Splash is opt-in (Settings → off by default). When off, the window
            // appears immediately and the dashboard fills in as it renders, which
            // feels quicker than waiting on a splash. When on, the native splash
            // covers the boot and hands off on Blazor's first render.
            var splashEnabled = string.Equals(storage.GetSync("splash"), "on", StringComparison.OrdinalIgnoreCase);

            if (splashEnabled)
            {
                Services.GetRequiredService<IAppReadySignal>().Ready += OnAppReady;
                _splash = new SplashWindow();
                _splash.Show();
                _main.Show();

                _fallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                _fallback.Tick += (_, _) => OnAppReady();
                _fallback.Start();
            }
            else
            {
                _main.Show();
            }
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private void OnAppReady()
    {
        Dispatcher.Invoke(() =>
        {
            _fallback?.Stop();
            _fallback = null;

            if (_splash is not null)
            {
                _splash.Close();
                _splash = null;
            }

            if (_main is not null)
            {
                _main.Activate();
                _main.Topmost = true;
                _main.Topmost = false;
                _main.Focus();
            }
        });
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread within <paramref name="wait"/>; when the
    /// UI is busy past that, the action is withdrawn and never runs (the new launch opens its own window).</summary>
    private bool RunOnUi(Action action, TimeSpan wait)
    {
        var op = Dispatcher.InvokeAsync(action);
        if (op.Wait(wait) == DispatcherOperationStatus.Completed)
        {
            return true;
        }

        if (op.Abort())
        {
            return false;
        }

        op.Wait(); // it had just started: let it finish (a few microseconds)
        return op.Status == DispatcherOperationStatus.Completed;
    }

    private void BringToFront()
    {
        if (_main is null || !_main.IsVisible)
        {
            return; // at startup the window shows itself
        }

        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }

        _main.Activate();
        _main.Topmost = true;
        _main.Topmost = false;
        _main.Focus();
    }

    /// <summary>D-190: signing out or shutting down asks about unsaved marks first.</summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel)
        {
            _main?.OnSessionEnding(e);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>True once the app has drawn its first screen: errors after that are not "during startup".</summary>
    private static bool _started;

    /// <summary>Adds the error to the log (kept to about 1 MB; the newest entries are at the end).</summary>
    private static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
            {
                File.Delete(LogPath);
            }

            File.AppendAllText(LogPath, DateTimeOffset.Now + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // logging must never throw
        }
    }

    /// <summary>The page's browser process has stopped: WebView2 says so on every call into it.</summary>
    private static bool IsPageGone(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is InvalidOperationException
                && (ex.TargetSite?.Name == "VerifyBrowserNotCrashed"
                    || (ex.Message?.Contains("browser process crashed", StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }

    private static void Report(Exception ex)
    {
        Log(ex);

        var webview2 = !_started && IsWebView2Problem(ex);
        var message = webview2
            ? "Chuvadi needs the Microsoft Edge WebView2 Runtime, which doesn't seem to be installed.\n\n" +
              "Install the \"Evergreen Standalone Installer\" from:\n" +
              "https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
              "Then relaunch Chuvadi."
            : (_started ? "Chuvadi hit an error:\n\n" : "Chuvadi hit an error during startup:\n\n") + ex.Message +
              "\n\nFull details were written to:\n" + LogPath;

        MessageBox.Show(message, _started ? "Chuvadi" : "Chuvadi — startup", MessageBoxButton.OK,
            webview2 ? MessageBoxImage.Warning : MessageBoxImage.Error);
    }

    private static bool IsWebView2Problem(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex.GetType().Name.Contains("WebView2", StringComparison.OrdinalIgnoreCase) ||
                (ex.Message?.Contains("WebView2", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }
}
