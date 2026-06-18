namespace ChuvadiReader.Core.Reader;

/// <summary>
/// Carries the "open this document" request from the dashboard to the Reader
/// page across navigation. The Reader consumes it on load and clears it.
/// </summary>
public sealed class OpenDocumentService
{
    public string? PendingPath { get; private set; }

    public string? PendingName { get; private set; }

    /// <summary>Optional 1-based page to jump to once the document opens.</summary>
    public int? PendingPage { get; private set; }

    /// <summary>Raised when a new open is requested (e.g. a file dropped on the window),
    /// so the shell can navigate to the Reader.</summary>
    public event Action? Requested;

    public void Request(string path)
        => Request(path, System.IO.Path.GetFileName(path));

    public void Request(string path, string name)
        => Request(path, name, null);

    public void Request(string path, string name, int? page)
    {
        PendingPath = path;
        PendingName = name;
        PendingPage = page;
        Requested?.Invoke();
    }

    public void Clear()
    {
        PendingPath = null;
        PendingName = null;
        PendingPage = null;
    }
}
