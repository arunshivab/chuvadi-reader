using Chuvadi.Pdf.Documents;

namespace ChuvadiReader.Core.Documents;

/// <summary>Adobe-style document properties read from a PDF.</summary>
public sealed record DocProperties(
    string FileName,
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    string? Creator,
    string? Producer,
    string? Created,
    string? Modified,
    int PageCount,
    string PageSize,
    bool Encrypted,
    string? Encryption,
    bool IsXfa,
    bool Linearized,
    long SizeBytes);

/// <summary>Reads document properties (Info metadata, page size, encryption, XFA)
/// from a PDF for the properties panel.</summary>
public sealed class DocumentPropertiesService
{
    public async Task<DocProperties> ReadAsync(string path, CancellationToken ct = default)
    {
        using var doc = await PdfFiles.OpenAsync(path, ct).ConfigureAwait(false);
        var fi = new FileInfo(path);

        var size = "—";
        try
        {
            PdfPage? first = null;
            var seen = new HashSet<string>();
            // D-160: the size of the page as shown (its CropBox within its MediaBox), as viewers report it.
            foreach (var p in doc.Pages)
            {
                first ??= p;
                var shown = ShownBox.Of(p).ShownSize(ShownBox.TurnOf(p));   // B-32: turned as shown
                seen.Add($"{Math.Round(shown.W)}x{Math.Round(shown.H)}");
            }

            if (first is not null)
            {
                var size1 = ShownBox.Of(first).ShownSize(ShownBox.TurnOf(first));
                var wmm = size1.W * 25.4 / 72.0;
                var hmm = size1.H * 25.4 / 72.0;
                size = $"{wmm:0} × {hmm:0} mm{PaperName(wmm, hmm)}";
                if (seen.Count > 1)
                {
                    size += $" (+{seen.Count - 1} other size{(seen.Count - 1 == 1 ? "" : "s")})";
                }
            }
        }
        catch { }

        var encrypted = false;
        string? enc = null;
        try
        {
            if (doc.Encryption is { } e)
            {
                encrypted = true;
                enc = $"{e.Algorithm}, {e.KeyLength}-bit";
            }
        }
        catch { }

        return new DocProperties(
            fi.Name,
            Clean(doc.Title),
            Clean(doc.Author),
            Clean(doc.Subject),
            Clean(doc.Keywords),
            Clean(doc.Creator),
            Clean(doc.Producer),
            doc.CreationDate is { } cd ? cd.ToString("yyyy-MM-dd HH:mm") : null,
            doc.ModDate is { } md ? md.ToString("yyyy-MM-dd HH:mm") : null,
            doc.PageCount,
            size,
            encrypted,
            enc,
            doc.IsXfa,
            doc.IsLinearized,
            fi.Length);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string PaperName(double wmm, double hmm)
    {
        var w = Math.Min(wmm, hmm);
        var h = Math.Max(wmm, hmm);
        bool Near(double a, double b) => Math.Abs(a - b) < 6;
        if (Near(w, 210) && Near(h, 297)) return " · A4";
        if (Near(w, 297) && Near(h, 420)) return " · A3";
        if (Near(w, 148) && Near(h, 210)) return " · A5";
        if (Near(w, 420) && Near(h, 594)) return " · A2";
        if (Near(w, 216) && Near(h, 279)) return " · Letter";
        if (Near(w, 216) && Near(h, 356)) return " · Legal";
        if (Near(w, 279) && Near(h, 432)) return " · Tabloid";
        return string.Empty;
    }
}
