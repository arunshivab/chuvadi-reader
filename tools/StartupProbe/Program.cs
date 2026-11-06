using System.Diagnostics;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Lifecycle;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Core.Storage;
using ChuvadiReader.Core.Theme;
using ChuvadiReader.Core.Window;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
#if !BEFORE
using ChuvadiReader.Hosting;
using ChuvadiReader.Platform;
#endif

var mainAt = DateTime.UtcNow;
var sw = Stopwatch.StartNew();
var processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
var toMain = (mainAt - processStart).TotalMilliseconds;

var dataDir = Path.Combine(Path.GetTempPath(), "chvd-probe-" + Environment.ProcessId);
var services = new ServiceCollection();
services.AddSingleton<IAppStorage>(new MemoryStorage());
#if BEFORE
// The Windows host's list on chuvadi-reader main c7b9e44 (App.xaml.cs), verbatim order.
services.AddSingleton<SaveFolderService>(); services.AddSingleton<CategoryService>(); services.AddSingleton<ThemeService>();
services.AddSingleton<UserProfileService>(); services.AddSingleton<RecentFilesService>(); services.AddSingleton<PinnedService>();
services.AddSingleton<WatchedFolderService>(); services.AddSingleton<LibraryService>(); services.AddSingleton<TagService>();
services.AddSingleton<FolderSearchService>(); services.AddSingleton<DocumentPropertiesService>(); services.AddSingleton<ExportService>();
services.AddSingleton<RedactService>(); services.AddSingleton<StampService>(); services.AddSingleton<DocxViewService>();
services.AddSingleton<XlsxViewService>(); services.AddSingleton<FlattenService>(); services.AddSingleton<RecoveryService>();
services.AddSingleton<PressService>(); services.AddSingleton<IPdfReader, ChuvadiPdfReader>(); services.AddSingleton<TabsService>();
services.AddSingleton<PdfToolsService>(); services.AddSingleton<BenchComposer>(); services.AddSingleton<BenchService>();
services.AddSingleton<SessionService>(); services.AddSingleton<OpenDocumentService>(); services.AddSingleton<BenchDropService>();
services.AddSingleton<RedactRequestService>(); services.AddSingleton<ShellService>(); services.AddSingleton<IUiScale, NoopUiScale>();
services.AddSingleton<IExternalLinks, NoExternalLinks>(); services.AddSingleton<PreferencesService>(); services.AddSingleton<TextViewService>();
services.AddSingleton<MeasureService>(); services.AddSingleton<SignatureCheckService>(); services.AddSingleton<PasswordService>();
services.AddSingleton<CertificateSignService>(); services.AddSingleton<IPrintService, NoopPrintService>();
services.AddSingleton<ChuvadiReader.Core.Licensing.IEntitlements, ChuvadiReader.Core.Licensing.DefaultEntitlements>();
services.AddSingleton<IAppReadySignal, AppReadySignal>(); services.AddSingleton<IWindowControls, NoopWindowControls>();
#else
services.AddChuvadiReader(AppStateLifetime.Process);
services.AddSingleton<IPlatformInfo>(new FixedPlatformInfo(PlatformKind.Windows, InputKinds.Mouse | InputKinds.Keyboard,
    new ShellChrome(WindowButtons.Right, false, false, true, 0), FixedPlatformInfo.DesktopCapabilities, new CtrlKeyLabels()));
services.AddSingleton<IClipboard, NullClipboard>();
#endif
services.AddSingleton<IFilePicker, NullPicker>();
services.AddSingleton<IImageClipboard, NullImageClipboard>();
services.AddSingleton<IJSRuntime, NullJs>();
services.AddSingleton<NavigationManager, ProbeNav>();
services.AddSingleton<INavigationInterception, NullInterception>();
services.AddSingleton<IScrollToLocationHash, NullScroll>();
services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
var sp = services.BuildServiceProvider();
var built = sw.Elapsed.TotalMilliseconds;

await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);
var html = await renderer.Dispatcher.InvokeAsync(async () =>
{
    var output = await renderer.RenderComponentAsync<ChuvadiReader.Ui.App>();
    await output.QuiescenceTask;
    return output.ToHtmlString();
});
var rendered = sw.Elapsed.TotalMilliseconds;
if (Environment.GetEnvironmentVariable("CHVD_PROBE_HTML") is { Length: > 0 } dump) File.WriteAllText(dump, html);

Console.WriteLine($"{toMain:F1}\t{built:F1}\t{rendered - built:F1}\t{toMain + rendered:F1}\t{html.Length}\t{System.Runtime.JitInfo.GetCompiledMethodCount()}\t{System.Runtime.JitInfo.GetCompilationTime().TotalMilliseconds:F1}\t{AppDomain.CurrentDomain.GetAssemblies().Length}");
try { Directory.Delete(dataDir, true); } catch { }

sealed class MemoryStorage : IAppStorage
{
    private readonly Dictionary<string, string> _d = new();
    public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_d.TryGetValue(key, out var v) ? v : null);
    public Task SetAsync(string key, string value, CancellationToken ct = default) { _d[key] = value; return Task.CompletedTask; }
}
sealed class NullPicker : IFilePicker
{
    public Task<string?> PickDocumentAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<string>> PickDocumentsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    public Task<string?> PickSavePdfAsync(string s, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<string?> PickSaveSessionAsync(string s, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<string?> PickOpenSessionAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<string?> PickSaveImageAsync(string s, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
sealed class NullImageClipboard : IImageClipboard { public Task SetImageAsync(byte[] b, CancellationToken ct = default) => Task.CompletedTask; }
#if !BEFORE
sealed class NullClipboard : IClipboard
{
    public bool CanCopyText => true; public bool CanCopyImage => true;
    public Task SetTextAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetImageAsync(byte[] b, CancellationToken ct = default) => Task.CompletedTask;
}
#endif
sealed class NullJs : IJSRuntime
{
    public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => ValueTask.FromResult(default(T)!);
    public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(T)!);
}
sealed class ProbeNav : NavigationManager { public ProbeNav() => Initialize("https://0.0.0.0/", "https://0.0.0.0/"); }
sealed class NullInterception : INavigationInterception { public Task EnableNavigationInterceptionAsync() => Task.CompletedTask; }
sealed class NullScroll : IScrollToLocationHash { public Task RefreshScrollPositionForHash(string h) => Task.CompletedTask; }
