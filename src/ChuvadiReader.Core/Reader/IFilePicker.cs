namespace ChuvadiReader.Core.Reader;

/// <summary>Native file/folder dialogs, implemented per host.</summary>
public interface IFilePicker
{
    /// <summary>Shows an open dialog; returns the chosen path, or null if cancelled.</summary>
    Task<string?> PickDocumentAsync(CancellationToken ct = default);

    /// <summary>Shows a multi-select open dialog for PDFs; returns the chosen paths
    /// (empty if cancelled).</summary>
    Task<IReadOnlyList<string>> PickDocumentsAsync(CancellationToken ct = default);

    /// <summary>Shows a save dialog for a PDF; returns the chosen path, or null if cancelled.</summary>
    Task<string?> PickSavePdfAsync(string suggestedName, CancellationToken ct = default);

    /// <summary>Shows a folder-picker; returns the chosen folder, or null if cancelled.</summary>
    Task<string?> PickFolderAsync(CancellationToken ct = default);

    /// <summary>Shows a multi-select open dialog for images (PNG/JPG/etc.); returns the
    /// chosen paths (empty if cancelled).</summary>
    Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken ct = default);
}
