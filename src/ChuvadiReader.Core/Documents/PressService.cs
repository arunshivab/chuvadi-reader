using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;
using ChuvadiReader.Core.Storage;
using ChuvadiReader.Platform;

namespace ChuvadiReader.Core.Documents;

public enum PressPreset
{
    Light,
    Balanced,
    Strong,
    Custom,
}

/// <summary>Settings for a press (compress) run.</summary>
public sealed record PressOptions(bool RecompressImages, int JpegQuality, int MinStreamLength, int MinImagePixels)
{
    public static PressOptions For(PressPreset preset) => preset switch
    {
        // Light = lossless: compress streams + drop dead objects, never touch images.
        PressPreset.Light => new PressOptions(false, 80, 256, 1_000_000),
        PressPreset.Balanced => new PressOptions(true, 70, 256, 10_000),
        PressPreset.Strong => new PressOptions(true, 45, 128, 2_000),
        _ => new PressOptions(true, 70, 256, 10_000),
    };
}

/// <summary>Outcome of a press run.</summary>
public sealed record PressResult(
    string OutputPath,
    long BeforeBytes,
    long AfterBytes,
    int ImagesRecompressed,
    int StreamsCompressed,
    int ObjectsRemoved)
{
    public long SavedBytes => Math.Max(0, BeforeBytes - AfterBytes);

    public double SavedPercent => BeforeBytes > 0 ? 100.0 * (BeforeBytes - AfterBytes) / BeforeBytes : 0;

    /// <summary>PS09: a pressed file can come out no smaller; say so rather than "-0.3% smaller".</summary>
    public string SavedLabel => AfterBytes < BeforeBytes
        ? $"{SavedPercent.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)}% smaller"
        : AfterBytes == BeforeBytes ? "no smaller"
        : $"no smaller: {(-SavedPercent).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)}% bigger";

    /// <summary>Set when a target size was asked for and even the strongest pressing could not reach it.</summary>
    public bool TargetMissed { get; init; }

    /// <summary>Set when the source was locked: the pressed copy keeps its open password and permissions with a new
    /// permissions password (D-207, D-210).</summary>
    public bool NewOwnerPassword { get; init; }
}

/// <summary>
/// Compresses ("presses") PDFs using the library's <see cref="PdfCompressor"/>.
/// Output is always a new file (never overwrites), by default into
/// Chuvadi Documents\Compressed. Supports presets, fully custom options, and a
/// target-size mode that steps quality down until the file fits.
/// </summary>
public sealed class PressService
{
    private readonly SaveFolderService _saveFolders;

    private readonly IAppPaths? _paths;

    public PressService(SaveFolderService saveFolders, IAppPaths? paths = null)
    {
        _saveFolders = saveFolders;
        _paths = paths;
    }

    public string DefaultFolder => _saveFolders.SubFolder("Compressed");

    public Task<PressResult> PressAsync(string pdfPath, string? outputFolder, PressOptions options, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var folder = string.IsNullOrEmpty(outputFolder) ? DefaultFolder : outputFolder;
            Directory.CreateDirectory(folder);
            var (source, plain, saveLock) = ReadThroughLock(pdfPath);
            var opts = new CompressionOptions
            {
                RecompressImages = options.RecompressImages,
                JpegQuality = options.JpegQuality,
                MinStreamLengthToCompress = options.MinStreamLength,
                MinImagePixelsToRecompress = options.MinImagePixels,
            };

            ct.ThrowIfCancellationRequested();
            using var doc = PdfDocument.Open(new MemoryStream(plain, writable: false));
            using var ms = new MemoryStream();
            var result = PdfCompressor.Compress(doc, ms, opts);
            if (result.Skipped) throw new InvalidOperationException(SkippedText(result.SkipReason.ToString()));
            var outPath = Write(folder, pdfPath, SourceLock.Lock(ms.ToArray(), saveLock));
            return new PressResult(outPath, source.LongLength, new FileInfo(outPath).Length, result.ImagesRecompressed, result.StreamsCompressed, result.ObjectsRemoved)
            {
                NewOwnerPassword = saveLock.NewOwnerPassword,
            };
        }, ct);

    /// <summary>Presses to a target size with the library's own search (PdfCompressor.CompressToTarget): the
    /// highest JPEG quality whose output fits. When nothing fits, the smallest output is kept and
    /// <see cref="PressResult.TargetMissed"/> says so.</summary>
    public Task<PressResult> PressToTargetAsync(string pdfPath, string? outputFolder, long targetBytes, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var folder = string.IsNullOrEmpty(outputFolder) ? DefaultFolder : outputFolder;
            Directory.CreateDirectory(folder);
            var (source, plain, saveLock) = ReadThroughLock(pdfPath);
            ct.ThrowIfCancellationRequested();
            using var doc = PdfDocument.Open(new MemoryStream(plain, writable: false));
            using var ms = new MemoryStream();
            var result = PdfCompressor.CompressToTarget(doc, ms, targetBytes, null);
            if (result.SkipReason != CompressionSkipReason.None) throw new InvalidOperationException(SkippedText(result.SkipReason.ToString()));
            var outPath = Write(folder, pdfPath, SourceLock.Lock(ms.ToArray(), saveLock));
            return new PressResult(outPath, source.LongLength, new FileInfo(outPath).Length, 0, 0, 0)
            {
                TargetMissed = !result.TargetMet,
                NewOwnerPassword = saveLock.NewOwnerPassword,
            };
        }, ct);

    /// <summary>The source, its pages with no lock (memory only), and the lock the pressed copy gets (D-207).</summary>
    private static (byte[] Source, byte[] Plain, SaveLock Lock) ReadThroughLock(string pdfPath)
    {
        var source = File.ReadAllBytes(pdfPath);
        var (open, owner) = PdfPasswords.Both(pdfPath);
        var saveLock = SourceLock.Plan(source, open, owner, intoSource: false);
        return (source, SourceLock.Plain(source, saveLock.OpenPassword), saveLock);
    }

    private static string Write(string folder, string pdfPath, byte[] bytes)
    {
        var outPath = ExportService.UniquePath(folder, Path.GetFileNameWithoutExtension(pdfPath) + "-pressed", "pdf");
        var temp = outPath + ".chuvadi-tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, outPath);
        }
        finally
        {
            TryDelete(temp);
        }

        return outPath;
    }

    private static string SkippedText(string reason) => reason switch
    {
        "Signed" => "This file is signed, so pressing it would break its signatures. It wasn't pressed.",
        _ => $"This file wasn't pressed ({reason}).",
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
