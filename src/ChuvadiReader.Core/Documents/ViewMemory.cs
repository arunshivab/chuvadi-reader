using ChuvadiReader.Core.Reader;

namespace ChuvadiReader.Core.Documents;

/// <summary>
/// WT-19: memory relief for the view-only formats. Closing a heavy Excel or Word file (or stopping its open) left
/// the process holding what reading it had allocated — 0.5 to 1.3 GB on a Web worker — because the relief after a
/// heavy close covered PDFs only. A viewer reports what its view cost when it lets the view go; a heavy one asks for
/// the same relief (one settled collection, shared and spaced as for PDFs; never while reading or paging).
/// </summary>
public static class ViewMemory
{
    /// <param name="loadedBytes">What reading the file allocated (<c>WorkbookView.LoadedBytes</c>, <c>WordView.LoadedBytes</c>).
    /// Call after the view is no longer referenced.</param>
    public static void Released(long loadedBytes)
    {
        if (loadedBytes >= MemoryRelief.HeavyBytes)
        {
            MemoryRelief.Request();
        }
    }
}
