using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// Merge and split operations over the chuvadi-pdf distribution
/// (<see cref="PageOperations"/>). Each operation opens its inputs by path, writes
/// the result to a file, and disposes the documents. The library work runs on a
/// background thread so the UI stays responsive. All page indices here are
/// zero-based at the library boundary; callers pass one-based page numbers and
/// convert at the edge.
/// </summary>
public sealed class PdfToolsService
{
    /// <summary>Opens a PDF just long enough to read its page count.</summary>
    public async Task<int> GetPageCountAsync(string path, CancellationToken ct = default)
    {
        using var doc = await PdfDocument.OpenAsync(path, ct).ConfigureAwait(false);
        return doc.PageCount;
    }

    /// <summary>Merges the given PDFs (in order) into a single output file.</summary>
    public async Task MergeAsync(IReadOnlyList<string> inputPaths, string outputPath, CancellationToken ct = default)
    {
        var docs = new List<PdfDocument>(inputPaths.Count);
        try
        {
            foreach (var path in inputPaths)
            {
                docs.Add(await PdfDocument.OpenAsync(path, ct).ConfigureAwait(false));
            }

            var array = docs.ToArray();
            await Task.Run(() =>
            {
                using var output = File.Create(outputPath);
                PageOperations.Merge(output, array);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            foreach (var doc in docs)
            {
                doc.Dispose();
            }
        }
    }

    /// <summary>Extracts a contiguous page range into a new PDF file.
    /// <paramref name="startIndex"/> is zero-based; <paramref name="count"/> pages are taken.</summary>
    public async Task ExtractRangeAsync(string inputPath, string outputPath, int startIndex, int count, CancellationToken ct = default)
    {
        using var doc = await PdfDocument.OpenAsync(inputPath, ct).ConfigureAwait(false);
        await Task.Run(() =>
        {
            using var output = File.Create(outputPath);
            PageOperations.ExtractPages(output, doc, startIndex, count);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Writes every page of the source PDF as its own single-page file in
    /// <paramref name="outputDir"/>, named "{source}_pNN.pdf". Returns the page count.</summary>
    public async Task<int> SplitToFolderAsync(string inputPath, string outputDir, CancellationToken ct = default)
    {
        using var doc = await PdfDocument.OpenAsync(inputPath, ct).ConfigureAwait(false);
        var pageCount = doc.PageCount;
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var width = pageCount.ToString().Length;

        await Task.Run(() =>
        {
            for (var i = 0; i < pageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var name = $"{baseName}_p{(i + 1).ToString().PadLeft(width, '0')}.pdf";
                using var output = File.Create(Path.Combine(outputDir, name));
                PageOperations.ExtractPages(output, doc, i, 1);
            }
        }, ct).ConfigureAwait(false);

        return pageCount;
    }
}
