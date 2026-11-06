namespace ChuvadiReader.Core.Reader;

/// <summary>One "open this document" request: the file, the tab name and an optional 1-based page.</summary>
public sealed record PendingOpen(string Path, string Name, int? Page);

/// <summary>
/// Carries "open this document" requests from the dashboard (or a drop, an upload, Open with)
/// to the Reader page across navigation. Requests queue in order, so several made before the
/// Reader runs each open in their own tab; the Reader takes them one at a time.
/// </summary>
public sealed class OpenDocumentService
{
    private readonly object _gate = new();
    private readonly Queue<PendingOpen> _queue = new();

    /// <summary>The next request's path, or null when nothing is waiting.</summary>
    public string? PendingPath => Peek()?.Path;

    /// <summary>The next request's tab name, or null when nothing is waiting.</summary>
    public string? PendingName => Peek()?.Name;

    /// <summary>Optional 1-based page to jump to once the next document opens.</summary>
    public int? PendingPage => Peek()?.Page;

    /// <summary>Raised after each request (e.g. a file dropped on the window),
    /// so the shell can navigate to the Reader.</summary>
    public event Action? Requested;

    public void Request(string path)
        => Request(path, System.IO.Path.GetFileName(path));

    public void Request(string path, string name)
        => Request(path, name, null);

    public void Request(string path, string name, int? page)
    {
        lock (_gate)
        {
            _queue.Enqueue(new PendingOpen(path, name, page));
        }

        Requested?.Invoke();
    }

    /// <summary>Takes the oldest waiting request. Each request is taken exactly once.</summary>
    public bool TryTake(out PendingOpen open)
    {
        lock (_gate)
        {
            return _queue.TryDequeue(out open!);
        }
    }

    /// <summary>Drops every waiting request.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _queue.Clear();
        }
    }

    private PendingOpen? Peek()
    {
        lock (_gate)
        {
            return _queue.TryPeek(out var next) ? next : null;
        }
    }
}
