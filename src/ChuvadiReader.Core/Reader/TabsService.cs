using System.Collections.Generic;
using System.Text.Json;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Storage;

namespace ChuvadiReader.Core.Reader;

/// <summary>The marks drawn on a tab's document that are not in the file yet (D-186 Q3 B). The Reader page owns
/// what they are; the tab keeps them, so they are still there after another tab or another page was shown.
/// Memory only: they go when the tab closes.</summary>
public interface ITabMarks : ITabUnsaved
{
}

/// <summary>Work on a tab's document that is not in a file yet and is kept with the tab, in memory (D-195):
/// marks, edited bookmarks, redaction areas. The page that makes it owns what it is.</summary>
public interface ITabUnsaved
{
    /// <summary>How many there are (marks, bookmarks, areas) that a save would write.</summary>
    int Count { get; }

    /// <summary>True when no save has written them yet.</summary>
    bool HasUnsaved { get; }

    /// <summary>Drops them (the person chose Discard).</summary>
    void Discard();
}

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

    /// <summary>This tab's unsaved marks, set by the Reader page; null until a mark is drawn on it.</summary>
    public ITabMarks? Marks { get; set; }

    /// <summary>True when closing this tab would lose marks (D-190).</summary>
    public bool HasUnsavedMarks => Marks?.HasUnsaved == true;

    /// <summary>D-195: this tab's bookmarks as edited and not yet saved, set by the Reader page.</summary>
    public ITabUnsaved? Bookmarks { get; set; }

    /// <summary>D-195: redaction areas drawn or found on this tab's file in Protect and not yet saved as a
    /// redacted copy, set by the Protect page.</summary>
    public ITabUnsaved? Redactions { get; set; }

    /// <summary>True when closing this tab would lose marks, bookmark edits or redaction areas.</summary>
    public bool HasUnsavedWork =>
        Marks?.HasUnsaved == true || Bookmarks?.HasUnsaved == true || Redactions?.HasUnsaved == true;

    /// <summary>HO16: the Reader has shown this tab once this run (fitted it to the window); showing it again keeps its
    /// place instead of starting again from page 1.</summary>
    public bool FittedOnce { get; set; }

    /// <summary>D-214: this Excel tab shows the workbook's hidden rows, columns and sheets (greyed). Off by default.</summary>
    public bool ShowHiddenParts { get; set; }

    /// <summary>Column widths and row heights set on screen for an Excel file in this tab (D-190). Viewing only.</summary>
    public SheetSizeMemory SheetSizes { get; } = new();

    public bool IsLoading { get; set; }

    /// <summary>Cancels this tab's PDF open while it is loading (B-24); closing the tab cancels it.</summary>
    internal CancellationTokenSource? OpenCancel { get; set; }

    public string? Error { get; set; }

    /// <summary>The PDF is password-protected and waits for its password (B-07).</summary>
    public bool NeedsPassword { get; set; }

    /// <summary>The password that opened this tab's PDF. Memory only, for this tab (as Word/Excel); never persisted.</summary>
    public string? Password { get; set; }

    /// <summary>D-207: the file's permissions (owner) password, when the person gave it so a change could keep the
    /// file's lock. Memory only, for this tab; never persisted.</summary>
    public string? OwnerPassword { get; set; }

    /// <summary>D-210: the source's size and time when this tab opened it; a save into the source checks it first.</summary>
    public FileStamp? Opened { get; set; }

    /// <summary>D-210: the source as it was when unsaved work began (marks, stamps), kept in memory: Save as writes
    /// from it, and Save into the source checks the file still matches it.</summary>
    public SourceCopy? WorkSource { get; set; }

    /// <summary>HO33: the app closed while opening this file last time; it waits for "Open it anyway".</summary>
    public bool HeldAfterCrash { get; set; }

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
public sealed class TabsService : IDisposable
{
    private const string TabsKey = "tabs";
    private const string ActiveKey = "tabs-active";
    /// <summary>HO33: the file being opened right now, cleared once it has opened (or failed to). Still set at the
    /// next start: the app closed while opening it, so its tab comes back without opening it by itself.</summary>
    private const string OpeningKey = "tabs-opening";

