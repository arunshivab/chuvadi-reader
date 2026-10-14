using Chuvadi.Pdf.IO;

namespace ChuvadiReader.Core.Documents;

/// <summary>Outcome of a recovery attempt.</summary>
public sealed record RecoveryResult(
    string OutputPath, bool Repaired, int ObjectsRecovered,
    bool TrailerReconstructed, bool TruncationDetected);

/// <summary>
/// User-initiated recovery of a damaged/undisplayable PDF, using the library's
/// <see cref="PdfRepairer"/>. This <em>rewrites</em> the document (rebuilt xref, recovered
/// objects), so it is never run automatically — the UI exposes it as an explicit "Try
/// recovery" action and opens the repaired copy as a new, clearly-labelled file. The
/// original is never touched; output goes to a new "(recovered)" file.
/// </summary>
public sealed class RecoveryService
{
    private readonly SaveFolderService _saveFolders;

    public RecoveryService(SaveFolderService saveFolders) => _saveFolders = saveFolders;

    public string DefaultFolder => _saveFolders.SubFolder("Recovered");

    /// <summary>Repairs <paramref name="sourcePath"/> into a new PDF and returns its path.
    /// Output is always a new file (never overwrites the original).</summary>
    public Task<RecoveryResult> RecoverAsync(string sourcePath, string? outputFolder = null, CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var folder = string.IsNullOrEmpty(outputFolder) ? DefaultFolder : outputFolder;
            Directory.CreateDirectory(folder);
            var outPath = ExportService.UniquePath(
                folder, Path.GetFileNameWithoutExtension(sourcePath) + " (recovered)", "pdf");

            using (var input = File.OpenRead(sourcePath))
            using (var output = File.Create(outPath))
            {
                var report = PdfRepairer.Repair(input, output);
                return new RecoveryResult(
                    outPath, report.Repaired, report.ObjectsRecovered,
                    report.TrailerReconstructed, report.TruncationDetected);
            }
        }, ct);
}
