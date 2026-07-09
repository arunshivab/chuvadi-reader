using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Watermark;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// The Bench workspace: a shelf of loaded source documents and a set of independent
/// <see cref="Desk"/>s. Pages are copied from the shelf into desks, arranged, and each
/// desk binds to its own PDF. A single instance is shared across the app so the
/// workspace survives navigating away and back.
/// </summary>
public sealed class BenchService
{
    private readonly IPdfReader _reader;
    private readonly BenchComposer _composer;
    private readonly ChuvadiReader.Core.Documents.ExportService _export;
    private readonly List<BenchSource> _sources = new();
    private readonly List<Desk> _desks = new();
    private readonly HashSet<Guid> _selected = new();
    private Guid? _selectionAnchor;
    private Guid? _activeDesk;

    // ── undo / redo history (desk + page edits only; shelf and selection excluded) ──
    private const int MaxHistory = 50;
    private readonly List<BenchSnapshot> _undo = new();
    private readonly List<BenchSnapshot> _redo = new();
    private BenchSnapshot? _baseline;   // state as of the last accepted edit (the next undo target)
    private string _baselineSig = "";   // structural signature of that state
    private bool _restoring;            // true while applying a snapshot, to suppress re-recording
    private readonly ChuvadiReader.Core.Documents.StampService _stamp = new();
    private readonly ChuvadiReader.Core.Documents.OutlineService _outline = new();

    public const int MaxDesks = 30;

    public BenchService(IPdfReader reader, BenchComposer composer, ChuvadiReader.Core.Documents.ExportService export)
    {
        _reader = reader;
        _composer = composer;
        _export = export;
        _baseline = TakeSnapshot();
        _baselineSig = DeskSignature();
    }

    public IReadOnlyList<BenchSource> Sources => _sources;

    public IReadOnlyList<Desk> Desks => _desks;

    public IReadOnlyCollection<Guid> Selected => _selected;

    public int SelectedCount => _selected.Count;

    public bool HasSources => _sources.Count > 0;

    public bool IsEmpty => _desks.All(d => d.IsEmpty);

    public event Action? Changed;

    /// <summary>Notify the UI. When a desk/page edit changed the structural signature (and we are
    /// not mid-restore), the pre-edit state is pushed onto the undo stack first. Selection-only and
    /// shelf-only changes leave the signature unchanged, so they never create history entries.</summary>
    private void Raise()
    {
        if (!_restoring)
        {
            var sig = DeskSignature();
            if (sig != _baselineSig)
            {
                if (_baseline is not null)
                {
                    _undo.Add(_baseline);
                    if (_undo.Count > MaxHistory)
                    {
                        _undo.RemoveAt(0);
                    }
                    _redo.Clear();
                }
                _baseline = TakeSnapshot();
                _baselineSig = sig;
            }
        }

        Changed?.Invoke();
    }

    // ── undo / redo ─────────────────────────────────────────────────────────────

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>The desk keyboard shortcuts act on (the one most recently selected into). Null
    /// when nothing is active.</summary>
    public Guid? ActiveDesk => _activeDesk is { } a && _desks.Any(d => d.Id == a) ? _activeDesk : _desks.FirstOrDefault()?.Id;

    public void SetActiveDesk(Guid? deskId) => _activeDesk = deskId;

    /// <summary>Steps back one desk/page edit. No-op when there is nothing to undo.</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var current = TakeSnapshot();
        var target = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        if (_redo.Count > MaxHistory)
        {
            _redo.RemoveAt(0);
        }

