namespace ChuvadiReader.Core.Reader;

/// <summary>
/// Routes an Explorer file-drop to the right place. The Bench page marks itself active while it is
/// mounted; the WPF host checks <see cref="IsBenchActive"/> on a drop and, when the Bench is open,
/// hands the dropped paths here (added to the shelf) instead of opening them in the Reader.
/// A singleton, so the host and the Blazor app share one instance.
/// </summary>
public sealed class BenchDropService
{
    /// <summary>True while the Bench page is mounted and should receive dropped files.</summary>
    public bool IsBenchActive { get; private set; }

    /// <summary>Raised when files are dropped while the Bench is active. The Bench page subscribes,
    /// adds the PDFs to the shelf, and refreshes.</summary>
    public event Action<IReadOnlyList<string>>? FilesDropped;

    public void SetBenchActive(bool active) => IsBenchActive = active;

    public void Drop(IReadOnlyList<string> paths) => FilesDropped?.Invoke(paths);
}
