using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Operations;
using ChuvadiReader.Core.Reader;

namespace ChuvadiReader.Core.Documents;

using Path = System.IO.Path;

/// <summary>An editable bookmark (PDF outline) node: a title, a 0-based destination page, and
/// nested children. Used for reading, editing and writing document outlines (#41 / #47).</summary>
public sealed class BookmarkNode
{
    public string Title { get; set; } = "";
    public int PageIndex { get; set; }
    public List<BookmarkNode> Children { get; set; } = new();

    public BookmarkNode() { }

    public BookmarkNode(string title, int pageIndex)
    {
        Title = title;
        PageIndex = pageIndex;
    }

    public BookmarkNode Clone() => new(Title, PageIndex)
    {
        Children = Children.Select(c => c.Clone()).ToList(),
    };
}

/// <summary>Reads, writes and builds PDF outlines (bookmarks) on top of the library's
/// <see cref="OutlineReader"/> / <see cref="OutlineWriter"/>.</summary>
public sealed class OutlineService
{
    /// <summary>Reads the outline of an open document into an editable tree.</summary>
    public IReadOnlyList<BookmarkNode> Read(PdfDocument doc) => FromItems(OutlineReader.GetOutlines(doc));

    /// <summary>Reads the outline of a file into an editable tree.</summary>
    public IReadOnlyList<BookmarkNode> Read(string path)
    {
        using var doc = PdfDocument.Open(path);
        return Read(doc);
    }

    /// <summary>Writes <paramref name="nodes"/> as the outline of <paramref name="srcBytes"/>,
    /// returning the new document bytes. An empty list clears the outline.</summary>
    public byte[] Write(byte[] srcBytes, IReadOnlyList<BookmarkNode> nodes)
    {
        using var doc = PdfDocument.Open(new MemoryStream(srcBytes, writable: false));
        using var ms = new MemoryStream();
        OutlineWriter.Apply(ms, doc, ToEntries(nodes, doc.PageCount));
        return ms.ToArray();
    }

    /// <summary>Reads <paramref name="srcPath"/>, applies <paramref name="nodes"/> as its outline
    /// and writes the result to <paramref name="outPath"/>.</summary>
    public async Task WriteToFileAsync(string srcPath, string outPath, IReadOnlyList<BookmarkNode> nodes, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            using var doc = PdfDocument.Open(srcPath);
            using var fs = File.Create(outPath);
            OutlineWriter.Apply(fs, doc, ToEntries(nodes, doc.PageCount));
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Injects an outline into an existing PDF file in place (via a temp file swap).</summary>
    public async Task InjectIntoFileAsync(string path, IReadOnlyList<BookmarkNode> nodes, CancellationToken ct = default)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var tmp = path + ".toc.tmp";
        await WriteToFileAsync(path, tmp, nodes, ct).ConfigureAwait(false);
        File.Copy(tmp, path, overwrite: true);
        try { File.Delete(tmp); } catch { /* best effort */ }
    }

    /// <summary>Builds the export outline (#47) for a bound desk: one top-level entry per
    /// consecutive run of pages from the same source (titled by the file name), pointing to that
    /// run's first output page. Each source's own bookmarks are nested underneath, remapped to the
    /// output page positions (pages that were dropped fall back to the section's first page).</summary>
    public List<BookmarkNode> BuildExportOutline(
        IReadOnlyList<BenchPage> pages,
        Func<string, string> titleForSource,
        Func<string, IReadOnlyList<BookmarkNode>> outlineForSource)
    {
        var result = new List<BookmarkNode>();
        var n = pages.Count;
        var i = 0;
        while (i < n)
        {
            var src = pages[i].SourcePath;
            var start = i;
            while (i < n && pages[i].SourcePath == src)
            {
                i++;
            }

            string title;
            List<BookmarkNode> children = new();
            var head = pages[start];
            if (string.IsNullOrEmpty(src) || head.IsBlank || head.IsImage)
            {
                title = head.IsImage ? "Image" : head.IsBlank ? "Blank page" : "Inserted page";
            }
            else
            {
                title = titleForSource(src);
                // origIndex → first output position within this run
                var map = new Dictionary<int, int>();
                for (var k = start; k < i; k++)
                {
                    if (!map.ContainsKey(pages[k].OriginalIndex))
                    {
                        map[pages[k].OriginalIndex] = k;
                    }
                }
                children = Remap(outlineForSource(src), map, start);
            }

            result.Add(new BookmarkNode(title, start) { Children = children });
        }
        return result;
    }

    private static List<BookmarkNode> Remap(IReadOnlyList<BookmarkNode> src, Dictionary<int, int> map, int fallback)
    {
        var list = new List<BookmarkNode>();
        foreach (var node in src)
        {
            var page = map.TryGetValue(node.PageIndex, out var outIdx) ? outIdx : fallback;
            list.Add(new BookmarkNode(node.Title, page) { Children = Remap(node.Children, map, fallback) });
        }
        return list;
    }

    private static List<BookmarkNode> FromItems(IReadOnlyList<OutlineItem> items)
    {
        var list = new List<BookmarkNode>();
        foreach (var it in items)
        {
            list.Add(new BookmarkNode(it.Title ?? "", it.DestinationPageIndex) { Children = FromItems(it.Children) });
        }
        return list;
    }

    private static IReadOnlyList<OutlineEntry> ToEntries(IReadOnlyList<BookmarkNode> nodes, int pageCount)
    {
        var list = new List<OutlineEntry>();
        var max = Math.Max(0, pageCount - 1);
        foreach (var node in nodes)
        {
            var title = string.IsNullOrWhiteSpace(node.Title) ? "Untitled" : node.Title.Trim();
            var page = Math.Clamp(node.PageIndex, 0, max);
            var children = ToEntries(node.Children, pageCount);
            list.Add(children.Count > 0 ? new OutlineEntry(title, page, children) : new OutlineEntry(title, page));
        }
        return list;
    }
}