        ApplySnapshot(target);
    }

    /// <summary>Re-applies the edit most recently undone. No-op when there is nothing to redo.</summary>
    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var current = TakeSnapshot();
        var target = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveAt(0);
        }

        ApplySnapshot(target);
    }

    private void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _baseline = TakeSnapshot();
        _baselineSig = DeskSignature();
    }

    /// <summary>Captures the current desk arrangement + selection as an independent deep copy.</summary>
    private BenchSnapshot TakeSnapshot() => new(
        _desks.Select(d => d.Clone()).ToList(),
        new HashSet<Guid>(_selected),
        _selectionAnchor,
        _activeDesk);

    /// <summary>Restores a snapshot (re-cloned so the stored copy stays pristine) and re-bases the
    /// history baseline to the restored state, then notifies the UI once.</summary>
    private void ApplySnapshot(BenchSnapshot snap)
    {
        _restoring = true;
        try
        {
            _desks.Clear();
            _desks.AddRange(snap.Desks.Select(d => d.Clone()));
            _selected.Clear();
            foreach (var id in snap.Selected)
            {
                _selected.Add(id);
            }
            _selectionAnchor = snap.Anchor;
            _activeDesk = snap.ActiveDesk;
            _baseline = TakeSnapshot();
            _baselineSig = DeskSignature();
        }
        finally
        {
            _restoring = false;
        }

        Changed?.Invoke();
    }

    /// <summary>A structural fingerprint of everything the undo history tracks: desk order, names,
    /// colours, normalise/bookmarks, the per-desk stamp configs, and each page's id / rotation /
    /// crop / margins. Selection and shelf state are deliberately excluded.</summary>
    private string DeskSignature()
    {
        var sb = new System.Text.StringBuilder(256);
        foreach (var d in _desks)
        {
            sb.Append('D').Append(d.Id.ToString("N")).Append('|')
              .Append(d.Name).Append('|').Append(d.NameLocked ? '1' : '0').Append('|')
              .Append(d.NormalizeSize).Append('|').Append(d.ColorHex).Append('|')
              .Append(d.AddBookmarks ? '1' : '0').Append('|');

            if (d.Watermark is { } w)
            {
                sb.Append("W:").Append(w.Text).Append(';').Append(w.FontFamily).Append(';')
                  .Append(w.Bold ? '1' : '0').Append(w.Italic ? '1' : '0').Append(';')
                  .Append(w.FontSize).Append(';').Append(w.ColorHex).Append(';')
                  .Append(w.Opacity).Append(';').Append(w.RotationDegrees).Append(';')
                  .Append(w.AllPages ? '1' : '0').Append(';').Append(w.FromPage).Append(';').Append(w.ToPage).Append('|');
            }
            if (d.HeaderFooter is { } h)
            {
                sb.Append("H:").Append(h.HeaderLeft).Append(';').Append(h.HeaderCenter).Append(';').Append(h.HeaderRight).Append(';')
                  .Append(h.FooterLeft).Append(';').Append(h.FooterCenter).Append(';').Append(h.FooterRight).Append(';')
                  .Append(h.FontSize).Append(';').Append(h.ColorHex).Append(';').Append(h.Fit).Append(';')
                  .Append(h.BackgroundEnabled ? '1' : '0').Append(';').Append(h.BackgroundHex).Append(';')
                  .Append(h.BandHeight).Append(';').Append(h.MarginX).Append(';')
                  .Append(h.AllPages ? '1' : '0').Append(';').Append(h.FromPage).Append(';').Append(h.ToPage).Append('|');
            }
            if (d.Numbering is { } n)
            {
                sb.Append("N:").Append(n.Style).Append(';').Append(n.Prefix).Append(';').Append(n.Start).Append(';')
                  .Append(n.PadWidth).Append(';').Append(n.Position).Append(';').Append(n.FontSize).Append(';')
                  .Append(n.ColorHex).Append(';').Append(n.AllPages ? '1' : '0').Append(';')
                  .Append(n.FromPage).Append(';').Append(n.ToPage).Append(';').Append(n.FirstPage).Append('|');
            }

            foreach (var p in d.Pages)
            {
                sb.Append('p').Append(p.Id.ToString("N")).Append(':').Append(p.Rotation);
                if (p.Crop is { } c)
                {
                    sb.Append("c(").Append(c.X).Append(',').Append(c.Y).Append(',').Append(c.W).Append(',').Append(c.H).Append(')').Append(p.CropMode);
                }
                if (p.Margins is { } m)
                {
                    sb.Append("m(").Append(m.Top).Append(',').Append(m.Right).Append(',').Append(m.Bottom).Append(',').Append(m.Left).Append(')');
                }
                sb.Append(';');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // ── select-all / invert (per desk) ──────────────────────────────────────────

    /// <summary>Adds every page on the desk to the selection.</summary>
    public void SelectAllInDesk(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.Pages.Count == 0)
        {
            return;
        }

        foreach (var p in desk.Pages)
        {
            _selected.Add(p.Id);
        }
        _selectionAnchor = desk.Pages[^1].Id;
        _activeDesk = deskId;
        Raise();
    }

    /// <summary>Flips the selected state of every page on the desk.</summary>
    public void InvertSelectionInDesk(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.Pages.Count == 0)
        {
            return;
        }

        foreach (var p in desk.Pages)
        {
            if (!_selected.Remove(p.Id))
            {
                _selected.Add(p.Id);
            }
        }
        _activeDesk = deskId;
        Raise();
    }

    /// <summary>The two distinct pages currently selected (for the side-by-side compare), or null
    /// when the selection is not exactly two pages.</summary>
    public (BenchPage A, BenchPage B)? SelectedPair()
    {
        var pages = _desks.SelectMany(d => d.Pages).Where(p => _selected.Contains(p.Id)).ToList();
        return pages.Count == 2 ? (pages[0], pages[1]) : null;
    }

    /// <summary>Removes every selected page across all desks (the Delete shortcut).</summary>
    public void RemoveSelectedAll()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        var removed = 0;
        foreach (var desk in _desks)
        {
            removed += desk.Pages.RemoveAll(p => _selected.Contains(p.Id));
        }
        if (removed == 0)
        {
            return;
        }

        _selected.RemoveWhere(id => !_desks.Any(d => d.Pages.Any(p => p.Id == id)));
        Raise();
    }

    /// <summary>Rotates the selected pages on a desk by ±90° (whole desk if nothing is selected).</summary>
    public void RotateSelected(Guid deskId, bool clockwise)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var targets = desk.Pages.Where(p => _selected.Contains(p.Id)).ToList();
        if (targets.Count == 0)
        {
            targets = desk.Pages.ToList();
        }

        var delta = clockwise ? 90 : 270;
        foreach (var page in targets)
        {
            page.Rotation = (page.Rotation + delta) % 360;
        }
        Raise();
    }

    /// <summary>Duplicates every selected page on a desk in place (after each original).</summary>
    public void DuplicateSelected(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var ids = desk.Pages.Where(p => _selected.Contains(p.Id)).Select(p => p.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        foreach (var id in ids)
        {
            var idx = desk.Pages.FindIndex(p => p.Id == id);
            if (idx >= 0)
            {
                desk.Pages.Insert(idx + 1, ClonePage(desk.Pages[idx]));
            }
        }
        Raise();
    }

    // ── shelf ─────────────────────────────────────────────────────────────────

    /// <summary>Loads a PDF document onto the shelf. Does not place any pages — those are
    /// dragged into desks. Ensures one desk exists. (Images go straight into a desk via
    /// <see cref="AddImageToDesk"/>; they are not shelf sources.)</summary>
    public async Task AddSourceAsync(string path, string fileName, CancellationToken ct = default)
    {
        var existing = _sources.FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                                                    && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            var session = await _reader.OpenAsync(path, fileName, null, ct).ConfigureAwait(false);
            _sources.Add(new BenchSource
            {
                Index = (_sources.Count == 0 ? 0 : _sources.Max(s => s.Index) + 1),
                Path = path,
                FileName = fileName,
                Session = session,
            });
        }

        EnsureDesk();
        Raise();
    }

    /// <summary>Find-or-add a source by (file name, path) and return its stable index.
    /// Used by the Reader's "send this page to a desk" shortcut.</summary>
    public async Task<int> EnsureSourceAsync(string path, string fileName, CancellationToken ct = default)
    {
        var existing = _sources.FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                                                    && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing.Index;

        await AddSourceAsync(path, fileName, ct).ConfigureAwait(false);
        var added = _sources.FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                                                 && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        return added?.Index ?? -1;
    }

    public void ToggleSourceCollapsed(int sourceIndex)
    {
        var source = _sources.FirstOrDefault(s => s.Index == sourceIndex);
        if (source is null)
        {
            return;
        }

        source.Collapsed = !source.Collapsed;
        Raise();
    }

    /// <summary>True only when every shelf source is currently collapsed.</summary>
    public bool AllCollapsed => _sources.Count > 0 && _sources.All(s => s.Collapsed);

    /// <summary>Collapse or expand every shelf source in one go.</summary>
    public void SetAllCollapsed(bool collapsed)
    {
        if (_sources.Count == 0) return;
        var changed = false;
        foreach (var s in _sources)
        {
            if (s.Collapsed != collapsed) { s.Collapsed = collapsed; changed = true; }
        }
        if (changed) Raise();
    }

    /// <summary>Moves a source up (direction &lt; 0) or down (direction &ge; 0) in the shelf
    /// order. Only the display order changes — each source keeps its stable <c>Index</c>, so
    /// page colours and desk page references are unaffected.</summary>
    public void MoveSource(int sourceIndex, int direction)
    {
        var pos = _sources.FindIndex(s => s.Index == sourceIndex);
        if (pos < 0) return;
        var target = pos + (direction < 0 ? -1 : 1);
        if (target < 0 || target >= _sources.Count) return;
        (_sources[pos], _sources[target]) = (_sources[target], _sources[pos]);
        Raise();
    }

    // ── shelf page filter (#19 range) + search (#25) ──────────────────────────
    /// <summary>The page indices a source shows on the shelf: its <see cref="BenchSource.PageFilter"/>
    /// when set, otherwise every page in order.</summary>
    public IReadOnlyList<int> VisiblePages(int sourceIndex)
    {
        var s = _sources.FirstOrDefault(x => x.Index == sourceIndex);
        if (s is null) return System.Array.Empty<int>();
        if (s.PageFilter is { Count: > 0 } f) return f;
        return Enumerable.Range(0, s.PageCount).ToArray();
    }

    public bool HasPageFilter(int sourceIndex) =>
        _sources.FirstOrDefault(x => x.Index == sourceIndex)?.PageFilter is { Count: > 0 };

    /// <summary>Sets (or clears, when null/empty) a source's shelf page filter. Indices are
    /// clamped to the document, de-duplicated, and kept in the given order.</summary>
    public void SetSourcePageFilter(int sourceIndex, IReadOnlyList<int>? pages)
    {
        var s = _sources.FirstOrDefault(x => x.Index == sourceIndex);
        if (s is null) return;
        if (pages is null || pages.Count == 0) { s.PageFilter = null; Raise(); return; }
        var seen = new HashSet<int>();
        var clean = new List<int>();
        foreach (var p in pages)
            if (p >= 0 && p < s.PageCount && seen.Add(p)) clean.Add(p);
        s.PageFilter = clean.Count == 0 || clean.Count == s.PageCount ? null : clean;
        Raise();
    }

    public void ClearSourcePageFilter(int sourceIndex) => SetSourcePageFilter(sourceIndex, null);

    /// <summary>Searches a source's text and returns the distinct page indices that match,
    /// in ascending order. Uses the already-open document (no re-open).</summary>
    public async Task<IReadOnlyList<int>> SearchSourcePagesAsync(int sourceIndex, string query, CancellationToken ct = default)
    {
        var s = _sources.FirstOrDefault(x => x.Index == sourceIndex);
        if (s is null || string.IsNullOrWhiteSpace(query)) return System.Array.Empty<int>();
        var pages = new SortedSet<int>();
        var opts = new Chuvadi.Pdf.Rendering.DisplayList.SearchOptions { CaseSensitive = false, WholeWord = false };
        using var doc = PdfDocument.Open(s.Path);
        await foreach (var m in Chuvadi.Pdf.Rendering.DisplayList.DocumentSearch.SearchAsync(doc, query, opts, ct).WithCancellation(ct))
        {
            if (m.PageNumber >= 0 && m.PageNumber < s.PageCount) pages.Add(m.PageNumber);
        }
        return pages.ToArray();
    }

    /// <summary>True if any desk currently holds a page that was pulled from this source.
    /// Image and blank pages carry no source, so they never count.</summary>
    public bool IsSourceInUse(int sourceIndex) =>
        _desks.Any(d => d.Pages.Any(p => !p.IsBlank && !p.IsImage && p.SourceIndex == sourceIndex));

    /// <summary>Closes a source and removes it from the shelf, disposing its session.
    /// Refuses (returns false) while any desk still holds pages from it — the caller
    /// should tell the user to clear those pages first.</summary>
    public bool TryRemoveSource(int sourceIndex)
    {
        if (IsSourceInUse(sourceIndex))
        {
            return false;
        }

        var source = _sources.FirstOrDefault(s => s.Index == sourceIndex);
        if (source is null)
        {
            return false;
        }

        _sources.Remove(source);
        try { source.Session.Dispose(); } catch { /* best-effort */ }
        Raise();
        return true;
    }

    public bool TryGetSourceThumb(int sourceIndex, int pageIndex, out string svg)
    {
        var source = _sources.FirstOrDefault(s => s.Index == sourceIndex);
        if (source is null)
        {
            svg = string.Empty;
            return false;
        }

        return source.Session.TryGetRenderedSvg(pageIndex, out svg);
    }

    public Task<string> RenderSourceThumbAsync(int sourceIndex, int pageIndex, CancellationToken ct = default)
    {
        var source = _sources.First(s => s.Index == sourceIndex);
        return source.Session.RenderPageSvgAsync(pageIndex, ct);
    }

    // ── desks ─────────────────────────────────────────────────────────────────

    public Desk? FindDesk(Guid deskId) => _desks.FirstOrDefault(d => d.Id == deskId);

    public void EnsureDesk()
    {
        if (_desks.Count == 0)
        {
            _desks.Add(new Desk { Name = "Desk 1" });
        }
    }

    public Desk AddDesk()
    {
        if (_desks.Count >= MaxDesks)
        {
            return _desks[^1];
        }

        var n = _desks.Count + 1;
        var desk = new Desk { Name = $"Desk {n}" };
        _desks.Add(desk);
        Raise();
        return desk;
    }

    public bool CanAddDesk => _desks.Count < MaxDesks;

    public void RemoveDesk(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        foreach (var p in desk.Pages)
        {
            _selected.Remove(p.Id);
        }

        _desks.Remove(desk);
        EnsureDesk();
        Raise();
    }

    /// <summary>Removes every page from a desk but keeps the desk itself. Used by the
    /// "Delete all" action so a source can then be closed from the shelf.</summary>
    public void ClearDesk(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.Pages.Count == 0)
        {
            return;
        }

        foreach (var p in desk.Pages)
        {
            _selected.Remove(p.Id);
        }

        desk.Pages.Clear();
        Raise();
    }

    public void RenameDesk(Guid deskId, string name)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        desk.Name = string.IsNullOrWhiteSpace(name) ? desk.Name : name.Trim();
        desk.NameLocked = true;
        Raise();
    }

    /// <summary>Reorders a desk in the row by one slot (-1 left/up, +1 right/down).</summary>
    public void MoveDesk(Guid deskId, int direction)
    {
        var pos = _desks.FindIndex(d => d.Id == deskId);
        if (pos < 0) return;
        var target = pos + (direction < 0 ? -1 : 1);
        if (target < 0 || target >= _desks.Count) return;
        (_desks[pos], _desks[target]) = (_desks[target], _desks[pos]);
        Raise();
    }

    /// <summary>Duplicates a desk — a deep copy of its pages and all its settings
    /// (size, colour, watermark, header/footer) — inserted right after it. No-op past
    /// <see cref="MaxDesks"/>.</summary>
    public void DuplicateDesk(Guid deskId)
    {
        if (!CanAddDesk) return;
        var pos = _desks.FindIndex(d => d.Id == deskId);
        if (pos < 0) return;
        var src = _desks[pos];
        var copy = new Desk
        {
            Name = string.IsNullOrWhiteSpace(src.Name) ? "Copy" : src.Name + " copy",
            NameLocked = true,
            NormalizeSize = src.NormalizeSize,
            ColorHex = src.ColorHex,
            Watermark = src.Watermark?.Clone(),
            HeaderFooter = src.HeaderFooter?.Clone(),
            Numbering = src.Numbering?.Clone(),
            AddBookmarks = src.AddBookmarks,
        };
        foreach (var p in src.Pages) copy.Pages.Add(ClonePage(p));
        _desks.Insert(pos + 1, copy);
        Raise();
    }

    /// <summary>Sets (or clears, when null/blank) a desk's colour tag.</summary>
    public void SetDeskColour(Guid deskId, string? hex)
    {
        var desk = FindDesk(deskId);
        if (desk is null) return;
        desk.ColorHex = string.IsNullOrWhiteSpace(hex) ? null : hex.Trim();
        Raise();
    }

    private void AutoNameDesk(Desk desk)
    {
        if (desk.NameLocked)
        {
            return;
        }

        var sources = desk.Pages.Where(p => !p.IsBlank && !p.IsImage).Select(p => p.SourceName).Distinct().ToList();
        if (sources.Count == 1)
        {
            desk.Name = Path.GetFileNameWithoutExtension(sources[0]);
        }
    }

    // ── populate desks ─────────────────────────────────────────────────────────

    public void AddPageToDesk(Guid deskId, int sourceIndex, int originalIndex, int insertIndex = -1)
    {
        var desk = FindDesk(deskId);
        var source = _sources.FirstOrDefault(s => s.Index == sourceIndex);
        if (desk is null || source is null)
        {
            return;
        }

        var page = new BenchPage
        {
            SourcePath = source.Path,
            SourceName = source.FileName,
            SourceIndex = source.Index,
            OriginalIndex = originalIndex,
        };

        Insert(desk, page, insertIndex);
        AutoNameDesk(desk);
        Raise();
    }

    /// <summary>Drops every page of a source into a desk, in order, at one spot
    /// (used when a whole file is dragged from the shelf).</summary>
    public void AddSourceAllToDesk(Guid deskId, int sourceIndex, int insertIndex = -1)
    {
        var desk = FindDesk(deskId);
        var source = _sources.FirstOrDefault(s => s.Index == sourceIndex);
        if (desk is null || source is null)
        {
            return;
        }

        var at = insertIndex;
        for (var i = 0; i < source.PageCount; i++)
        {
            var page = new BenchPage
            {
                SourcePath = source.Path,
                SourceName = source.FileName,
                SourceIndex = source.Index,
                OriginalIndex = i,
            };
            Insert(desk, page, at);
            if (at >= 0)
            {
                at++;
            }
        }

        AutoNameDesk(desk);
        Raise();
    }

    /// <summary>Drops a raw image straight into a desk as an image page. It is not
    /// converted to PDF until Bind.</summary>
    public void AddImageToDesk(Guid deskId, string imagePath, int insertIndex = -1)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var page = new BenchPage
        {
            SourcePath = string.Empty,
            SourceName = Path.GetFileName(imagePath),
            SourceIndex = -1,
            OriginalIndex = -1,
            IsImage = true,
            ImagePath = imagePath,
        };

        Insert(desk, page, insertIndex);
        Raise();
    }

    /// <summary>A data-URI thumbnail for an image page, so it can render as an
    /// &lt;img&gt; without converting to PDF. Returns empty if it can't be read.</summary>
    public string ImageDataUri(BenchPage page)
    {
        if (!page.IsImage || string.IsNullOrWhiteSpace(page.ImagePath) || !File.Exists(page.ImagePath))
        {
            return string.Empty;
        }

        try
        {
            var bytes = File.ReadAllBytes(page.ImagePath);
            var ext = Path.GetExtension(page.ImagePath).TrimStart('.').ToLowerInvariant();
            var mime = ext switch
            {
                "jpg" or "jpeg" => "image/jpeg",
                "png" => "image/png",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "webp" => "image/webp",
                "tif" or "tiff" => "image/tiff",
                _ => "application/octet-stream",
            };
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return string.Empty;
        }
    }

    public void AddBlankToDesk(Guid deskId, int insertIndex = -1, string? bgHex = null)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var page = new BenchPage
        {
            SourcePath = string.Empty,
            SourceName = "Blank",
            SourceIndex = -1,
            OriginalIndex = -1,
            IsBlank = true,
            BackgroundHex = bgHex,
        };

        Insert(desk, page, insertIndex);
        Raise();
    }

    private static void Insert(Desk desk, BenchPage page, int insertIndex)
    {
        if (insertIndex < 0 || insertIndex > desk.Pages.Count)
        {
            desk.Pages.Add(page);
        }
        else
        {
            desk.Pages.Insert(insertIndex, page);
        }
    }

    /// <summary>Moves an existing page to a position in a (possibly different) desk.</summary>
    public void MovePage(Guid pageId, Guid toDeskId, int insertIndex)
    {
        var from = DeskOf(pageId);
        var to = FindDesk(toDeskId);
        if (from is null || to is null)
        {
            return;
        }

        var idx = from.Pages.FindIndex(p => p.Id == pageId);
        if (idx < 0)
        {
            return;
        }

        var page = from.Pages[idx];
        from.Pages.RemoveAt(idx);

        if (ReferenceEquals(from, to) && insertIndex > idx)
        {
            insertIndex--;
        }

        Insert(to, page, insertIndex);
        AutoNameDesk(to);
        Raise();
    }

    public void RemovePage(Guid pageId)
    {
        var desk = DeskOf(pageId);
        if (desk is null)
        {
            return;
        }

        desk.Pages.RemoveAll(p => p.Id == pageId);
        _selected.Remove(pageId);
        Raise();
    }

    public void TurnPage(Guid pageId)
    {
        var desk = DeskOf(pageId);
        var page = desk?.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null)
        {
            return;
        }

        page.Rotation = (page.Rotation + 90) % 360;
        Raise();
    }

    public Desk? DeskOf(Guid pageId) => _desks.FirstOrDefault(d => d.Pages.Any(p => p.Id == pageId));

    // ── desk-page thumbnails ────────────────────────────────────────────────────

    public bool TryGetThumb(BenchPage page, out string svg)
    {
        if (page.IsBlank)
        {
            svg = string.Empty;
            return false;
        }

        var source = _sources.FirstOrDefault(s => s.Index == page.SourceIndex);
        if (source is null)
        {
            svg = string.Empty;
            return false;
        }

        return source.Session.TryGetRenderedSvg(page.OriginalIndex, out svg);
    }

    public Task<string> RenderThumbAsync(BenchPage page, CancellationToken ct = default)
    {
        var source = _sources.First(s => s.Index == page.SourceIndex);
        return source.Session.RenderPageSvgAsync(page.OriginalIndex, ct);
    }

    // ── selection (global across desks) ─────────────────────────────────────────

    public bool IsSelected(Guid id) => _selected.Contains(id);

    public int SelectedInDesk(Guid deskId)
    {
        var desk = FindDesk(deskId);
        return desk is null ? 0 : desk.Pages.Count(p => _selected.Contains(p.Id));
    }

    public void ToggleSelect(Guid id)
    {
        if (!_selected.Remove(id))
        {
            _selected.Add(id);
        }

        _selectionAnchor = id;
        _activeDesk = DeskOf(id)?.Id ?? _activeDesk;
        Raise();
    }

    public void SelectRangeTo(Guid id)
    {
        var desk = DeskOf(id);
        if (_selectionAnchor is not Guid anchor || anchor == id || desk is null)
        {
            ToggleSelect(id);
            return;
        }

        var a = desk.Pages.FindIndex(p => p.Id == anchor);
        var b = desk.Pages.FindIndex(p => p.Id == id);
        if (a < 0 || b < 0)
        {
            ToggleSelect(id);
            return;
        }

        var (lo, hi) = a <= b ? (a, b) : (b, a);
        for (var i = lo; i <= hi; i++)
        {
            _selected.Add(desk.Pages[i].Id);
        }

        Raise();
    }

    public void ClearSelection()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        _selected.Clear();
        _selectionAnchor = null;
        Raise();
    }

    // ── per-desk trim / turn ────────────────────────────────────────────────────

    public void TrimSelected(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var removed = desk.Pages.RemoveAll(p => _selected.Contains(p.Id));
        if (removed == 0)
        {
            return;
        }

        _selected.RemoveWhere(id => !_desks.Any(d => d.Pages.Any(p => p.Id == id)));
        Raise();
    }

    public void TurnSelected(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var targets = desk.Pages.Where(p => _selected.Contains(p.Id)).ToList();
        if (targets.Count == 0)
        {
            targets = desk.Pages.ToList();
        }

        foreach (var page in targets)
        {
            page.Rotation = (page.Rotation + 90) % 360;
        }

        Raise();
    }

    // ── page operations (duplicate / reverse / sort / extract / split) ───────────

    private static BenchPage ClonePage(BenchPage p) => new()
    {
        SourcePath = p.SourcePath,
        SourceName = p.SourceName,
        SourceIndex = p.SourceIndex,
        OriginalIndex = p.OriginalIndex,
        Rotation = p.Rotation,
        IsBlank = p.IsBlank,
        BackgroundHex = p.BackgroundHex,
        IsImage = p.IsImage,
        ImagePath = p.ImagePath,
        Crop = p.Crop,
        CropMode = p.CropMode,
        Margins = p.Margins,
    };

    /// <summary>Inserts a copy of a page directly after the original, on the same desk.</summary>
    public void DuplicatePage(Guid pageId)
    {
        var desk = DeskOf(pageId);
        if (desk is null) return;
        var idx = desk.Pages.FindIndex(p => p.Id == pageId);
        if (idx < 0) return;
        desk.Pages.Insert(idx + 1, ClonePage(desk.Pages[idx]));
        Raise();
    }

    /// <summary>Reverses the page order of a desk.</summary>
    public void ReverseDesk(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.Pages.Count < 2) return;
        desk.Pages.Reverse();
        Raise();
    }

    /// <summary>Sorts a desk's pages back into source order (by source, then page number).
    /// Blank and image pages have no source, so they sink to the end keeping their order.</summary>
    public void SortDesk(Guid deskId)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.Pages.Count < 2) return;
        var sorted = desk.Pages
            .OrderBy(p => (p.IsBlank || p.IsImage) ? 1 : 0)
            .ThenBy(p => p.SourceIndex)
            .ThenBy(p => p.OriginalIndex)
            .ToList();
        desk.Pages.Clear();
        desk.Pages.AddRange(sorted);
        Raise();
    }

    /// <summary>Moves every currently-selected page (from any desk) into a new desk,
    /// preserving their order. Returns null if nothing is selected or the desk limit is hit.</summary>
    public Desk? ExtractSelectedToNewDesk()
    {
        if (_selected.Count == 0 || _desks.Count >= MaxDesks) return null;
        var picked = new List<BenchPage>();
        foreach (var d in _desks)
        {
            foreach (var p in d.Pages)
            {
                if (_selected.Contains(p.Id)) picked.Add(p);
            }
        }
        if (picked.Count == 0) return null;
        foreach (var d in _desks)
        {
            d.Pages.RemoveAll(p => _selected.Contains(p.Id));
        }
        var desk = new Desk { Name = "Extracted" };
        desk.Pages.AddRange(picked);
        _desks.Add(desk);
        _selected.Clear();
        AutoNameDesk(desk);
        Raise();
        return desk;
    }

    /// <summary>Splits a desk at a page: that page and everything after it move to a new
    /// desk inserted just after the original. No-op if the page is the desk's first page.</summary>
    public Desk? SplitDeskAt(Guid pageId)
    {
        var desk = DeskOf(pageId);
        if (desk is null || _desks.Count >= MaxDesks) return null;
        var idx = desk.Pages.FindIndex(p => p.Id == pageId);
        if (idx <= 0) return null;
        var count = desk.Pages.Count - idx;
        var tail = desk.Pages.GetRange(idx, count);
        desk.Pages.RemoveRange(idx, count);
        var newDesk = new Desk { Name = $"{desk.Name} (2)" };
        newDesk.Pages.AddRange(tail);
        var pos = _desks.IndexOf(desk);
        _desks.Insert(pos + 1, newDesk);
        AutoNameDesk(newDesk);
        Raise();
        return newDesk;
    }

    // ── geometry / arrangement (items 13–15) ──────────────────────────────────────

    /// <summary>Builds a new desk that interleaves two sources page-for-page
    /// (A1, B1, A2, B2, …), then appends the longer source's remainder. For weaving
    /// a separately-scanned front and back of a double-sided stack.</summary>
    public Desk? InterleaveSources(int sourceIndexA, int sourceIndexB)
    {
        var a = _sources.FirstOrDefault(s => s.Index == sourceIndexA);
        var b = _sources.FirstOrDefault(s => s.Index == sourceIndexB);
        if (a is null || b is null || a.Index == b.Index || _desks.Count >= MaxDesks) return null;

        var desk = AddDesk();
        var max = Math.Max(a.PageCount, b.PageCount);
        for (var i = 0; i < max; i++)
        {
            if (i < a.PageCount)
                desk.Pages.Add(new BenchPage { SourcePath = a.Path, SourceName = a.FileName, SourceIndex = a.Index, OriginalIndex = i });
            if (i < b.PageCount)
                desk.Pages.Add(new BenchPage { SourcePath = b.Path, SourceName = b.FileName, SourceIndex = b.Index, OriginalIndex = i });
        }
        AutoNameDesk(desk);
        Raise();
        return desk;
    }

    private static (double W, double H) SheetDims(string sheet, bool landscape)
    {
        var (w, h) = sheet.Equals("Letter", StringComparison.OrdinalIgnoreCase) ? (612.0, 792.0) : (595.0, 842.0);
        return landscape ? (h, w) : (w, h);
    }

    /// <summary>N-up imposition (#38): flattens a desk to its bound form, lays its pages
    /// <paramref name="rows"/>×<paramref name="cols"/> per sheet, and brings the result back as a
    /// new shelf source + new desk. Returns the new desk (or null if nothing to do / desk cap).</summary>
    public async Task<Desk?> NupToNewDeskAsync(Guid deskId, int rows, int cols, string sheet, double gutterPt, CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.IsEmpty || !CanAddDesk) return null;

        var flat = Path.Combine(Path.GetTempPath(), $"chuvadi-flat-{Guid.NewGuid():N}.pdf");
        var nup = Path.Combine(Path.GetTempPath(), $"chuvadi-nup-{Guid.NewGuid():N}.pdf");
        try
        {
            await BindDeskAsync(deskId, flat, ct).ConfigureAwait(false);
            var (sw, sh) = SheetDims(sheet, landscape: cols > rows);
            await _composer.ImposeNupAsync(flat, nup, rows, cols, sw, sh, gutterPt, 18, ct).ConfigureAwait(false);
        }
        finally { try { File.Delete(flat); } catch { /* best effort */ } }

        var idx = await EnsureSourceAsync(nup, $"{SafeName(desk.Name)}-{cols}x{rows}up.pdf", ct).ConfigureAwait(false);
        if (idx < 0) return null;
        var newDesk = AddDesk();
        newDesk.Name = $"{desk.Name} · {rows * cols}-up"; newDesk.NameLocked = true;
        AddSourceAllToDesk(newDesk.Id, idx);
        Raise();
        return newDesk;
    }

    /// <summary>Booklet imposition (#39): flattens a desk, imposes it saddle-stitch (2-up,
    /// padded to a multiple of four), and brings the result back as a new source + new desk.</summary>
    public async Task<Desk?> BookletToNewDeskAsync(Guid deskId, string sheet, double gutterPt, CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.IsEmpty || !CanAddDesk) return null;

        var flat = Path.Combine(Path.GetTempPath(), $"chuvadi-flat-{Guid.NewGuid():N}.pdf");
        var bk = Path.Combine(Path.GetTempPath(), $"chuvadi-booklet-{Guid.NewGuid():N}.pdf");
        try
        {
            await BindDeskAsync(deskId, flat, ct).ConfigureAwait(false);
            var (sw, sh) = SheetDims(sheet, landscape: true); // 2-up → landscape sheet
            await _composer.ImposeBookletAsync(flat, bk, sw, sh, gutterPt, 18, ct).ConfigureAwait(false);
        }
        finally { try { File.Delete(flat); } catch { /* best effort */ } }

        var idx = await EnsureSourceAsync(bk, $"{SafeName(desk.Name)}-booklet.pdf", ct).ConfigureAwait(false);
        if (idx < 0) return null;
        var newDesk = AddDesk();
        newDesk.Name = $"{desk.Name} · booklet"; newDesk.NameLocked = true;
        AddSourceAllToDesk(newDesk.Id, idx);
        Raise();
        return newDesk;
    }

    /// <summary>Sets a crop window on a page (normalised top-left/y-down fractions of the
    /// displayed page) and how it is realised at Bind. A near-empty rect clears the crop.</summary>
    public void SetPageCrop(Guid pageId, double x, double y, double w, double h, CropFit mode)
    {
        var page = _desks.SelectMany(d => d.Pages).FirstOrDefault(p => p.Id == pageId);
        if (page is null) return;
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
        w = Math.Clamp(w, 0, 1 - x); h = Math.Clamp(h, 0, 1 - y);
        if (w <= 0.01 || h <= 0.01) { page.Crop = null; }
        else { page.Crop = new CropRect(x, y, w, h); page.CropMode = mode; }
        Raise();
    }

    public void ClearPageCrop(Guid pageId)
    {
        var page = _desks.SelectMany(d => d.Pages).FirstOrDefault(p => p.Id == pageId);
        if (page is null || page.Crop is null) return;
        page.Crop = null;
        Raise();
    }

    public bool HasCrop(Guid pageId) =>
        _desks.SelectMany(d => d.Pages).FirstOrDefault(p => p.Id == pageId)?.Crop is not null;

    // ── margins (item: Add Margins) ───────────────────────────────────────────
    /// <summary>Sets per-side content margins (in PDF points) on a page, or on every page of
    /// its desk when <paramref name="allPagesInDesk"/> is true. All-zero clears the margins.
    /// Applied at Bind: the page content scales to fit inside the page minus the margins.</summary>
    public void SetPageMargins(Guid pageId, double topPt, double rightPt, double bottomPt, double leftPt, bool allPagesInDesk)
    {
        var desk = _desks.FirstOrDefault(d => d.Pages.Any(p => p.Id == pageId));
        if (desk is null) return;
        var m = new MarginSet(Math.Max(0, topPt), Math.Max(0, rightPt), Math.Max(0, bottomPt), Math.Max(0, leftPt));
        var any = m.Top > 0 || m.Right > 0 || m.Bottom > 0 || m.Left > 0;
        if (allPagesInDesk)
        {
            foreach (var p in desk.Pages) p.Margins = any ? m : null;
        }
        else
        {
            var page = desk.Pages.FirstOrDefault(p => p.Id == pageId);
            if (page is null) return;
            page.Margins = any ? m : null;
        }
        Raise();
    }

    public void ClearPageMargins(Guid pageId)
    {
        var page = _desks.SelectMany(d => d.Pages).FirstOrDefault(p => p.Id == pageId);
        if (page is null || page.Margins is null) return;
        page.Margins = null;
        Raise();
    }

    public bool HasMargins(Guid pageId) =>
        _desks.SelectMany(d => d.Pages).FirstOrDefault(p => p.Id == pageId)?.Margins is not null;

    public MarginSet? GetMargins(Guid pageId) =>
        _desks.SelectMany(d => d.Pages).FirstOrDefault(p => p.Id == pageId)?.Margins;

    /// <summary>Per-desk page-size normalisation applied at Bind: "A4", "Letter", or null/"" to
    /// leave pages at their own size. Every page is scaled to fit the target (aspect preserved).</summary>
    public void SetDeskNormalize(Guid deskId, string? size)
    {
        var desk = FindDesk(deskId);
        if (desk is null) return;
        var s = (size ?? "").Trim();
        desk.NormalizeSize = (s.Equals("A4", StringComparison.OrdinalIgnoreCase) || s.Equals("Letter", StringComparison.OrdinalIgnoreCase)) ? s : null;
        Raise();
    }

    // ── output ──────────────────────────────────────────────────────────────────

    public string SuggestName(Guid deskId)
    {
        var desk = FindDesk(deskId);
        return desk is null ? "binding" : SafeName(desk.Name);
    }

    private static string SafeName(string name)
    {
        var safe = new string(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "binding" : safe.Trim();
    }

    public async Task BindDeskAsync(Guid deskId, string outputPath, CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.IsEmpty)
        {
            return;
        }

        var bytes = await _composer.ComposeAsync(desk.Pages, ct, desk.NormalizeSize).ConfigureAwait(false);
        bytes = ApplyWatermark(bytes, desk, desk.Pages, allComposed: true);
        bytes = ApplyHeaderFooter(bytes, desk, desk.Pages);
        bytes = ApplyNumbering(bytes, desk, desk.Pages);
        bytes = ApplyBookmarks(bytes, desk, desk.Pages);
        await File.WriteAllBytesAsync(outputPath, bytes, ct).ConfigureAwait(false);
    }

    public async Task LiftDeskAsync(Guid deskId, string outputPath, CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        var selected = desk.Pages.Where(p => _selected.Contains(p.Id)).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var bytes = await _composer.ComposeAsync(selected, ct, desk.NormalizeSize).ConfigureAwait(false);
        bytes = ApplyWatermark(bytes, desk, selected, allComposed: true);
        bytes = ApplyHeaderFooter(bytes, desk, selected);
        bytes = ApplyNumbering(bytes, desk, selected);
        bytes = ApplyBookmarks(bytes, desk, selected);
        await File.WriteAllBytesAsync(outputPath, bytes, ct).ConfigureAwait(false);
    }

    /// <summary>Export with bookmarks (#47): builds a per-source-section outline (each source's
    /// own bookmarks nested + remapped) and writes it into the bound bytes. Off when the desk's
    /// AddBookmarks is false or nothing meaningful results.</summary>
    private byte[] ApplyBookmarks(byte[] bytes, Desk desk, IReadOnlyList<BenchPage> composed)
    {
        if (!desk.AddBookmarks)
        {
            return bytes;
        }

        try
        {
            var titles = new Dictionary<string, string>();
            foreach (var p in composed)
            {
                if (!string.IsNullOrEmpty(p.SourcePath) && !titles.ContainsKey(p.SourcePath))
                {
                    titles[p.SourcePath] = string.IsNullOrWhiteSpace(p.SourceName) ? Path.GetFileName(p.SourcePath) : p.SourceName;
                }
            }

            var outline = _outline.BuildExportOutline(
                composed,
                src => titles.TryGetValue(src, out var t) ? t : Path.GetFileName(src),
                src => { try { return _outline.Read(src); } catch { return Array.Empty<ChuvadiReader.Core.Documents.BookmarkNode>(); } });

            return outline.Count == 0 ? bytes : _outline.Write(bytes, outline);
        }
        catch
        {
            return bytes; // bookmarks must never block the bind
        }
    }

    /// <summary>Binds every non-empty desk to <paramref name="folder"/>, one PDF per desk.
    /// Returns the number of files written.</summary>
    public async Task<int> BindAllAsync(string folder, CancellationToken ct = default)
    {
        var written = 0;
        foreach (var desk in _desks.Where(d => !d.IsEmpty))
        {
            ct.ThrowIfCancellationRequested();
            var outPath = ChuvadiReader.Core.Documents.ExportService.UniquePath(folder, SafeName(desk.Name), "pdf");
            await BindDeskAsync(desk.Id, outPath, ct).ConfigureAwait(false);
            written++;
        }

        return written;
    }

    /// <summary>Combines every non-empty desk (in row order) into a single PDF at
    /// <paramref name="outputPath"/>. Each desk is bound with its own size / watermark /
    /// header-footer first, then the desks are merged object-level. Returns the desk count.</summary>
    public async Task<int> CombineAllAsync(string outputPath, CancellationToken ct = default)
    {
        var desks = _desks.Where(d => !d.IsEmpty).ToList();
        if (desks.Count == 0) return 0;

        var temps = new List<string>(desks.Count);
        try
        {
            foreach (var desk in desks)
            {
                ct.ThrowIfCancellationRequested();
                var tmp = Path.Combine(Path.GetTempPath(), $"chuvadi-combine-{Guid.NewGuid():N}.pdf");
                await BindDeskAsync(desk.Id, tmp, ct).ConfigureAwait(false);
                temps.Add(tmp);
            }
            await _composer.MergeFilesAsync(temps, outputPath, ct).ConfigureAwait(false);
            return desks.Count;
        }
        finally
        {
            foreach (var t in temps)
            {
                try { File.Delete(t); } catch { /* best effort */ }
            }
        }
    }

    public async Task<int> ScatterDeskAsync(Guid deskId, string folder, CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null || desk.IsEmpty)
        {
            return 0;
        }

        var width = desk.Pages.Count.ToString().Length;
        for (var i = 0; i < desk.Pages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = desk.Pages[i];
            var baseName = page.IsBlank ? "blank" : Path.GetFileNameWithoutExtension(page.SourceName);
            var fileBase = $"{(i + 1).ToString().PadLeft(width, '0')}_{baseName}";
            var outPath = ChuvadiReader.Core.Documents.ExportService.UniquePath(folder, fileBase, "pdf");
            await _composer.ComposeToFileAsync(new[] { page }, outPath, ct, desk.NormalizeSize).ConfigureAwait(false);
        }

        return desk.Pages.Count;
    }

    public async Task<int> ExportDeskAsync(
        Guid deskId,
        string folder,
        ChuvadiReader.Core.Documents.ExportFormat format,
        int dpi,
        bool onlySelected,
        CancellationToken ct = default)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return 0;
        }

        var pages = (onlySelected && SelectedInDesk(deskId) > 0)
            ? desk.Pages.Where(p => _selected.Contains(p.Id)).ToList()
            : desk.Pages.ToList();
        if (pages.Count == 0)
        {
            return 0;
        }

        var tmp = Path.Combine(Path.GetTempPath(), $"chuvadi-bench-{Guid.NewGuid():N}.pdf");
        try
        {
            await _composer.ComposeToFileAsync(pages, tmp, ct, desk.NormalizeSize).ConfigureAwait(false);
            return await _export.ExportAsync(tmp, folder, format, allPages: true,
                currentPageIndex: 0, dpi: dpi, baseNameOverride: SafeName(desk.Name), ct: ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(tmp); }
            catch { }
        }
    }

    // ── watermark ────────────────────────────────────────────────────────────────

    public void SetDeskWatermark(Guid deskId, string text, string fontFamily, bool bold, bool italic,
        double fontSize, string colorHex, double opacity, double rotationDegrees, bool allPages)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            desk.Watermark = null;
        }
        else
        {
            desk.Watermark = new DeskWatermark
            {
                Text = text.Trim(),
                FontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Helvetica" : fontFamily,
                Bold = bold,
                Italic = italic,
                FontSize = Math.Clamp(fontSize, 6, 400),
                ColorHex = string.IsNullOrWhiteSpace(colorHex) ? "#808080" : colorHex,
                Opacity = Math.Clamp(opacity, 0.05, 1.0),
                RotationDegrees = rotationDegrees,
                AllPages = allPages,
            };
        }

        Raise();
    }

    private static Chuvadi.Pdf.Graphics.ColorF HexToColorF(string? hex)
    {
        var s = (hex ?? "#808080").TrimStart('#');
        if (s.Length == 3)
        {
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        }
        byte r = 0x80, g = 0x80, b = 0x80;
        if (s.Length >= 6
            && byte.TryParse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var rr)
            && byte.TryParse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var gg)
            && byte.TryParse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var bb))
        {
            r = rr; g = gg; b = bb;
        }
        return Chuvadi.Pdf.Graphics.ColorF.FromRgb8(r, g, b, 255);
    }

    private byte[] ApplyWatermark(byte[] bytes, Desk desk, IReadOnlyList<BenchPage> composed, bool allComposed)
    {
        var wm = desk.Watermark;
        if (wm is null || string.IsNullOrWhiteSpace(wm.Text))
        {
            return bytes;
        }

        int[] indices;
        if (wm.AllPages)
        {
            indices = Enumerable.Range(0, composed.Count).ToArray();
        }
        else
        {
            indices = composed
                .Select((p, i) => (p, i))
                .Where(t => _selected.Contains(t.p.Id))
                .Select(t => t.i)
                .ToArray();
            if (indices.Length == 0)
            {
                return bytes; // nothing selected to stamp
            }
        }

        return _stamp.ApplyWatermark(bytes, wm, indices);
    }

    // ── header / footer ────────────────────────────────────────────────────────

    public void SetDeskHeaderFooter(Guid deskId, DeskHeaderFooter? config)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        desk.HeaderFooter = (config is not null && config.HasAnyContent) ? config : null;
        Raise();
    }

    private byte[] ApplyHeaderFooter(byte[] bytes, Desk desk, IReadOnlyList<BenchPage> composed)
    {
        var hf = desk.HeaderFooter;
        if (hf is null || !hf.HasAnyContent)
        {
            return bytes;
        }

        var pageIdx = ChuvadiReader.Core.Documents.StampService.ResolveRange(
            hf.AllPages, hf.FromPage, hf.ToPage, composed.Count);
        if (pageIdx.Length == 0)
        {
            return bytes;
        }

        var filePath = string.IsNullOrWhiteSpace(desk.Name) ? "document.pdf" : desk.Name + ".pdf";
        try
        {
            return _stamp.ApplyHeaderFooter(bytes, hf, pageIdx, filePath, DateTimeOffset.Now);
        }
        catch
        {
            return bytes; // never let a footer failure block the bind
        }
    }

    // ── numbering (#40) ──────────────────────────────────────────────────────────

    public void SetDeskNumbering(Guid deskId, DeskNumbering? config)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        desk.Numbering = config;
        Raise();
    }

    public void SetDeskBookmarks(Guid deskId, bool on)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        desk.AddBookmarks = on;
        Raise();
    }

    private byte[] ApplyNumbering(byte[] bytes, Desk desk, IReadOnlyList<BenchPage> composed)
    {
        var num = desk.Numbering;
        if (num is null)
        {
            return bytes;
        }

        try
        {
            return _stamp.ApplyNumbering(bytes, num, composed.Count);
        }
        catch
        {
            return bytes; // numbering must never block the bind
        }
    }

    // ── reset ──────────────────────────────────────────────────────────────────

    // ── sessions & templates (#36 / #37) ───────────────────────────────────────

    /// <summary>Captures the whole bench — shelf sources and every desk/page/setting — as a
    /// serializable DTO. Sources are recorded by path, not embedded.</summary>
    public BenchSessionDto ExportSession() => new()
    {
        Version = 1,
        Sources = _sources
            .OrderBy(s => s.Index)
            .Select(s => new SessionSourceDto
            {
                Index = s.Index,
                Path = s.Path,
                FileName = s.FileName,
                PageFilter = s.PageFilter?.ToList(),
                Collapsed = s.Collapsed,
            })
            .ToList(),
        Desks = _desks
            .Select(d => new SessionDeskDto
            {
                Name = d.Name,
                NameLocked = d.NameLocked,
                ColorHex = d.ColorHex,
                NormalizeSize = d.NormalizeSize,
                AddBookmarks = d.AddBookmarks,
                Watermark = d.Watermark?.Clone(),
                HeaderFooter = d.HeaderFooter?.Clone(),
                Numbering = d.Numbering?.Clone(),
                Pages = d.Pages.Select(p => new SessionPageDto
                {
                    SourceIndex = p.SourceIndex,
                    OriginalIndex = p.OriginalIndex,
                    SourcePath = p.SourcePath,
                    SourceName = p.SourceName,
                    Rotation = p.Rotation,
                    IsBlank = p.IsBlank,
                    BackgroundHex = p.BackgroundHex,
                    IsImage = p.IsImage,
                    ImagePath = p.ImagePath,
                    Crop = p.Crop,
                    CropMode = p.CropMode,
                    Margins = p.Margins,
                }).ToList(),
            })
            .ToList(),
    };

    /// <summary>Replaces the whole bench with a restored session. Each source is re-opened from its
    /// path; a source whose file is missing is skipped (and its pages with it), and every skip is
    /// reported in the returned <see cref="SessionImportResult"/>. Pages are matched to sources by
    /// path, so a single missing source doesn't corrupt the rest.</summary>
    public async Task<SessionImportResult> ImportSessionAsync(BenchSessionDto session, CancellationToken ct = default)
    {
        var result = new SessionImportResult();

        foreach (var source in _sources)
        {
            source.Session.Dispose();
        }
        _sources.Clear();
        _desks.Clear();
        _selected.Clear();
        _selectionAnchor = null;
        _activeDesk = null;

        // Re-open the sources first, by path. Skipped sources are remembered so their pages drop.
        var byPath = new Dictionary<string, BenchSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var sd in session.Sources.OrderBy(s => s.Index))
        {
            if (byPath.ContainsKey(sd.Path))
            {
                continue;
            }

            try
            {
                if (!File.Exists(sd.Path))
                {
                    throw new FileNotFoundException(sd.Path);
                }

                var pdfSession = await _reader.OpenAsync(sd.Path, sd.FileName, null, ct).ConfigureAwait(false);
                var src = new BenchSource
                {
                    Index = _sources.Count == 0 ? 0 : _sources.Max(s => s.Index) + 1,
                    Path = sd.Path,
                    FileName = sd.FileName,
                    Session = pdfSession,
                };
                src.PageFilter = sd.PageFilter is { Count: > 0 } ? sd.PageFilter.ToList() : null;
                src.Collapsed = sd.Collapsed;
                _sources.Add(src);
                byPath[sd.Path] = src;
            }
            catch
            {
                result.SourcesMissing++;
                result.Warnings.Add($"Source not found — skipped: {sd.FileName}");
            }
        }
        result.SourcesLoaded = _sources.Count;

        foreach (var dd in session.Desks)
        {
            var desk = new Desk
            {
                Name = dd.Name,
                NameLocked = dd.NameLocked,
                ColorHex = dd.ColorHex,
                NormalizeSize = dd.NormalizeSize,
                AddBookmarks = dd.AddBookmarks,
                Watermark = dd.Watermark?.Clone(),
                HeaderFooter = dd.HeaderFooter?.Clone(),
                Numbering = dd.Numbering?.Clone(),
            };

            foreach (var pd in dd.Pages)
            {
                if (pd.IsBlank)
                {
                    desk.Pages.Add(new BenchPage
                    {
                        SourcePath = string.Empty,
                        SourceName = string.Empty,
                        SourceIndex = -1,
                        OriginalIndex = -1,
                        IsBlank = true,
                        BackgroundHex = pd.BackgroundHex,
                        Rotation = pd.Rotation,
                        Crop = pd.Crop,
                        CropMode = pd.CropMode,
                        Margins = pd.Margins,
                    });
                }
                else if (pd.IsImage)
                {
                    if (string.IsNullOrEmpty(pd.ImagePath) || !File.Exists(pd.ImagePath))
                    {
                        result.PagesSkipped++;
                        result.Warnings.Add($"Image not found — skipped: {Path.GetFileName(pd.ImagePath ?? "(unknown)")}");
                        continue;
                    }

                    desk.Pages.Add(new BenchPage
                    {
                        SourcePath = string.Empty,
                        SourceName = string.Empty,
                        SourceIndex = -1,
                        OriginalIndex = -1,
                        IsImage = true,
                        ImagePath = pd.ImagePath,
                        Rotation = pd.Rotation,
                        Crop = pd.Crop,
                        CropMode = pd.CropMode,
                        Margins = pd.Margins,
                    });
                }
                else if (byPath.TryGetValue(pd.SourcePath, out var src))
                {
                    desk.Pages.Add(new BenchPage
                    {
                        SourcePath = src.Path,
                        SourceName = src.FileName,
                        SourceIndex = src.Index,
                        OriginalIndex = pd.OriginalIndex,
                        Rotation = pd.Rotation,
                        Crop = pd.Crop,
                        CropMode = pd.CropMode,
                        Margins = pd.Margins,
                    });
                }
                else
                {
                    // Its source was missing/skipped above (already reported at source level).
                    result.PagesSkipped++;
                }
            }

            _desks.Add(desk);
        }

        if (_desks.Count == 0)
        {
            EnsureDesk();
        }

        ClearHistory();
        Raise();
        return result;
    }

    /// <summary>Snapshots a desk's settings (no pages) as a reusable template.</summary>
    public DeskTemplateDto? CaptureTemplate(Guid deskId, string name)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return null;
        }

        return new DeskTemplateDto
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Template" : name.Trim(),
            ColorHex = desk.ColorHex,
            NormalizeSize = desk.NormalizeSize,
            AddBookmarks = desk.AddBookmarks,
            Watermark = desk.Watermark?.Clone(),
            HeaderFooter = desk.HeaderFooter?.Clone(),
            Numbering = desk.Numbering?.Clone(),
        };
    }

    /// <summary>Applies a template's settings to a desk (its pages are untouched). Undoable.</summary>
    public void ApplyTemplate(Guid deskId, DeskTemplateDto template)
    {
        var desk = FindDesk(deskId);
        if (desk is null)
        {
            return;
        }

        desk.ColorHex = template.ColorHex;
        desk.NormalizeSize = template.NormalizeSize;
        desk.AddBookmarks = template.AddBookmarks;
        desk.Watermark = template.Watermark?.Clone();
        desk.HeaderFooter = template.HeaderFooter?.Clone();
        desk.Numbering = template.Numbering?.Clone();
        Raise();
    }

    public void Reset()
    {
        foreach (var source in _sources)
        {
            source.Session.Dispose();
        }

        _sources.Clear();
        _desks.Clear();
        _selected.Clear();
        _selectionAnchor = null;
        _activeDesk = null;
        EnsureDesk();
        ClearHistory();
        Raise();
    }

    /// <summary>An immutable deep-copied capture of the desk arrangement and selection, used as
    /// one undo/redo step. Desks are already cloned when a snapshot is taken and cloned again when
    /// applied, so a stored snapshot is never mutated by later edits.</summary>
    private sealed record BenchSnapshot(
        List<Desk> Desks,
        HashSet<Guid> Selected,
        Guid? Anchor,
        Guid? ActiveDesk);
}
