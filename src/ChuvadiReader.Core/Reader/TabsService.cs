using System.Collections.Generic;
using System.Text.Json;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Storage;

namespace ChuvadiReader.Core.Reader;

/// <summary>One open tab: a document plus the view-state we remember for it.</summary>
public sealed class ReaderTab
{
    public required string Id { get; init; }

    public required string Path { get; set; }

    public required string FileName { get; set; }

    public DocumentFormat Format { get; set; }

    /// <summary>The open document, or null until the tab is first activated (lazy restore).</summary>
    public IPdfSession? Session { get; set; }

    public bool IsLoaded => Session is not null || Format.IsViewOnly();

    public bool IsLoading { get; set; }

    /// <summary>Cancels this tab's PDF open while it is loading (B-24); closing the tab cancels it.</summary>
    internal CancellationTokenSource? OpenCancel { get; set; }

    public string? Error { get; set; }

    /// <summary>The PDF is password-protected and waits for its password (B-07).</summary>
    public bool NeedsPassword { get; set; }

    /// <summary>The password that opened this tab's PDF. Memory only, for this tab (as Word/Excel); never persisted.</summary>
    public string? Password { get; set; }

    /// <summary>Set when the last password tried didn't open the file.</summary>
    public string? PasswordError { get; set; }

    /// <summary>The PDF couldn't be parsed (damaged or not really a PDF) — B-11. <see cref="Error"/> holds the
    /// plain message; <see cref="ErrorDetail"/> the library's own text, for "Details".</summary>
    public bool IsDamaged { get; set; }

    public string? ErrorDetail { get; set; }

    // Per-tab view-state, persisted so switching (and restarting) lands where you left off.
    /// <summary>The page in view, 1-based as shown ("page 1 of 13").</summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>The page in view as a zero-based index, for code that indexes pages (print "Current",
    /// Measure, Layers, Signatures, Password cards). WT-02: passing <see cref="CurrentPage"/> there printed
    /// the next page.</summary>
    public int CurrentPageIndex => Math.Max(CurrentPage - 1, 0);

    public double Scale { get; set; } = 1.0;

    public double ScrollTop { get; set; }

    // Per-tab view-state (in-session memory; resets to defaults on restart).
    public ReaderFitMode FitMode { get; set; } = ReaderFitMode.Free;

    /// <summary>Two-page (facing) layout when true; single page when false.</summary>
    public bool TwoPage { get; set; }

    /// <summary>Free continuous scrolling when true; paged snap when false.</summary>
    public bool Continuous { get; set; }

    /// <summary>Per-page view rotation in degrees (0/90/180/270), keyed by 0-based page index. View-only; never written to the file.</summary>
    public Dictionary<int, int> PageRotations { get; } = new();

    public bool ScrollLocked { get; set; }

    /// <summary>In two-page layout, show the first page on its own so spreads line up. Off by default.</summary>
    public bool CoverAlone { get; set; }

    /// <summary>Per-tab watermark config for the Stamp panel. Lives with the tab (not persisted
    /// across app restarts), so each open file keeps its own pending stamp until the tab closes.</summary>
    public DeskWatermark Watermark { get; set; } = new()
    {
        Text = "", FontFamily = "Helvetica", FontSize = 48,
        ColorHex = "#808080", Opacity = 0.25, RotationDegrees = 45, AllPages = true,
    };

    /// <summary>Per-tab header/footer config for the Stamp panel. Starts blank (no bands enabled).</summary>
    public DeskHeaderFooter HeaderFooter { get; set; } = new()
    {
        HeaderEnabled = false, FooterEnabled = false, AllPages = true, Fit = "overlay",
    };

    /// <summary>Whether the Stamp drawer is open for this tab.</summary>
    public bool StampOpen { get; set; }
}

