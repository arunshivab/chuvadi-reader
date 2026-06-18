using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

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

    public PressService(SaveFolderService saveFolders) => _saveFolders = saveFolders;

    public string DefaultFolder => _saveFolders.SubFolder("Compressed");

    public Task<PressResult> PressAsync(string pdfPath, string? outputFolder, PressOptions options, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var folder = string.IsNullOrEmpty(outputFolder) ? DefaultFolder : outputFolder;
            Directory.CreateDirectory(folder);
            var before = new FileInfo(pdfPath).Length;
            var outPath = ExportService.UniquePath(folder, Path.GetFileNameWithoutExtension(pdfPath) + "-pressed", "pdf");

            var result = RunCompress(pdfPath, outPath, options, ct);
            var after = new FileInfo(outPath).Length;
            return new PressResult(outPath, before, after, result.ImagesRecompressed, result.StreamsCompressed, result.ObjectsRemoved);
        }, ct);

    /// <summary>Presses repeatedly, lowering JPEG quality until the output is at or
    /// below <paramref name="targetBytes"/> (or the quality floor is reached), keeping
    /// only the best attempt.</summary>
    public Task<PressResult> PressToTargetAsync(string pdfPath, string? outputFolder, long targetBytes, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var folder = string.IsNullOrEmpty(outputFolder) ? DefaultFolder : outputFolder;
            Directory.CreateDirectory(folder);
            var before = new FileInfo(pdfPath).Length;

            string? bestTmp = null;
            CompressionResult? bestResult = null;
            var bestSize = long.MaxValue;

            foreach (var quality in new[] { 85, 70, 55, 45, 35, 25 })
            {
                ct.ThrowIfCancellationRequested();
                var tmp = Path.Combine(Path.GetTempPath(), $"press-{Guid.NewGuid():N}.pdf");
                var result = RunCompress(pdfPath, tmp, new PressOptions(true, quality, 128, 2_000), ct);
                var size = new FileInfo(tmp).Length;

                if (size < bestSize)
                {
                    if (bestTmp is not null)
                    {
                        TryDelete(bestTmp);
                    }

                    bestTmp = tmp;
                    bestResult = result;
                    bestSize = size;
                }
                else
                {
                    TryDelete(tmp);
                }

                if (size <= targetBytes)
                {
                    break;
                }
            }

            var outPath = ExportService.UniquePath(folder, Path.GetFileNameWithoutExtension(pdfPath) + "-pressed", "pdf");
            File.Move(bestTmp!, outPath);
            return new PressResult(outPath, before, bestSize, bestResult!.ImagesRecompressed, bestResult.StreamsCompressed, bestResult.ObjectsRemoved);
        }, ct);

    private static CompressionResult RunCompress(string pdfPath, string outPath, PressOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var opts = new CompressionOptions
        {
            RecompressImages = options.RecompressImages,
            JpegQuality = options.JpegQuality,
            MinStreamLengthToCompress = options.MinStreamLength,
            MinImagePixelsToRecompress = options.MinImagePixels,
        };

        using var doc = PdfDocument.Open(pdfPath);
        using var fs = File.Create(outPath);
        return PdfCompressor.Compress(doc, fs, opts);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
