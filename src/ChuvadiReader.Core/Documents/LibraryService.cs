using Chuvadi.Pdf.Documents;

namespace ChuvadiReader.Core.Documents;

/// <summary>One PDF found in the library. <see cref="Pages"/> is filled lazily by
/// a background pass (null = not counted yet, -1 = unreadable). <see cref="Category"/>
/// is derived from the file's category subfolder, or null if uncategorised.</summary>
public sealed class LibraryEntry
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset Modified { get; init; }
    public string? Category { get; set; }
    public string? CategoryColor { get; set; }
    public int? Pages { get; set; }
}

/// <summary>
/// Lists the PDFs under the Chuvadi Documents folder (recursively, so category
/// subfolders are included) for the library and storage map. Each file's category
/// is derived from its folder. A file-system watcher re-scans automatically when
/// files are added, removed, or renamed, so the dashboard and library stay live.
/// </summary>
public sealed class LibraryService : IDisposable
{
    private readonly SaveFolderService _saveFolders;
    private readonly CategoryService _categories;
    private readonly List<LibraryEntry> _entries = new();
    private CancellationTokenSource? _pageScan;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;

    public LibraryService(SaveFolderService saveFolders, CategoryService categories)
    {
        _saveFolders = saveFolders;
        _categories = categories;
        _saveFolders.Changed += OnBaseFolderChanged;
    }

    public string FolderPath => _saveFolders.BaseFolder;

    public IReadOnlyList<LibraryEntry> Entries => _entries;

    public bool IsScanning { get; private set; }

    public event Action? Changed;

    public async Task HydrateAsync()
    {
        _categories.EnsureFolders();
        SetupWatcher();
        await ScanAsync();
    }

    private void OnBaseFolderChanged()
    {
        _categories.EnsureFolders();
        SetupWatcher();
        _ = ScanAsync();
    }

    public async Task ScanAsync()
    {
        _pageScan?.Cancel();
        IsScanning = true;
        Changed?.Invoke();

        var folder = FolderPath;
        var found = await Task.Run(() =>
        {
            var list = new List<LibraryEntry>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        var cat = _categories.CategoryFor(f);
                        list.Add(new LibraryEntry
                        {
                            Path = f,
                            Name = fi.Name,
                            SizeBytes = fi.Length,
                            Modified = fi.LastWriteTime,
                            Category = cat?.Name,
                            CategoryColor = cat?.Color,
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return list.OrderByDescending(e => e.Modified).ToList();
        });

        _entries.Clear();
        _entries.AddRange(found);
        IsScanning = false;
        Changed?.Invoke();

        _ = FillPagesAsync();
    }

    private async Task FillPagesAsync()
    {
        _pageScan = new CancellationTokenSource();
        var ct = _pageScan.Token;
        var since = 0;
        foreach (var entry in _entries.ToList())
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (entry.Pages is not null)
            {
                continue;
            }

            try
            {
                using var doc = await PdfDocument.OpenAsync(entry.Path, ct).ConfigureAwait(false);
                entry.Pages = doc.PageCount;
            }
            catch
            {
                entry.Pages = -1;
            }

            if (++since >= 6)
            {
                since = 0;
                Changed?.Invoke();
            }
        }

        Changed?.Invoke();
    }

    private void SetupWatcher()
    {
        try { _watcher?.Dispose(); } catch { }
        _watcher = null;
        try
        {
            var folder = FolderPath;
            Directory.CreateDirectory(folder);
            var w = new FileSystemWatcher(folder, "*.pdf")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            w.Created += OnFsEvent;
            w.Deleted += OnFsEvent;
            w.Renamed += OnFsEvent;
            w.Changed += OnFsEvent;
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch
        {
            // a watcher is best-effort; manual navigation still re-scans
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => Debounce();

    private void Debounce()
    {
        _debounce ??= new System.Threading.Timer(_ => _ = ScanAsync(), null,
            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _debounce.Change(500, System.Threading.Timeout.Infinite);
    }

    public void Dispose()
    {
        _saveFolders.Changed -= OnBaseFolderChanged;
        try { _watcher?.Dispose(); } catch { }
        try { _debounce?.Dispose(); } catch { }
    }
}
