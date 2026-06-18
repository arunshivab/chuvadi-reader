using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// Composes an arbitrary arrangement of pages (drawn from several sources, in any
/// order, with rotations, with inserted blank pages, and with raw image pages) into
/// a single PDF.
///
/// Blank pages and image pages carry no source PDF, so they are first materialised
/// into tiny one-page temp PDFs (images via <see cref="ImagePdfConverter"/>) and then
/// flow through the same verified <see cref="PageOperations"/> pipeline as everything
/// else: Merge distinct sources, ReorderPages into screen order, ExtractPages to drop
/// the rest, RotatePages per distinct angle.
/// </summary>
public sealed class BenchComposer
{
    private sealed record Resolved(string Path, int Index, int Rotation);

    public Task ComposeToFileAsync(IReadOnlyList<BenchPage> pages, string outputPath, CancellationToken ct = default)
        => Task.Run(async () =>
        {
            var bytes = await ComposeAsync(pages, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(outputPath, bytes, ct).ConfigureAwait(false);
        }, ct);

    public async Task<byte[]> ComposeAsync(IReadOnlyList<BenchPage> pages, CancellationToken ct = default)
    {
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("There are no pages to compose.");
        }

        var tempFiles = new List<string>();
        try
        {
            // Resolve blanks to temp one-page PDFs (reused per background colour),
            // and image pages to their own temp one-page PDFs (converted only now).
            var blankByColor = new Dictionary<string, string>();
            var resolved = new List<Resolved>(pages.Count);
            foreach (var page in pages)
            {
                if (page.IsBlank)
                {
                    var key = page.BackgroundHex ?? "";
                    if (!blankByColor.TryGetValue(key, out var temp))
                    {
                        temp = Path.GetTempFileName();
                        await File.WriteAllBytesAsync(temp, BuildBlankPdf(page.BackgroundHex), ct).ConfigureAwait(false);
                        blankByColor[key] = temp;
                        tempFiles.Add(temp);
                    }
                    resolved.Add(new Resolved(temp, 0, ((page.Rotation % 360) + 360) % 360));
                }
                else if (page.IsImage && !string.IsNullOrWhiteSpace(page.ImagePath))
                {
                    var temp = Path.GetTempFileName();
                    ImagePdfConverter.ConvertFile(page.ImagePath, temp, ImagePdfOptions.Default);
                    tempFiles.Add(temp);
                    resolved.Add(new Resolved(temp, 0, ((page.Rotation % 360) + 360) % 360));
                }
                else
                {
                    resolved.Add(new Resolved(page.SourcePath, page.OriginalIndex, ((page.Rotation % 360) + 360) % 360));
                }
            }

            var sourceOrder = new List<string>();
            foreach (var r in resolved)
            {
                if (!sourceOrder.Contains(r.Path))
                {
                    sourceOrder.Add(r.Path);
                }
            }

            var docs = new List<PdfDocument>(sourceOrder.Count);
            try
            {
                foreach (var path in sourceOrder)
                {
                    docs.Add(await PdfDocument.OpenAsync(path, ct).ConfigureAwait(false));
                }

                var offset = new Dictionary<string, int>(sourceOrder.Count);
                var running = 0;
                for (var i = 0; i < sourceOrder.Count; i++)
                {
                    offset[sourceOrder[i]] = running;
                    running += docs[i].PageCount;
                }

                var total = running;

                var combined = docs.Count == 1
                    ? await File.ReadAllBytesAsync(sourceOrder[0], ct).ConfigureAwait(false)
                    : MergeToBytes(docs);

                var want = new List<int>(resolved.Count);
                foreach (var r in resolved)
                {
                    want.Add(offset[r.Path] + r.Index);
                }

                var k = want.Count;
                byte[] result;

                if (k == total && IsIdentity(want, total))
                {
                    result = combined;
                }
                else
                {
                    // Build the output one page at a time and merge. This is the only
                    // approach that is correct when a page appears more than once (the
                    // same source page can be placed in a desk twice), which a single
                    // ReorderPages permutation cannot express.
                    using var combinedDoc = await OpenBytesAsync(combined, ct).ConfigureAwait(false);
                    var singleCache = new Dictionary<int, byte[]>();

                    async Task<byte[]> SingleAsync(int idx)
                    {
                        if (!singleCache.TryGetValue(idx, out var bytes))
                        {
                            using var sms = new MemoryStream();
                            PageOperations.ExtractPages(sms, combinedDoc, idx, 1);
                            bytes = sms.ToArray();
                            singleCache[idx] = bytes;
                        }
                        return bytes;
                    }

                    var singleDocs = new List<PdfDocument>(want.Count);
                    var singleTemps = new List<string>(want.Count);
                    try
                    {
                        foreach (var idx in want)
                        {
                            var temp = Path.GetTempFileName();
                            await File.WriteAllBytesAsync(temp, await SingleAsync(idx).ConfigureAwait(false), ct).ConfigureAwait(false);
                            singleTemps.Add(temp);
                            singleDocs.Add(await PdfDocument.OpenAsync(temp, ct).ConfigureAwait(false));
                        }

                        using var mms = new MemoryStream();
                        PageOperations.Merge(mms, singleDocs.ToArray());
                        result = mms.ToArray();
                    }
                    finally
                    {
                        foreach (var sd in singleDocs)
                        {
                            sd.Dispose();
                        }
                        foreach (var temp in singleTemps)
                        {
                            try { File.Delete(temp); } catch { /* best effort */ }
                        }
                    }
                }

                // Apply rotations, grouped by angle, on final page positions.
                var byAngle = resolved
                    .Select((r, i) => (r.Rotation, Index: i))
                    .Where(t => t.Rotation != 0)
                    .GroupBy(t => t.Rotation);

                foreach (var group in byAngle)
                {
                    using var doc = await OpenBytesAsync(result, ct).ConfigureAwait(false);
                    using var oms = new MemoryStream();
                    PageOperations.RotatePages(oms, doc, group.Key, group.Select(t => t.Index));
                    result = oms.ToArray();
                }

                return result;
            }
            finally
            {
                foreach (var doc in docs)
                {
                    doc.Dispose();
                }
            }
        }
        finally
        {
            foreach (var temp in tempFiles)
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }
    }

    private static byte[] BuildBlankPdf(string? bgHex)
    {
        var builder = PdfDocumentBuilder.Create();
        var page = builder.AddPage(PageSize.A4);
        if (!string.IsNullOrWhiteSpace(bgHex))
        {
            var color = Color.FromHex(bgHex);
            page.DrawRectangle(0, 0, page.Width, page.Height, color, null, 0);
        }

        return builder.ToByteArray();
    }

    private static bool IsIdentity(List<int> order, int total)
    {
        if (order.Count != total)
        {
            return false;
        }

        for (var i = 0; i < total; i++)
        {
            if (order[i] != i)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] MergeToBytes(List<PdfDocument> docs)
    {
        using var ms = new MemoryStream();
        PageOperations.Merge(ms, docs.ToArray());
        return ms.ToArray();
    }

    private static async Task<PdfDocument> OpenBytesAsync(byte[] bytes, CancellationToken ct)
    {
        var temp = Path.GetTempFileName();
        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
        return await PdfDocument.OpenAsync(temp, ct).ConfigureAwait(false);
    }
}
