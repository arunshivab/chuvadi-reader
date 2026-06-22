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
using Microsoft.Extensions.DependencyInjection;

namespace ChuvadiReader.Windows;

public partial class App : Application
{
    private SplashWindow? _splash;
    private MainWindow? _main;
    private WpfWindowControls? _windowControls;
    private DispatcherTimer? _fallback;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Chuvadi", "startup-error.log");

    public IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Nothing fails silently any more.
        DispatcherUnhandledException += (_, args) =>
        {
            Report(args.Exception);
            args.Handled = true;
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
            services.AddSingleton<IAppStorage>(storage);
            services.AddSingleton<SaveFolderService>();
            services.AddSingleton<CategoryService>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<UserProfileService>();
            services.AddSingleton<RecentFilesService>();
            services.AddSingleton<PinnedService>();
            services.AddSingleton<WatchedFolderService>();
            services.AddSingleton<LibraryService>();
            services.AddSingleton<TagService>();
            services.AddSingleton<FolderSearchService>();
            services.AddSingleton<DocumentPropertiesService>();
            services.AddSingleton<ExportService>();
            services.AddSingleton<ChuvadiReader.Core.Documents.RedactService>();
            services.AddSingleton<ChuvadiReader.Core.Documents.StampService>();
            services.AddSingleton<PressService>();
            services.AddSingleton<IPdfReader, ChuvadiPdfReader>();
            services.AddSingleton<TabsService>();
            services.AddSingleton<PdfToolsService>();
            services.AddSingleton<BenchComposer>();
            services.AddSingleton<BenchService>();
            services.AddSingleton<OpenDocumentService>();
            services.AddSingleton<RedactRequestService>();
            services.AddSingleton<ChuvadiReader.Core.Licensing.IEntitlements, ChuvadiReader.Core.Licensing.DefaultEntitlements>();
            services.AddSingleton<IFilePicker, WpfFilePicker>();
            services.AddSingleton<IImageClipboard, WpfImageClipboard>();
            services.AddSingleton<IAppReadySignal, AppReadySignal>();

            _windowControls = new WpfWindowControls();
            services.AddSingleton<IWindowControls>(_windowControls);

            Services = services.BuildServiceProvider();

            _main = new MainWindow(Services);
            _windowControls.Attach(_main);
            MainWindow = _main;

            // Match the window background to the saved theme so the pale default
            // doesn't flash before the (dark) WebView content paints on startup.
            if (string.Equals(storage.GetSync("theme"), "Dark", StringComparison.OrdinalIgnoreCase))
            {
                _main.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x11, 0x0B));
            }

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

    private static void Report(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, DateTimeOffset.Now + Environment.NewLine + ex);
        }
        catch
        {
            // logging must never throw
        }

        var webview2 = IsWebView2Problem(ex);
        var message = webview2
            ? "Chuvadi needs the Microsoft Edge WebView2 Runtime, which doesn't seem to be installed.\n\n" +
              "Install the \"Evergreen Standalone Installer\" from:\n" +
              "https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
              "Then relaunch Chuvadi."
            : "Chuvadi hit an error during startup:\n\n" + ex.Message +
              "\n\nFull details were written to:\n" + LogPath;

        MessageBox.Show(message, "Chuvadi — startup", MessageBoxButton.OK,
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
