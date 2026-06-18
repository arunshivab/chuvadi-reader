namespace ChuvadiReader.Core.Documents;

public enum DocumentFormat
{
    Pdf,
    Docx,
    Xlsx,
}

public static class DocumentFormatExtensions
{
    /// <summary>Maps a format to the icon name used by the UI icon set.</summary>
    public static string IconName(this DocumentFormat format) => format switch
    {
        DocumentFormat.Pdf => "file-pdf",
        DocumentFormat.Docx => "file-docx",
        DocumentFormat.Xlsx => "file-xlsx",
        _ => "file-pdf",
    };

    public static DocumentFormat FromPath(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".docx" => DocumentFormat.Docx,
            ".xlsx" => DocumentFormat.Xlsx,
            _ => DocumentFormat.Pdf,
        };
    }
}

/// <summary>A document that was recently opened.</summary>
public sealed record RecentItem(
    string FileName,
    string Path,
    DocumentFormat Format,
    DateTimeOffset LastOpened,
    bool IsEncrypted);

/// <summary>A pinned document or folder the user wants quick access to.</summary>
public sealed record PinnedItem(
    string Name,
    string Path,
    bool IsFolder,
    DocumentFormat? Format);