    public const string HeldAfterCrashText =
        "Chuvadi closed while opening this file last time, so it isn't opened again by itself.";

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
        // A copy taken under the lock: another open may be adding its tab on another thread right now (walking the
        // list itself threw "Collection was modified", rarely, in the overlapping-opens test).
        var existing = TabsSnapshot().FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>D-190, D-195: asked before tabs with unsaved marks, bookmark edits or redaction areas are closed. It gets those tabs and returns false when
    /// the person chose Cancel; marks they chose to save are saved by then. The shell sets it (it shows the card).
    /// Null: nothing is asked.</summary>
    public Func<IReadOnlyList<ReaderTab>, Task<bool>>? CloseGuard { get; set; }

    // ── work on a file that has no tab (D-195) ───────────────────────────────

    // Protect can open a file of its own, with no tab in the strip. Its redaction areas are kept here, under a
    // stand-in that never shows as a tab, so they survive leaving Protect and are asked about when the app closes.
    private readonly Dictionary<string, ReaderTab> _detached = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The file Protect last had open (it opens there again when Protect is shown with nothing handed to it).</summary>
    public string? ProtectPath { get; set; }

    /// <summary>Where work on this file is kept: its tab when it has one, otherwise a stand-in made for it.</summary>
    public ReaderTab WorkHolderFor(string path, string fileName)
    {
        lock (_tabsGate)
        {
            var tab = _tabs.Find(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
            if (tab is not null)
            {
                // The file got a tab after Protect had it: its areas move to the tab.
                if (_detached.Remove(path, out var was) && tab.Redactions is null) tab.Redactions = was.Redactions;
                return tab;
            }

            if (!_detached.TryGetValue(path, out var holder))
            {
                _detached[path] = holder = new ReaderTab { Id = "protect:" + Guid.NewGuid().ToString("N"), Path = path, FileName = fileName };
            }

            return holder;
        }
    }

    /// <summary>True for a stand-in from <see cref="WorkHolderFor"/> (a file with no tab in the strip).</summary>
    public bool IsDetached(ReaderTab tab)
    {
        lock (_tabsGate)
        {
            return _detached.TryGetValue(tab.Path, out var holder) && ReferenceEquals(holder, tab);
        }
    }

    private ReaderTab[] DetachedSnapshot()
    {
        lock (_tabsGate)
        {
            return _detached.Values.ToArray();
        }
    }

    /// <summary>Raised when a tab's marks became unsaved, or stopped being so (the Reader page says when).</summary>
    public event Action? UnsavedMarksChanged;

    /// <summary>The Reader page calls this when the tab in front gained its first unsaved mark or lost its last.</summary>
    public void NoteUnsavedMarksChanged() => UnsavedMarksChanged?.Invoke();

    /// <summary>True when any open tab has unsaved marks, bookmark edits or redaction areas.</summary>
    public bool HasUnsavedMarks
    {
        get
        {
            lock (_tabsGate)
            {
                return _tabs.Exists(t => t.HasUnsavedWork) || _detached.Values.Any(t => t.HasUnsavedWork);
            }
        }
    }

    /// <summary>Asks about these tabs' unsaved marks, if they have any. True: go ahead and close.</summary>
    public async Task<bool> ConfirmCloseAsync(IEnumerable<ReaderTab> tabs)
    {
        var unsaved = tabs.Where(t => t.HasUnsavedWork).ToList();
        if (unsaved.Count == 0 || CloseGuard is not { } guard)
        {
            return true;
        }

        if (Interlocked.Exchange(ref _confirming, 1) == 1)
        {
            return false; // the question is already on screen
        }

        var before = Active;
        try
        {
            bool go = await guard(unsaved).ConfigureAwait(false);

            // The question shows each file it asks about. When the answer is to go ahead, the tab that was in front
            // comes back; on Cancel the person stays on the file the question was about.
            if (go && before is not null && !ReferenceEquals(Active, before) && TabsSnapshot().Contains(before))
            {
                await ActivateAsync(before.Id).ConfigureAwait(false);
            }

            return go;
        }
        finally
        {
            Volatile.Write(ref _confirming, 0);
        }
    }

    /// <summary>The question before the whole app closes: every tab with unsaved marks, one after another. No tab
    /// is closed. True: the app may close.</summary>
    public Task<bool> ConfirmCloseAllAsync()
    {
        return ConfirmCloseAsync(TabsSnapshot().Concat(DetachedSnapshot()));
    }

    private int _confirming;

    /// <summary>Close a tab, disposing its document and activating a neighbour. A tab with unsaved marks is asked
    /// about first (<see cref="CloseGuard"/>) and stays open on Cancel.</summary>
    public async Task CloseAsync(string id, CancellationToken ct = default)
    {
        var asked = Array.Find(TabsSnapshot(), t => t.Id == id);
        if (asked is not null && !await ConfirmCloseAsync([asked]).ConfigureAwait(false))
        {
            return;
        }

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
        if (!await ConfirmCloseAsync(TabsSnapshot().Where(t => !ReferenceEquals(t, keep))).ConfigureAwait(false))
        {
            return;
        }

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
        if (!await ConfirmCloseAsync(TabsSnapshot()).ConfigureAwait(false))
        {
            return;
        }

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
        var crashedOn = await _storage.GetAsync(OpeningKey, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(crashedOn)) await SetOpeningAsync(string.Empty, ct).ConfigureAwait(false);

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
            if (!string.IsNullOrEmpty(crashedOn) && string.Equals(crashedOn, d.Path, StringComparison.OrdinalIgnoreCase))
            {
                restored.HeldAfterCrash = true;   // HO33: one click must not crash the app again
                restored.Error = HeldAfterCrashText;
            }

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

    /// <summary>WT-22: when the tabs' owner goes (on the Web portal a page reload or a dropped connection ends the
    /// scope; on a desktop the app is closing), every document they hold is closed, with its page images and memory.
    /// Before, the documents stayed open with nothing pointing at them. The saved tab list is left as it is, so the
    /// next start or page load brings the same tabs back.</summary>
    public void Dispose()
    {
        ReaderTab[] tabs;
        lock (_tabsGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            tabs = _tabs.ToArray();
            _tabs.Clear();
        }

        Active = null;
        foreach (var tab in tabs)
        {
            Release(tab);
        }
    }

    private bool _disposed;

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

    /// <summary>HO33: notes that <paramref name="tab"/>'s file is being opened now (viewer tabs call this as their
    /// viewer starts; PDF tabs here).</summary>
    public Task MarkOpeningAsync(ReaderTab tab, CancellationToken ct = default) =>
        tab.HeldAfterCrash ? Task.CompletedTask : SetOpeningAsync(tab.Path, ct);

    /// <summary>HO33: the file being opened has opened, or failed to without taking the app with it.</summary>
    public Task OpenedAsync(CancellationToken ct = default) => SetOpeningAsync(string.Empty, ct);

    // One write at a time with the tab list's own saves (the storage contract).
    private async Task SetOpeningAsync(string value, CancellationToken ct)
    {
        await _persistGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _storage.SetAsync(OpeningKey, value, ct).ConfigureAwait(false);
        }
        finally
        {
            _persistGate.Release();
        }
    }

    /// <summary>HO33: "Open it anyway" on a tab held after a crash.</summary>
    public async Task OpenHeldAsync(string id, CancellationToken ct = default)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is null || !tab.HeldAfterCrash) return;
        tab.HeldAfterCrash = false;
        tab.Error = null;
        Changed?.Invoke();
        await EnsureLoadedAsync(tab, ct).ConfigureAwait(false);
    }

    private async Task EnsureLoadedAsync(ReaderTab tab, CancellationToken ct)
    {
        if (tab.IsLoaded || tab.IsLoading || tab.HeldAfterCrash)
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
        await MarkOpeningAsync(tab, ct).ConfigureAwait(false);
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
        FileStamp? stamp = null;
        var open = Task.Run(
            async () =>
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(null, path);
                }

                stamp = FileStamp.Of(path);
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
            PdfPasswords.Remember(path, password, tab.OwnerPassword);   // BUG-19: every tool reuses it
            tab.Opened = stamp;
            tab.WorkSource = null;
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
            try { await OpenedAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
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
