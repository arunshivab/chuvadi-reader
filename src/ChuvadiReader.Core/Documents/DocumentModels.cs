namespace ChuvadiReader.Core.Documents;

public enum DocumentFormat
{
    Pdf,
    Docx,
    Xlsx,

    /// <summary>Markdown (.md, .markdown) — viewed read-only in the WebView (D-09).</summary>
    Markdown,

    /// <summary>Plain text (.txt, .log, .csv shown as text) — viewed read-only (D-09).</summary>
    Text,

    /// <summary>HTML (.html, .htm) — viewed sandboxed, read-only (D-09).</summary>
    Html,
}

public static class DocumentFormatExtensions
{
    /// <summary>Maps a format to the icon name used by the UI icon set.</summary>
    public static string IconName(this DocumentFormat format) => format switch
    {
        DocumentFormat.Pdf => "file-pdf",
        DocumentFormat.Docx => "file-docx",
        DocumentFormat.Xlsx => "file-xlsx",
        DocumentFormat.Markdown => "file",
        DocumentFormat.Text => "file",
        DocumentFormat.Html => "file",
        _ => "file-pdf",
    };

    /// <summary>True for formats that are view-only in Chuvadi (D-07, D-09): Mark, Arrange and
    /// Protect stay dimmed until the file is converted to PDF.</summary>
    public static bool IsViewOnly(this DocumentFormat format) => format != DocumentFormat.Pdf;

    /// <summary>Short label for the format (tab tooltips, status bar).</summary>
    public static string Label(this DocumentFormat format) => format switch
    {
        DocumentFormat.Docx => "Word",
        DocumentFormat.Xlsx => "Excel",
        DocumentFormat.Markdown => "Markdown",
        DocumentFormat.Text => "Text",
        DocumentFormat.Html => "HTML",
        _ => "PDF",
    };

    /// <summary>Every file extension Chuvadi can open (lower case, with the dot). The single list the
    /// Open dialogs, window drop and "Open with" use, so they always agree.</summary>
    public static IReadOnlyList<string> OpenableExtensions { get; } =
        new[] { ".pdf", ".docx", ".xlsx", ".md", ".markdown", ".txt", ".log", ".html", ".htm" };

    /// <summary>True when <paramref name="path"/> has an extension in <see cref="OpenableExtensions"/>.</summary>
    public static bool IsOpenable(string path) =>
        OpenableExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    public static DocumentFormat FromPath(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".docx" => DocumentFormat.Docx,
            ".xlsx" => DocumentFormat.Xlsx,
            ".md" or ".markdown" => DocumentFormat.Markdown,
            ".txt" or ".log" or ".text" => DocumentFormat.Text,
            ".html" or ".htm" => DocumentFormat.Html,
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
