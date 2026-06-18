using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Svg;

namespace ChuvadiReader.Core.Documents;

public enum ExportFormat
{
    Svg,
    Png,
    Jpg,
    Bmp,
    Tiff,
}

/// <summary>
/// Exports pages of a PDF to image files. SVG is vector (selectable text); PNG,
/// JPG and BMP are rasterised at a chosen DPI. Files are never overwritten — a
/// " (n)" suffix is added when a name already exists. One file per page; a single
/// page keeps the document name, otherwise each file is suffixed with its page.
/// </summary>
public sealed class ExportService
{
    /// <summary>Exports pages and returns the number of files written.</summary>
    public Task<int> ExportAsync(
        string pdfPath,
        string outputFolder,
        ExportFormat format,
        bool allPages,
        int currentPageIndex,
        int dpi,
        string? baseNameOverride = null,
        CancellationToken ct = default)
        => Task.Run(() =>
        {
            using var doc = PdfDocument.Open(pdfPath);
            var pageCount = doc.PageCount;
            if (pageCount == 0)
            {
                return 0;
            }

            Directory.CreateDirectory(outputFolder);
            var baseName = Sanitize(baseNameOverride ?? Path.GetFileNameWithoutExtension(pdfPath));
            var ext = Extension(format);

            var indices = allPages
                ? Enumerable.Range(0, pageCount)
                : new[] { Math.Clamp(currentPageIndex, 0, pageCount - 1) };
            var multi = allPages && pageCount > 1;

            SvgRenderer? svg = format == ExportFormat.Svg
                ? new SvgRenderer(new SvgExportOptions { TextStrategy = SvgTextStrategy.Selectable })
                : null;
            PageRasterizer? raster = format != ExportFormat.Svg
                ? new PageRasterizer(doc.Objects, new RenderOptions { Dpi = dpi, Background = Chuvadi.Pdf.Graphics.ColorF.White, AntiAlias = true })
                : null;

            var written = 0;
            foreach (var i in indices)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.Pages[i];
                var bytes = format switch
                {
                    ExportFormat.Svg => Encoding.UTF8.GetBytes(svg!.RenderPage(doc, i)),
                    ExportFormat.Tiff => TiffEncoder.Encode(new ImageFrame(raster!.Rasterize(page), ImageColorFormat.Rgba32)),
                    _ => EncodeRaster(raster!, page, format),
                };

                var pageBase = multi ? $"{baseName}-p{i + 1:000}" : baseName;
                File.WriteAllBytes(UniquePath(outputFolder, pageBase, ext), bytes);
                written++;
            }

            return written;
        }, ct);

    private static byte[] EncodeRaster(PageRasterizer raster, PdfPage page, ExportFormat format)
    {
        var frame = new ImageFrame(raster.Rasterize(page), ImageColorFormat.Rgba32);
        using var ms = new MemoryStream();
        switch (format)
        {
            case ExportFormat.Png:
                PngEncoder.Encode(frame, ms, false);
                break;
            case ExportFormat.Jpg:
                JpegEncoder.Encode(frame, ms, 92);
                break;
            default:
                BmpEncoder.Encode(frame, ms, false);
                break;
        }

        return ms.ToArray();
    }

    // The content is already centred on the page by the renderer; the export
    // writes the library's SVG verbatim.

    /// <summary>A path that does not yet exist, adding " (n)" before the extension if needed.</summary>
    public static string UniquePath(string folder, string baseName, string ext)
    {
        var path = Path.Combine(folder, $"{baseName}.{ext}");
        if (!File.Exists(path))
        {
            return path;
        }

        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(folder, $"{baseName} ({n}).{ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Extension(ExportFormat f) => f switch
    {
        ExportFormat.Svg => "svg",
        ExportFormat.Png => "png",
        ExportFormat.Jpg => "jpg",
        ExportFormat.Bmp => "bmp",
        ExportFormat.Tiff => "tiff",
        _ => "png",
    };

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "page" : name;
    }
}
