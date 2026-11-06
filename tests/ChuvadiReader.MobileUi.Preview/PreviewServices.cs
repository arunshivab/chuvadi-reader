using System.Collections.Concurrent;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Graphics;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Core.Storage;
using ChuvadiReader.Hosting;
using ChuvadiReader.Platform;
using Path = System.IO.Path;

namespace ChuvadiReader.MobileUi.Preview;

/// <summary>Platform facts for the preview: Android phone / tablet, Windows desktop, or the Web portal
/// (<paramref name="web"/>: server-side, no folders, account menu — for checking portal wording).</summary>
public sealed class PreviewPlatform(FormFactor formFactor, bool web = false) : IPlatformInfo
{
    public PlatformKind Kind => web ? PlatformKind.Web
        : formFactor == FormFactor.Desktop ? PlatformKind.Windows : PlatformKind.Android;

    public FormFactor FormFactor => formFactor;

    public event Action? FormFactorChanged { add { } remove { } }

    public InputKinds Input => formFactor == FormFactor.Desktop
        ? InputKinds.Mouse | InputKinds.Keyboard
        : InputKinds.Touch;

    public ShellChrome Chrome => web ? new ShellChrome(WindowButtons.None, false, true, false, 0)
        : formFactor == FormFactor.Desktop
        ? new ShellChrome(WindowButtons.Right, false, false, true, 0)
        : new ShellChrome(WindowButtons.None, false, false, false, 0);

    public PlatformCapabilities Capabilities => web
        ? FixedPlatformInfo.DesktopCapabilities with
        {
            PickFolders = false, SaveInPlace = false, WatchFolders = false, RevealInFolder = false,
            SavesAsDownload = true, ServerSide = true,
            // CHVD_DROP=0: the portal before its drop catcher is on (the Web host's D-89 setting until then).
            DropFiles = Environment.GetEnvironmentVariable("CHVD_DROP") != "0",
        }
        : formFactor == FormFactor.Desktop
        ? FixedPlatformInfo.DesktopCapabilities
        : FixedPlatformInfo.DesktopCapabilities with { DropFiles = false }; // as the Android host reports

    public IKeyLabels Keys { get; } = new CtrlKeyLabels();
}

public sealed class MemoryStorage : IAppStorage
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }
}

/// <summary>"Open" hands back a fresh fixture document; save pickers write to the temp folder.</summary>
public sealed class FixturePicker : IFilePicker
{
    private int _n;

    public Task<string?> PickDocumentAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(Fixtures.Document($"opened-{Interlocked.Increment(ref _n)}.pdf", 3));

    public Task<IReadOnlyList<string>> PickDocumentsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(new[] { Fixtures.Document($"opened-{Interlocked.Increment(ref _n)}.pdf", 3) });

    public Task<string?> PickSavePdfAsync(string suggestedName, CancellationToken ct = default) =>
        Task.FromResult<string?>(Path.Combine(Fixtures.Folder, "saved-" + suggestedName));

    public Task<string?> PickSaveSessionAsync(string suggestedName, CancellationToken ct = default) =>
        Task.FromResult<string?>(Path.Combine(Fixtures.Folder, suggestedName));

    public Task<string?> PickOpenSessionAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task<string?> PickSaveImageAsync(string suggestedName, CancellationToken ct = default) =>
        Task.FromResult<string?>(Path.Combine(Fixtures.Folder, suggestedName));

    public Task<string?> PickFolderAsync(CancellationToken ct = default) => Task.FromResult<string?>(Fixtures.Folder);

    public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}

public sealed class NoClipboard : IImageClipboard, IClipboard
{
    public bool CanCopyText => true;

    public bool CanCopyImage => true;

    public Task SetTextAsync(string text, CancellationToken ct = default) => Task.CompletedTask;

    public Task SetImageAsync(byte[] pngBytes, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Fixture PDFs authored in code with chuvadi-pdf (no binary files), like the Reader tests.</summary>
public static class Fixtures
{
    public static readonly string Folder = Directory.CreateTempSubdirectory("chvd-mobile-preview-").FullName;

    public static string Document(string name, int pages)
    {
        var b = PdfDocumentBuilder.Create();
        var widths = new[] { 1.0, .96, 1.0, .88, 1.0, .72, 1.0, .94, 1.0, .60, 1.0, .90, 1.0, .80 };
        for (var p = 1; p <= pages; p++)
        {
            const double w = 595, h = 842;
            var pb = b.AddPage(new PageSize(w, h));
            pb.DrawRectangle(0, 0, w, h, Color.FromHex("#FFFFFF"), Color.FromHex("#FFFFFF"), 0);
            pb.DrawText(p == 1 ? "Discharge summary" : $"Page {p}", 60, 90, "Helvetica-Bold", 22, Color.FromHex("#1A1310"));
            var y = h - 140;
            for (var i = 0; i < widths.Length * 2; i++)
            {
                var lw = (w - 120) * widths[i % widths.Length];
                pb.DrawRectangle(60, y, lw, 6, Color.FromHex("#EBE8E2"), Color.FromHex("#EBE8E2"), 0);
                y -= 22;
            }
        }

        var path = Path.Combine(Folder, name);
        File.WriteAllBytes(path, b.ToByteArray());
        return path;
    }
}

/// <summary>Preview stand-in for a host's own notices (Android ships the .NET runtime's).</summary>
public sealed class PreviewHostNotices : IHostNotices
{
    public string? Title => ".NET runtime";

    public string ReadText() => "PREVIEW: a host's own third-party notices appear here (Android: the .NET runtime and .NET for Android notices).";
}

/// <summary>Preview stand-in for the Web host's drop handling (D-89 A): stores each dropped file in a temp
/// folder and opens it through the kit's router, as the Web host does with its working copies.</summary>
public sealed class PreviewDroppedFiles(ChuvadiReader.Hosting.IncomingFiles incoming) : IDroppedFiles
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "chvd-preview-drops");

    public bool IsAvailable => true;

    public long MaxFileBytes => 50L * 1024 * 1024;

    public async Task<IReadOnlyList<string>> ReceiveAsync(IReadOnlyList<DroppedFile> files, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        var failures = new List<string>();
        var stored = new List<string>();
        foreach (var f in files)
        {
            if (!ChuvadiReader.Core.Documents.DocumentFormatExtensions.IsOpenable(f.Name))
            {
                failures.Add($"Couldn't upload {f.Name}: Chuvadi doesn't open this kind of file.");
                continue;
            }

            var path = Path.Combine(Folder, Guid.NewGuid().ToString("N")[..8] + "-" + Path.GetFileName(f.Name));
            await using (var source = f.OpenRead(ct))
            await using (var target = File.Create(path))
            {
                await source.CopyToAsync(target, ct);
            }

            stored.Add(path);
        }

        incoming.Receive(stored);
        return failures;
    }
}
