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

    public bool IsLoaded => Session is not null;

    public bool IsLoading { get; set; }

    public string? Error { get; set; }

    // Per-tab view-state, persisted so switching (and restarting) lands where you left off.
    public int CurrentPage { get; set; } = 1;

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
        _tabs.Add(tab);
        Active = tab;
        Changed?.Invoke();

        await EnsureLoadedAsync(tab, ct).ConfigureAwait(false);
        DropOtherCaches(tab);
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
        tab.Session?.Dispose();
        _tabs.RemoveAt(idx);

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
                tab.Session?.Dispose();
            }
        }

        _tabs.RemoveAll(t => !ReferenceEquals(t, keep));
        Changed?.Invoke();
        await PersistAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Close all tabs.</summary>
    public async Task CloseAllAsync(CancellationToken ct = default)
    {
        foreach (var tab in _tabs)
        {
            tab.Session?.Dispose();
        }

        _tabs.Clear();
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
        _tabs.RemoveAt(from);
        _tabs.Insert(toIndex, tab);

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
        _tabs.RemoveAt(from);
        _tabs.Insert(to, tab);

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
            _tabs.Add(new ReaderTab
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = d.Path,
                FileName = d.FileName,
                Format = d.Format,
                CurrentPage = d.Page < 1 ? 1 : d.Page,
                Scale = d.Scale <= 0 ? 1.0 : d.Scale,
                ScrollTop = d.ScrollTop,
            });
        }

        Active = _tabs.FirstOrDefault(t => string.Equals(t.Path, activePath, StringComparison.OrdinalIgnoreCase))
                 ?? _tabs.FirstOrDefault();
        Changed?.Invoke();

        if (Active is not null)
        {
            await EnsureLoadedAsync(Active, ct).ConfigureAwait(false);
        }
    }

    private async Task EnsureLoadedAsync(ReaderTab tab, CancellationToken ct)
    {
        if (tab.IsLoaded || tab.IsLoading)
        {
            return;
        }

        if (!File.Exists(tab.Path))
        {
            tab.Error = $"File not found: {tab.Path}";
            Changed?.Invoke();
            return;
        }

        tab.IsLoading = true;
        tab.Error = null;
        Changed?.Invoke();

        try
        {
            tab.Session = await _reader.OpenAsync(tab.Path, tab.FileName, null, ct, transparentBackground: true).ConfigureAwait(false);
            if (tab.CurrentPage > tab.Session.Document.PageCount)
            {
                tab.CurrentPage = 1;
                tab.ScrollTop = 0;
            }
        }
        catch (Exception ex)
        {
            tab.Error = $"Could not open this document. {ex.Message}";
        }
        finally
        {
            tab.IsLoading = false;
            Changed?.Invoke();
        }
    }

    private void DropOtherCaches(ReaderTab keep)
    {
        foreach (var t in _tabs)
        {
            if (!ReferenceEquals(t, keep))
            {
                t.Session?.DropRenderCache();
            }
        }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var dto = _tabs.Select(t => new TabDto
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