/// <summary>
/// Owns the set of open documents (tabs) and which one is active. Documents are
/// deduplicated by path, restored lazily on startup (only the active tab opens
/// immediately), and background tabs keep their handle open but drop their render
/// cache. Everything here is host-agnostic; persistence goes through IAppStorage.
/// </summary>
public sealed class TabsService
{
    private const string TabsKey = "tabs";
    private const string ActiveKey = "tabs-active";

    private readonly IPdfReader _reader;
    private readonly IAppStorage _storage;
    private readonly List<ReaderTab> _tabs = new();

    // Opens overlap and continue on the thread pool (ConfigureAwait(false)), so the tab list is read off the Ui
    // thread (DropOtherCaches, PersistAsync) while another open adds to it. Every change to the list holds this
    // lock, and those readers walk a copy taken under it (Mac smoke, 10 Nov: "Collection was modified").
    private readonly object _tabsGate = new();

    private ReaderTab[] TabsSnapshot()
    {
        lock (_tabsGate)
        {
            return _tabs.ToArray();
        }
    }
    private bool _restored;

    public TabsService(IPdfReader reader, IAppStorage storage)
    {
        _reader = reader;
        _storage = storage;
    }

    public IReadOnlyList<ReaderTab> Tabs => _tabs;

    public ReaderTab? Active { get; private set; }

    /// <summary>Raised whenever the tab set, the active tab, or a tab's load-state changes.</summary>
    public event Action? Changed;

    /// <summary>Open a document, or focus its existing tab if the same path is already open.</summary>
    public async Task<ReaderTab> OpenAsync(string path, string fileName, DocumentFormat format, CancellationToken ct = default)
    {
        var existing = _tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            await ActivateAsync(existing.Id, ct).ConfigureAwait(false);
            return existing;
        }

        var tab = new ReaderTab
        {
            Id = Guid.NewGuid().ToString("N"),
            Path = path,
            FileName = fileName,
            Format = format,
        };
        lock (_tabsGate)
        {
            _tabs.Add(tab);
        }
        Active = tab;
        Changed?.Invoke();

        await EnsureLoadedAsync(tab, ct).ConfigureAwait(false);
        if (ReferenceEquals(Active, tab))
        {
            DropOtherCaches(tab); // B-24: opens can overlap now; never drop the newer active tab's cache
        }

