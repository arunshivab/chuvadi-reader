using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

namespace ChuvadiReader.Core.Documents;

/// <summary>Lightweight probe used to surface the Flatten action (e.g. an "XFA form" hint).</summary>
public sealed record FlattenInfo(int PageCount, bool IsXfa);

/// <summary>
/// Flattens a PDF's dynamic layers — XFA form data, AcroForm fields and markup annotations —
/// into the page content, producing a static, non-XFA PDF that renders and prints identically
/// in any viewer. Built on <c>Chuvadi.Pdf.Operations.AnnotationFlattener</c>.
/// </summary>
public sealed class FlattenService
{
    /// <summary>Opens the file just far enough to report page count and whether it is an XFA form,
    /// so the Ui can show a hint (e.g. on a Certificate of Incorporation).</summary>
    public FlattenInfo Probe(string sourcePath)
    {
        using var doc = PdfDocument.Open(sourcePath);
        return new FlattenInfo(doc.PageCount, doc.IsXfa);
    }

    /// <summary>
    /// Flatten <paramref name="sourcePath"/> to a static PDF at <paramref name="outputPath"/>.
    /// By default bakes everything (form fields + XFA values + markup) so the result is fully static.
    /// </summary>
    public Task FlattenToFileAsync(
        string sourcePath,
        string outputPath,
        AnnotationFlattenKinds kinds = AnnotationFlattenKinds.All,
        CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var src = PdfDocument.Open(sourcePath);
            using var fs = File.Create(outputPath);
            AnnotationFlattener.Flatten(fs, src, new AnnotationFlattenOptions { Kinds = kinds });
        }, ct);
}
