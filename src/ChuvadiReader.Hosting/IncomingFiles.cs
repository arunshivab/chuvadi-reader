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
/// D-164: a file that arrives before the Ui is ready is not held back — its open is requested at once
/// (<see cref="OpenDocumentService"/> keeps requests in arrival order), so the Ui starts on the Reader and the
/// document loads while the rest of the shell does. Only <see cref="Delivered"/> (bring the window to the
/// front) waits for the Ui's first render, when there is a window to bring.
/// </summary>
public sealed class IncomingFiles
{
    private readonly BenchDropService _bench;
    private readonly OpenDocumentService _open;
    private readonly object _gate = new();
    private bool _ready;
    private bool _deliveredBeforeReady;

    public IncomingFiles(BenchDropService bench, OpenDocumentService open, IAppReadySignal ready, IAppInstance instance)
    {
        _bench = bench;
        _open = open;
        _ready = ready.IsReady;
        if (!_ready)
        {
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
        bool early;
        lock (_gate)
        {
            early = !_ready;
            if (early && files.Count > 0)
            {
                _deliveredBeforeReady = true;   // as before: a launch without a file brings nothing forward
            }
        }

        if (early)
        {
            // Before the Ui exists nothing is on the Bench, so each file is a Reader tab. The shell starts on the
            // Reader when a request is waiting (MainLayout), and the Reader takes them in order.
            foreach (var f in files)
            {
                _open.Request(f);
            }

            return files.Count;
        }

        Deliver(files);
        return files.Count;
    }

    private void Release()
    {
        bool raise;
        lock (_gate)
        {
            _ready = true;
            raise = _deliveredBeforeReady;
            _deliveredBeforeReady = false;
        }

        if (raise)
        {
            Delivered?.Invoke();
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
