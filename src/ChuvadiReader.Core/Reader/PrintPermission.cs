using Chuvadi.Pdf.Documents;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// A PDF's own "no printing" permission (PDF 32000-1 §7.6.3.2, /P bit 3). Every platform respects
/// it: the Print card is locked with <see cref="Refused"/> as the reason, and host print services
/// refuse the job as well.
/// </summary>
public static class PrintPermission
{
    public const string Refused = "This file's permissions don't allow printing.";

    /// <summary>False when the file is encrypted and its permissions forbid printing.</summary>
    public static bool Allows(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Encryption?.AllowPrint ?? true;
    }
}
