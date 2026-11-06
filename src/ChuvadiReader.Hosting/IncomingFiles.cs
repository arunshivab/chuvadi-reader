using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Lifecycle;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Platform;

namespace ChuvadiReader.Hosting;

/// <summary>
/// One route for every file that arrives from outside the app: the command line at launch, a launch
/// handed over to the running copy (<see cref="IAppInstance"/>), a drop on the window, macOS open-file,
/// an Android intent. HANDOFF §2: to the Bench shelf when the Bench is open, otherwise each file in
/// its own Reader tab, in order. Only existing files of a type the Open dialog accepts get through.
/// Files that arrive before the Ui's first render wait until it, then go in arrival order.
/// </summary>
public sealed class IncomingFiles
{
    private readonly BenchDropService _bench;
    private readonly OpenDocumentService _open;
    private readonly object _gate = new();
    private List<string>? _waiting;

    public IncomingFiles(BenchDropService bench, OpenDocumentService open, IAppReadySignal ready, IAppInstance instance)
    {
        _bench = bench;
        _open = open;
        if (!ready.IsReady)
        {
            _waiting = new List<string>();
            ready.Ready += Release;
        }

        instance.FilesReceived += files => Receive(files);
    }

    /// <summary>Raised after files were delivered (or an empty hand-over arrived), so the host can
    /// bring its window to the front. Raised on the caller's thread.</summary>
    public event Action? Delivered;

    /// <summary>Keeps only existing files of a supported type, as typed paths, in order.</summary>
    public static IReadOnlyList<string> Supported(IEnumerable<string> paths) =>
        paths.Where(p => !string.IsNullOrWhiteSpace(p) && DocumentFormatExtensions.IsOpenable(p) && File.Exists(p))
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToArray();

    /// <summary>Routes <paramref name="paths"/>. Returns how many were accepted.</summary>
    public int Receive(IEnumerable<string> paths)
    {
        var files = Supported(paths);
        lock (_gate)
        {
            if (_waiting is not null)
            {
                _waiting.AddRange(files);
                return files.Count;
            }
        }

        Deliver(files);
        return files.Count;
    }

    private void Release()
    {
        List<string> pending;
        lock (_gate)
        {
            pending = _waiting ?? new List<string>();
            _waiting = null;
        }

        if (pending.Count > 0)
        {
            Deliver(pending);
        }
    }

    private void Deliver(IReadOnlyList<string> files)
    {
        if (files.Count > 0)
        {
            if (_bench.IsBenchActive)
            {
                _bench.Drop(files);
            }
            else
            {
                foreach (var f in files)
                {
                    _open.Request(f);
                }
            }
        }

        Delivered?.Invoke();
    }
}
