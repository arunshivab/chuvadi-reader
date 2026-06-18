namespace ChuvadiReader.Core.Reader;

/// <summary>
/// Carries a "send this document to Redact" request from the Reader to the Redact
/// destination across navigation. Redact consumes it on load and clears it. Mirrors
/// <see cref="OpenDocumentService"/>.
/// </summary>
public sealed class RedactRequestService
{
    public string? PendingPath { get; private set; }

    public string? PendingName { get; private set; }

    public void Request(string path)
        => Request(path, System.IO.Path.GetFileName(path));

    public void Request(string path, string name)
    {
        PendingPath = path;
        PendingName = name;
    }

    public void Clear()
    {
        PendingPath = null;
        PendingName = null;
    }
}