        await PersistAsync(ct).ConfigureAwait(false);
        return tab;
    }

    /// <summary>Make a tab active, opening its document if it hasn't been opened yet.</summary>
    public async Task ActivateAsync(string id, CancellationToken ct = default)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is null)
        {
            return;
        }

        Active = tab;
        Changed?.Invoke();

        await EnsureLoadedAsync(tab, ct).ConfigureAwait(false);
        DropOtherCaches(tab);
        await PersistAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Release the active tab's open document (so the file can be replaced on disk),
    /// run <paramref name="afterRelease"/>, then reopen the document in place — same tab, same
    /// page. Used by in-place "Save" so the overwritten file is reloaded cleanly.</summary>
    public async Task ReloadActiveAsync(Func<Task>? afterRelease = null, CancellationToken ct = default)
    {
        var tab = Active;
        if (tab is null)
        {
            return;
        }

        var keepPage = tab.CurrentPage;
        tab.Session?.Dispose();
        tab.Session = null; // IsLoaded => false, so EnsureLoadedAsync reopens

        try
        {
            if (afterRelease is not null)
            {
                await afterRelease().ConfigureAwait(false);
            }
        }
        finally
        {
            await EnsureLoadedAsync(tab, ct).ConfigureAwait(false);
            if (tab.Session is not null && keepPage >= 1 && keepPage <= tab.Session.Document.PageCount)
            {
                tab.CurrentPage = keepPage;
            }

            DropOtherCaches(tab);
            Changed?.Invoke();
            await PersistAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Close a tab, disposing its document and activating a neighbour.</summary>
    public async Task CloseAsync(string id, CancellationToken ct = default)
    {
        var idx = _tabs.FindIndex(t => t.Id == id);
        if (idx < 0)
        {
            return;
        }

        var tab = _tabs[idx];
        Release(tab);
        lock (_tabsGate)
        {
            _tabs.RemoveAt(idx);
        }

        if (ReferenceEquals(Active, tab))
        {
            Active = _tabs.Count == 0 ? null : _tabs[Math.Min(idx, _tabs.Count - 1)];
            if (Active is not null)
            {
                await EnsureLoadedAsync(Active, ct).ConfigureAwait(false);
            }
        }

        Changed?.Invoke();
        await PersistAsync(ct).ConfigureAwait(false);
    }

    public Task CloseActiveAsync(CancellationToken ct = default)
        => Active is null ? Task.CompletedTask : CloseAsync(Active.Id, ct);

    /// <summary>Close every tab except the active one.</summary>
    public async Task CloseOthersAsync(CancellationToken ct = default)
    {
        if (Active is null)
        {
            return;
        }

        var keep = Active;
        foreach (var tab in _tabs)
        {
            if (!ReferenceEquals(tab, keep))
            {
                Release(tab);
            }
        }

        lock (_tabsGate)
        {
            _tabs.RemoveAll(t => !ReferenceEquals(t, keep));
        }
        Changed?.Invoke();
        await PersistAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Close all tabs.</summary>
    public async Task CloseAllAsync(CancellationToken ct = default)
    {
        foreach (var tab in _tabs)
        {
            Release(tab);
        }

        lock (_tabsGate)
        {
            _tabs.Clear();
        }
        Active = null;
        Changed?.Invoke();
        await PersistAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Cycle the active tab (+1 next, -1 previous), wrapping around.</summary>
    public Task CycleAsync(int direction, CancellationToken ct = default)
    {
        if (_tabs.Count < 2 || Active is null)
        {
            return Task.CompletedTask;
        }

        var i = _tabs.IndexOf(Active);
        var j = ((i + direction) % _tabs.Count + _tabs.Count) % _tabs.Count;
        return ActivateAsync(_tabs[j].Id, ct);
    }

    /// <summary>Move a tab to a new index (drag-to-reorder).</summary>
    public async Task ReorderAsync(string id, int toIndex, CancellationToken ct = default)
    {
        var from = _tabs.FindIndex(t => t.Id == id);
        if (from < 0)
        {
            return;
        }

        toIndex = Math.Clamp(toIndex, 0, _tabs.Count - 1);
        if (toIndex == from)
        {
            return;
        }

        var tab = _tabs[from];
        lock (_tabsGate)
        {
            _tabs.RemoveAt(from);
            _tabs.Insert(toIndex, tab);
        }

        Changed?.Invoke();
        await PersistAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Move a tab into a gap. <paramref name="insertIndex"/> is the gap position
    /// in the current ordering (0..Count): 0 = before the first tab, Count = after the
    /// last. This is the drag-drop entry point, where the drop indicator sits between tabs.</summary>
    public async Task MoveToAsync(string id, int insertIndex, CancellationToken ct = default)
    {
        var from = _tabs.FindIndex(t => t.Id == id);
        if (from < 0)
        {
            return;
        }

        // The gap index counts the dragged tab; once it's removed, gaps to its right shift left.
        var to = insertIndex;
        if (to > from)
        {
            to--;
        }

        to = Math.Clamp(to, 0, _tabs.Count - 1);
        if (to == from)
        {
            return;
        }

        var tab = _tabs[from];
        lock (_tabsGate)
        {
            _tabs.RemoveAt(from);
            _tabs.Insert(to, tab);
        }

        Changed?.Invoke();
        await PersistAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Remember a tab's reading position (called as the user reads/scrolls).</summary>
    public void UpdateViewState(string id, int currentPage, double scale, double scrollTop)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is null)
        {
            return;
        }

        tab.CurrentPage = currentPage;
        tab.Scale = scale;
        tab.ScrollTop = scrollTop;
    }

    /// <summary>Persist the current view-state to disk (called on switch/close/leave).</summary>
    public Task SaveAsync(CancellationToken ct = default) => PersistAsync(ct);

    /// <summary>Rebuild the tab strip from the last session. Idempotent: only runs once.
    /// Tabs are created unloaded; only the active tab's document opens now.</summary>
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        if (_restored)
        {
            return;
        }

        _restored = true;

        var json = await _storage.GetAsync(TabsKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        List<TabDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<TabDto>>(json);
        }
        catch (JsonException)
        {
            return;
        }

        if (dtos is null || dtos.Count == 0)
        {
            return;
        }

        var activePath = await _storage.GetAsync(ActiveKey, ct).ConfigureAwait(false);

        foreach (var d in dtos)
        {
            // Bug fix (D-48): a host may open a tab before the first restore; never show a file twice.
            if (_tabs.Any(t => string.Equals(t.Path, d.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var restored = new ReaderTab
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = d.Path,
                FileName = d.FileName,
                Format = d.Format,
                CurrentPage = d.Page < 1 ? 1 : d.Page,
                Scale = d.Scale <= 0 ? 1.0 : d.Scale,
                ScrollTop = d.ScrollTop,
            };
            lock (_tabsGate)
            {
                _tabs.Add(restored);
            }
        }

        Active = _tabs.FirstOrDefault(t => string.Equals(t.Path, activePath, StringComparison.OrdinalIgnoreCase))
                 ?? _tabs.FirstOrDefault();
        Changed?.Invoke();

        if (Active is not null)
        {
            await EnsureLoadedAsync(Active, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Drops a tab's document, and stops waiting for it if it is still opening (B-24).</summary>
    private static void Release(ReaderTab tab)
    {
        try
        {
            tab.OpenCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // the open just finished
        }

        tab.Session?.Dispose();
    }

    /// <summary>An abandoned open may still complete later; its document is closed then, never shown.</summary>
    private static void DisposeWhenDone(Task<IPdfSession> open) =>
        _ = open.ContinueWith(
            t => t.Result.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>Shown under the password field when the password given doesn't open the PDF (B-07).</summary>
    public const string WrongPassword = "That password doesn't open this file.";

    /// <summary>Shown when a document can't be opened for a reason other than damage, a password or access (WT-10).</summary>
    public const string OpenFailed = "This file is damaged or isn't a PDF Chuvadi can read.";

    /// <summary>WT-10: a file that can't be read (permissions, in use, a missing drive), for the Reader and the viewers.</summary>
    public const string CantRead = "Chuvadi couldn't read this file. It may be open in another app, or you may not have permission to read it.";

    private async Task EnsureLoadedAsync(ReaderTab tab, CancellationToken ct)
    {
        if (tab.IsLoaded || tab.IsLoading)
        {
            return;
        }

        if (tab.Format.IsViewOnly())
        {
            // Word / Excel / Markdown / text / HTML tabs are rendered by their own viewers
            // (D-07, D-09); there is no PDF session to open.
            return;
        }

        tab.IsLoading = true;
        tab.Error = null;
        tab.ErrorDetail = null;
        tab.IsDamaged = false;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tab.OpenCancel = cancel;
        Changed?.Invoke(); // "Opening…" shows now, before anything touches the file

        // B-24: the file is touched only on a pool thread. A source that never delivers data (a hung
        // network mount, an unplugged USB stick, a FIFO) can block even File.Exists or open(2) — the
        // library's open runs synchronously until its first await — and on the caller's thread that
        // froze the Reader and every later open. Closing the tab cancels the wait at once; the stuck
        // read finishes (or not) on its own, and anything it opens later is disposed (DisposeWhenDone).
        var path = tab.Path;
        var password = tab.Password;
        var token = cancel.Token;
        var open = Task.Run(
            async () =>
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(null, path);
                }

                return await _reader.OpenAsync(path, tab.FileName, password, token, transparentBackground: true).ConfigureAwait(false);
            },
            CancellationToken.None);

        try
        {
            var session = await open.WaitAsync(cancel.Token).ConfigureAwait(false);
            if (!_tabs.Contains(tab))
            {
                session.Dispose(); // B-08b: the tab was closed ("Stop opening") while it opened
                return;
            }

            tab.Session = session;
            tab.NeedsPassword = false;
            tab.PasswordError = null;
            if (tab.CurrentPage > tab.Session.Document.PageCount)
            {
                tab.CurrentPage = 1;
                tab.ScrollTop = 0;
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            DisposeWhenDone(open); // B-24: stopped while the file was still being read
        }
        catch (FileNotFoundException)
        {
            tab.Error = $"File not found: {tab.Path}";
        }
        catch (Chuvadi.Pdf.Primitives.PdfPasswordRequiredException ex)
        {
            // B-07: ask for the password. The library says whether one was given (PasswordSupplied, 3.24+):
            // none → just the prompt; a wrong one → say so. (No RequiresPassword pre-check: it costs as much
            // as opening a plain PDF, and on a locked file more than this failed open.)
            tab.PasswordError = ex.PasswordSupplied ? WrongPassword : null;
            tab.Password = null;
            tab.NeedsPassword = true;
        }
        catch (Exception ex) when (ex is Chuvadi.Pdf.Primitives.PdfParseException or Chuvadi.Pdf.Primitives.PdfCorruptionException)
        {
            // B-11: the handout's damaged-file state, not the parser's text (kept under Details).
            tab.IsDamaged = true;
            tab.Error = "This PDF is damaged";
            tab.ErrorDetail = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // WT-10: a plain sentence; the system's own text stays under Details.
            tab.Error = CantRead;
            tab.ErrorDetail = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // WT-10 / B-11b: the parser's text (e.g. "Invalid integer '4294965240' at offset 3927…") is detail.
            tab.Error = OpenFailed;
            tab.ErrorDetail = ex.Message;
        }
        finally
        {
            tab.OpenCancel = null;
            tab.IsLoading = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Opens a password-protected PDF tab with <paramref name="password"/> (kept in memory for this tab only).</summary>
    public async Task UnlockAsync(string id, string password, CancellationToken ct = default)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is null || !tab.NeedsPassword)
        {
            return;
        }

        tab.Password = password;
        await EnsureLoadedAsync(tab, ct).ConfigureAwait(false);
        if (tab.Session is not null)
        {
            DropOtherCaches(tab);
            await PersistAsync(ct).ConfigureAwait(false);
        }
    }

    private void DropOtherCaches(ReaderTab keep)
    {
        foreach (var t in TabsSnapshot())
        {
            if (!ReferenceEquals(t, keep))
            {
                t.Session?.DropRenderCache();
            }
        }
    }

    // Saves go one at a time, each with the tab list as it is when its turn comes. Overlapping opens each save from
    // their own thread; side by side, an older list could be written after a newer one (a tab missing at the next
    // start), and a store that isn't built for two writers at once is never given two (Windows runner, 11 Nov).
    private readonly SemaphoreSlim _persistGate = new(1, 1);

    private async Task PersistAsync(CancellationToken ct)
    {
        await _persistGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dto = TabsSnapshot().Select(t => new TabDto
            {
                Path = t.Path,
                FileName = t.FileName,
                Format = t.Format,
                Page = t.CurrentPage,
                Scale = t.Scale,
                ScrollTop = t.ScrollTop,
            }).ToList();

            await _storage.SetAsync(TabsKey, JsonSerializer.Serialize(dto), ct).ConfigureAwait(false);
            await _storage.SetAsync(ActiveKey, Active?.Path ?? string.Empty, ct).ConfigureAwait(false);
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private sealed class TabDto
    {
        public string Path { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public DocumentFormat Format { get; set; }

        public int Page { get; set; } = 1;

        public double Scale { get; set; } = 1.0;

        public double ScrollTop { get; set; }
    }
}
