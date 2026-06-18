using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Text;

namespace ChuvadiReader.Core.Documents;

/// <summary>A match for a folder search: the file, the page (1-based), and a
/// snippet of surrounding text.</summary>
public sealed record SearchHit(string Path, string Name, int Page, string Snippet);

/// <summary>
/// Searches for a word across every PDF in the library folder without opening
/// them in the reader, using the library's <see cref="TextExtractor"/>. Returns
/// one hit per matching page with a short snippet for context.
/// </summary>
public sealed class FolderSearchService
{
    private readonly LibraryService _library;

    public FolderSearchService(LibraryService library) => _library = library;

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxHits = 300, CancellationToken ct = default)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length < 2)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        var files = _library.Entries.Select(e => (e.Path, e.Name)).ToList();

        return Task.Run(() =>
        {
            var hits = new List<SearchHit>();
            foreach (var (path, name) in files)
            {
                if (ct.IsCancellationRequested || hits.Count >= maxHits)
                {
                    break;
                }

                try
                {
                    using var doc = PdfDocument.Open(path);
                    var extractor = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);
                    foreach (var page in doc.Pages)
                    {
                        if (ct.IsCancellationRequested || hits.Count >= maxHits)
                        {
                            break;
                        }

                        string text;
                        try
                        {
                            text = extractor.ExtractText(page) ?? string.Empty;
                        }
                        catch
                        {
                            continue;
                        }

                        var idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            hits.Add(new SearchHit(path, name, page.PageNumber, Snippet(text, idx, query.Length)));
                        }
                    }
                }
                catch
                {
                    // unreadable / encrypted file — skip
                }
            }

            return (IReadOnlyList<SearchHit>)hits;
        }, ct);
    }

    private static string Snippet(string text, int idx, int matchLen)
    {
        const int pad = 44;
        var start = Math.Max(0, idx - pad);
        var end = Math.Min(text.Length, idx + matchLen + pad);
        var s = Regex.Replace(text.Substring(start, end - start), @"\s+", " ").Trim();
        return (start > 0 ? "… " : string.Empty) + s + (end < text.Length ? " …" : string.Empty);
    }
}
