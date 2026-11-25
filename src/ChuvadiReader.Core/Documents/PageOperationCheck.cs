using System.Collections.Concurrent;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace ChuvadiReader.Core.Documents;

/// <summary>What a page operation leaves out of one file, said before it runs.</summary>
/// <param name="FileName">The file's name.</param>
/// <param name="Lost">What the file holds that the result will not have, in words ("bookmarks", "page labels").</param>
/// <param name="HasLayers">The file has layers, whose hidden ones may show in the result.</param>
public sealed record PageOperationFile(string FileName, IReadOnlyList<string> Lost, bool HasLayers);

/// <summary>D-198, until the Reader is on Pdf 3.35.1 (library advisory, OPS-02 and OPS-03): the library's page
/// operations (merge, extract, reorder, rotate: everything Arrange, Merge and Split write) drop a file's bookmarks,
/// form, named destinations, page labels, language and tags, and can make a hidden layer visible. The Reader asks
/// before every page operation and names what each file will lose. Remove with the ask when Phase 2 takes the
/// patched library.</summary>
public static class PageOperationCheck
{
    /// <summary>The things a page operation drops, in the order they are named.</summary>
    public static IReadOnlyList<string> AllDropped { get; } =
        ["bookmarks", "form fields", "links to places in the file", "page labels", "language", "accessibility tags"];

    private static readonly ConcurrentDictionary<string, (long Length, DateTime Written, PageOperationFile Result)> Seen = new(StringComparer.Ordinal);

    /// <summary>What a page operation would leave out of this file. A file that cannot be read here (missing,
    /// damaged, needs a password) is reported with nothing found: the operation itself says what is wrong.</summary>
    public static PageOperationFile Inspect(string path)
    {
        string name = Path.GetFileName(path);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new(name, [], false);
            if (Seen.TryGetValue(path, out var seen) && seen.Length == info.Length && seen.Written == info.LastWriteTimeUtc) return seen.Result;

            var lost = new List<string>();
            bool layers;
            using (var doc = PdfFiles.Open(path))
            {
                var catalog = doc.Catalog;
                PdfDictionary? Dict(PdfDictionary? from, string key) =>
                    from is null ? null : doc.Objects.ResolveDictionaryEntry<PdfDictionary>(from, PdfName.Intern(key));
                bool Has(PdfDictionary? from, string key) => from is not null && from.ContainsKey(PdfName.Intern(key));

                if (Has(Dict(catalog, "Outlines"), "First")) lost.Add(AllDropped[0]);
                if (Has(Dict(catalog, "AcroForm"), "Fields")) lost.Add(AllDropped[1]);
                if (Has(catalog, "Dests") || Has(Dict(catalog, "Names"), "Dests")) lost.Add(AllDropped[2]);
                if (Has(catalog, "PageLabels")) lost.Add(AllDropped[3]);
                if (Has(catalog, "Lang")) lost.Add(AllDropped[4]);
                if (doc.HasStructTree || doc.IsTagged) lost.Add(AllDropped[5]);
                layers = OptionalContentReader.GetGroups(doc).Count > 0;
            }

            var result = new PageOperationFile(name, lost, layers);
            Seen[path] = (info.Length, info.LastWriteTimeUtc, result);
            return result;
        }
        catch (Exception)
        {
            return new(name, [], false);   // never the reason an operation cannot start
        }
    }
}
