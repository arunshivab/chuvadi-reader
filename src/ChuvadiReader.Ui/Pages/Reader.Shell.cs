using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Ui.Components.Platform;
using ChuvadiReader.Ui.Components.Viewers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace ChuvadiReader.Ui.Pages;

/// <summary>
/// D-10 wiring for the Reader: modes (Read / Mark / Protect live here; Arrange is the Bench
/// view), the Tools and View rails, the Pages &amp; outline drawer, floating cards, the hover
/// strip, Measure, markup undo, view-only viewers (Word, Excel, Markdown, text, HTML), and
/// everything published to the shell (top-bar counter, Document menu, Ctrl K, status bar).
/// </summary>
public partial class Reader
{
    [Inject] private ShellService Shell { get; set; } = default!;
    [Inject] private MeasureService Measurer { get; set; } = default!;
    [Inject] private SignatureCheckService SigCheck { get; set; } = default!;
    [Inject] private CertificateSignService CertSign { get; set; } = default!;

    // ── state ────────────────────────────────────────────────────────────
    private bool _protect;
    private bool _pagesDrawer;
    private string _drawerTab = "pages";
    private string? _card;
    private string _stampFocus = "stamp";
    private string? _bmLoadedFor;

    // Measure
    private bool _measureOn;
    private int _msPage = -1;
    private PagePoint? _msA;
    private PagePoint? _msB;
    private MeasureResult? _measure;

    // Signatures
    private SignatureCheckReport? _sigReport;
    private bool _showSigBanner = true;
    private string? _sigCheckedFor;

    // Certificate signing
    private string? _certPath;
    private string _certPwd = string.Empty;
    private string _certReason = string.Empty;
    private string _certLocation = string.Empty;
    private string? _certSummary;
    private bool _certBusy;

    // View-only viewers
    private ViewerBase? _viewer;
    private IReadOnlyList<ViewerOutlineEntry> _viewerOutline = Array.Empty<ViewerOutlineEntry>();
    private ViewerCounter? _viewerCounter;
    private string? _viewerFind;
    private string _viewerFindInput = string.Empty;
    private bool _viewerFindOpen;
    private int _viewerFindCount;
    private string? _viewerStatus;
    private int _textSize = 100;
    private string _readWidth = "normal";
    private bool _wrap = true;
    private bool _showSource;

    // Markup undo / redo (snapshots of every overlay)
    private readonly Stack<MkState> _mkUndo = new();
    private readonly Stack<MkState> _mkRedo = new();
    private MkState? _pendingSnap;

    private sealed record MkState(
        Dictionary<int, List<TextBox>> Texts,
        Dictionary<int, List<ShapeBox>> Shapes,
        Dictionary<int, List<ImageBox>> Images,
        string Signature);

    // ── lifecycle hooks (called from Reader.razor) ───────────────────────
    private void ShellInit()
    {
        Shell.ModeHandler = HandleModeAsync;
        _counterHandler = p => InvokeAsync(() => CounterGoAsync(p));
        _findHandler = () => InvokeAsync(OpenFindFromShell);
        Shell.CounterHandler = _counterHandler;
        Shell.FindHandler = _findHandler;
        _findTextHandler = q => InvokeAsync(() => FindTextAsync(q));
        Shell.FindTextHandler = _findTextHandler;
        Shell.Changed += OnShellChanged;
        ApplyShellMode();
    }

    /// <summary>Open a Protect tool requested by the redaction view (after the document loads).</summary>
    private async Task ConsumePendingToolAsync()
    {
        var tool = Shell.PendingTool;
        if (tool is null || Tab?.Session is null)
        {
            return;
        }

        Shell.PendingTool = null;
        if (tool == "flatten")
        {
            SetLocalMode(DocMode.Protect);
            await FlattenNow();
        }
        else
        {
            OpenProtectCard(tool);
        }
    }

    private Func<int, Task>? _counterHandler;
    private Func<Task>? _findHandler;
    private Func<string, Task>? _findTextHandler;

    /// <summary>Top-bar search: open Find with the text and run it.</summary>
    private async Task FindTextAsync(string query)
    {
        if (Tab is { } t && t.Format.IsViewOnly())
        {
            _viewerFindOpen = true;
            _viewerFindInput = query;
            _viewerFind = query;
            StateHasChanged();
            return;
        }

        _findOpen = true;
        _findQuery = query;
        StateHasChanged();
        await RunFindSearch();
        StateHasChanged();
    }

    private bool _shellGone;

    private void ShellDispose()
    {
        _shellGone = true;
        Shell.Changed -= OnShellChanged;
        if (Shell.ModeHandler == HandleModeAsync)
        {
            Shell.ModeHandler = null;
        }

        // The next page may already have installed its own handlers; only remove ours.
        if (Shell.CounterHandler == _counterHandler)
        {
            Shell.CounterHandler = null;
        }

        if (Shell.FindHandler == _findHandler)
        {
            Shell.FindHandler = null;
        }

        if (Shell.FindTextHandler == _findTextHandler)
        {
            Shell.FindTextHandler = null;
        }

        Shell.UnregisterCommands(this);
    }

    private void OnShellChanged() => _ = InvokeAsync(() =>
    {
        if (ApplyShellMode())
        {
            StateHasChanged();
        }
    });

    /// <summary>Called when the active document changes (new tab or reloaded file).</summary>
    private void OnActiveDocChanged(ReaderTab? tab)
    {
        _measureOn = false;
        _msA = _msB = null;
        _measure = null;
        _msPage = -1;
        _card = _card == "view" ? "view" : null;
        _sigReport = null;
        _showSigBanner = true;
        _viewer = null;
        _viewerOutline = Array.Empty<ViewerOutlineEntry>();
        _viewerCounter = null;
        _viewerFind = null;
        _viewerFindOpen = false;
        _viewerFindCount = 0;
        _viewerStatus = null;
        _showSource = false;
        _mkUndo.Clear();
        _mkRedo.Clear();
        _bmLoadedFor = null;
        if (_pagesDrawer && tab is not null && !tab.Format.IsViewOnly())
        {
            EnsureBookmarksLoaded();
        }

        _ = CheckSignaturesAsync(tab);
    }

    /// <summary>True while any floating card is open, so the rails stay narrow and never cover it.</summary>
    private bool HasAnyCard =>
        _card is not null
        || (_markup && (_sigOpen || _selText is not null || _selShape is not null || _selImage is not null || _mkStatus is not null));

    // ── modes ────────────────────────────────────────────────────────────
    private DocMode LocalMode => _markup ? DocMode.Mark : _protect ? DocMode.Protect : DocMode.Read;

    private async Task<bool> HandleModeAsync(DocMode m)
    {
        if (m == DocMode.Arrange)
        {
            // The shell syncs the shelf and opens Arrange. Unsaved marks stay in memory here.
            return false;
        }

        await InvokeAsync(() =>
        {
            Shell.SetMode(m, Tab?.Id);
            ApplyShellMode();
            StateHasChanged();
        });
        return true;
    }

    /// <summary>Bring the local view in line with the shell's mode. Returns true if it changed.</summary>
    private bool ApplyShellMode()
    {
        var want = Shell.Mode;
        if (want == DocMode.Arrange)
        {
            want = DocMode.Read;
        }

        if (Tab is { } t && t.Format.IsViewOnly())
        {
            want = DocMode.Read;
        }

        if (want == LocalMode)
        {
            return false;
        }

        switch (want)
        {
            case DocMode.Mark:
                _protect = false;
                CloseProtectCards();
                if (!_markup)
                {
                    ToggleMarkup();
                }

                _measureOn = false;
                break;
            case DocMode.Protect:
                if (_markup)
                {
                    ToggleMarkup();
                }

                _protect = true;
                _measureOn = false;
                if (_card is "layers" or "measure" or "signatures" or "print")
                {
                    _card = null;
                }

                break;
            default:
                if (_markup)
                {
                    ToggleMarkup();
                }

                _protect = false;
                CloseProtectCards();
                break;
        }

        return true;
    }

    private void ApplyShellModeForTab(ReaderTab? tab)
    {
        if (tab is null)
        {
            return;
        }

        var m = Shell.ModeFor(tab.Id);
        if (m == DocMode.Arrange || tab.Format.IsViewOnly())
        {
            m = DocMode.Read;
        }

        Shell.SetMode(m, tab.Id);
        ApplyShellMode();
    }

    private void SetLocalMode(DocMode m)
    {
        Shell.SetMode(m, Tab?.Id);
        ApplyShellMode();
    }

    private void CloseProtectCards()
    {
        if (_card is "stamp" or "password" or "cert")
        {
            CloseStampPreview();
            _card = null;
        }
    }

    // ── publishing to the shell ──────────────────────────────────────────
    private void PublishShell()
    {
        if (_shellGone || !Nav.ToBaseRelativePath(Nav.Uri).StartsWith("reader", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Shell.PendingTool is not null && Tab?.Session is not null)
        {
            _ = InvokeAsync(ConsumePendingToolAsync);
        }

        var tab = Tab;
        if (tab is null)
        {
            Shell.ClearCounter();
            Shell.SetDocumentMenu(null);
            Shell.UnregisterCommands(this);
            return;
        }

        if (tab.Format.IsViewOnly())
        {
            Shell.SetCounterStep(1);
            if (_viewerCounter is { } vc && vc.Total > 0)
            {
                Shell.SetCounter(vc.Current, vc.Total, vc.Unit);
            }
            else
            {
                Shell.ClearCounter();
            }

            Shell.SetActivity(_viewerStatus ?? $"{tab.Format.Label()} · read-only");
        }
        else if (tab.Session?.Document is { } d)
        {
            Shell.SetCounter(CurPage, d.PageCount, "page");
            Shell.SetCounterStep(PageStep);
            var activity = LocalMode switch
            {
                DocMode.Mark => $"Mark · {MarkCountLabel}{(OverlayCount > 0 ? " · unsaved" : "")}",
                DocMode.Protect => "Protect · always writes a new file",
                _ => $"Read · page {CurPage} of {d.PageCount}{(_sigReport is { HasSignatures: true } r ? " · " + r.Headline : "")}",
            };
            if (_measureOn)
            {
                activity += _msA is null ? " · Measure: click the first point" : _msB is null ? " · Measure: click the second point" : "";
            }

            Shell.SetActivity(activity);
        }
        else
        {
            Shell.ClearCounter();
            Shell.SetActivity(tab.IsLoading ? $"Opening {tab.FileName}…" : null);
        }

        Shell.SetDocumentMenu(DocumentMenuItems(tab).ToList());
        Shell.RegisterCommands(this, ReaderCommands(tab).ToList());
    }

    private async Task CounterGoAsync(int target)
    {
        var tab = Tab;
        if (tab is null)
        {
            return;
        }

        if (tab.Format.IsViewOnly())
        {
            if (_viewer is not null)
            {
                await _viewer.GoToAsync(target);
            }

            return;
        }

        await GoTo(target);
        StateHasChanged();
    }

    private static ShellCommand Sep(string id) => new("sep." + id, "", "", "", null, () => Task.CompletedTask);

    private IEnumerable<ShellCommand> DocumentMenuItems(ReaderTab tab)
    {
        var isPdf = !tab.Format.IsViewOnly();
        var notPdf = isPdf ? null : "Convert to PDF first";
        yield return new("doc.props", "Properties", "Document", "info", null, DocProperties, notPdf);
        yield return new("doc.export", "Export pages as images", "Document", "download", null, () => { DocExport(); return Task.CompletedTask; }, notPdf);
        yield return new("doc.press", "Reduce file size (Press)", "Document", "compress", null, () => { DocPress(); return Task.CompletedTask; }, notPdf, "compress");
        yield return new("doc.print", "Print", "Document", "print", "Ctrl P", () => { OpenCard("print"); return Task.CompletedTask; }, notPdf);
        yield return Sep("1");
        yield return new("doc.flatten", "Flatten forms & marks", "Document", "compress", null, DocFlatten, notPdf ?? (_flattening ? "Flattening…" : null));
        yield return new("doc.password", "Password-protect a copy", "Document", "doc-lock", null, () => { OpenProtectCard("password"); return Task.CompletedTask; }, notPdf);
        yield return Sep("2");
        yield return new("doc.merge", "Merge files…", "Document", "merge", null, () => { DocNav("/merge"); return Task.CompletedTask; }, null, "combine");
        yield return new("doc.split", "Split pages…", "Document", "split", null, () => { DocNav("/split"); return Task.CompletedTask; }, null, "extract");
        if (!isPdf)
        {
            yield return new("doc.convert", "Convert to PDF", "Document", "file-pdf", null, () => Task.CompletedTask, "Coming with the Layout library");
        }

        yield return Sep("3");
        // Real paths only exist where there is a file manager (desktop); a working copy's path means nothing to people.
        if (Platform.Capabilities.RevealInFolder)
        {
            yield return new("doc.reveal", "Show in folder", "Document", "folder", null, () => RevealAsync(tab.Path),
                FileManager.IsAvailable ? null : "Not available on this computer");
            yield return new("doc.copypath", "Copy file path", "Document", "copy", null, () => CopyTextAsync(tab.Path, "File path copied"));
        }

        yield return new("doc.tags", "Tags…", "Document", "tag", null, () => { Nav.NavigateTo("/library"); return Task.CompletedTask; }, null, "library category");
        yield return Sep("4");
        yield return new("doc.close", "Close", "Document", "close", "Ctrl W", CloseActiveTab);
    }

    private IEnumerable<ShellCommand> ReaderCommands(ReaderTab tab)
    {
        foreach (var c in DocumentMenuItems(tab).Where(c => !c.Id.StartsWith("sep", StringComparison.Ordinal) && c.Id != "doc.print"))
        {
            yield return c;
        }

        if (tab.Format.IsViewOnly())
        {
            yield return new("v.find", "Find in this file", "Read", "find-in-page", null, () => { OpenViewerFind(); return Task.CompletedTask; });
            yield return new("v.copy", "Copy all text", "Read", "copy", null, CopyAllViewerText);
            yield return new("v.outline", "Outline", "Read", "outline", null, () => { TogglePagesDrawer(); return Task.CompletedTask; }, _viewerOutline.Count == 0 ? "No headings in this file" : null);
            yield return new("v.view", "View options", "View", "sliders", null, () => { ToggleCard("view"); return Task.CompletedTask; });
            yield break;
        }

        Func<Task> T(Action a) => () => { a(); StateHasChanged(); return Task.CompletedTask; };

        // Read
        yield return new("r.pages", "Pages & outline", "Read", "page-single", null, T(TogglePagesDrawer), Keywords: "thumbnails bookmarks");
        yield return new("r.bookmark", "Add bookmark", "Read", "bookmark", "Ctrl B", T(AddBookmarkHere));
        yield return new("r.snapshot", "Snapshot a region", "Read", "snapshot", LocalMode == DocMode.Mark ? null : "S", () => SetTool(ReaderTool.Snapshot));
        yield return new("r.layers", "Layers", "Read", "skin-layer", null, T(() => ToggleCard("layers")), Keywords: "optional content ocg");
        yield return new("r.measure", "Measure", "Read", "measure-tool", LocalMode == DocMode.Mark ? null : "M", T(ToggleMeasure), Keywords: "distance ruler");
        yield return new("r.signatures", "Signatures", "Read", "certification", null, T(() => ToggleCard("signatures")), Keywords: "verify");
        yield return new("r.print", "Print", "Read", "print", "Ctrl P", T(() => OpenCard("print")));
        yield return new("r.immersive", "Immersive (full screen)", "Read", "eye", "F11", ToggleReading, Keywords: "reading fullscreen");
        yield return new("r.find", "Find in document", "Read", "find-in-page", null, OpenFindFromShell);

        // Mark
        var markOk = LocalMode == DocMode.Mark;
        foreach (var (tool, label, icon, key) in new[]
        {
            ("text", "Text", "text-box", "T"), ("rect", "Rectangle", "shape-rect", "B"), ("ellipse", "Ellipse", "shape-ellipse", "E"),
            ("line", "Line", "minus", "L"), ("arrow", "Arrow", "arrow-anno", "A"), ("freehand", "Draw", "freehand", "D"),
        })
        {
            var t = tool;
            yield return new("m." + t, "Mark: " + label, "Mark", icon, markOk ? key : null, T(() => { if (!markOk) SetLocalMode(DocMode.Mark); PickMkTool(t); }));
        }

        yield return new("m.image", "Mark: Image", "Mark", "file-image", markOk ? "I" : null, async () => { if (!markOk) SetLocalMode(DocMode.Mark); await PickImageTool(); });
        yield return new("m.sign", "Mark: Signature", "Mark", "signature", markOk ? "G" : null, T(() => { if (!markOk) SetLocalMode(DocMode.Mark); OpenSignature(); }));
        yield return new("m.highlight", "Mark: Highlight", "Mark", "highlight", null, () => Task.CompletedTask, "Coming soon");
        yield return new("m.note", "Mark: Note", "Mark", "sticky-note", null, () => Task.CompletedTask, "Coming soon");
        yield return new("m.form", "Fill form", "Mark", "form-field", null, () => Task.CompletedTask, "Coming soon");
        if (markOk)
        {
            yield return new("m.undo", "Undo mark", "Mark", "undo", null, T(MkUndo), _mkUndo.Count == 0 ? "Nothing to undo" : null);
            yield return new("m.redo", "Redo mark", "Mark", "redo", "Ctrl Y", T(MkRedo), _mkRedo.Count == 0 ? "Nothing to redo" : null);
            yield return new("m.save", "Save marks into this file", "Mark", "save", "Ctrl S", SaveInPlace, OverlayCount == 0 ? "Nothing to save" : null);
            yield return new("m.saveas", "Save as new PDF", "Mark", "download", "Ctrl Shift S", SaveMarkup, OverlayCount == 0 ? "Nothing to save" : null);
            yield return new("m.clear", "Clear all marks", "Mark", "trash", null, T(() => { MkSnap(); ClearMarkup(); }), OverlayCount == 0 ? "Nothing to clear" : null);
        }

        // Protect
        yield return new("p.redact", "Redact box", "Protect", "redact", LocalMode == DocMode.Protect ? "R" : null, () => GoRedact(false), Entitlements.CanRedact ? null : "Not in this edition");
        yield return new("p.find", "Find & redact", "Protect", "find-in-page", null, () => GoRedact(true), Entitlements.CanRedact ? null : "Not in this edition");
        yield return new("p.stamp", "Stamp (header & footer)", "Protect", "stamp", null, T(() => OpenProtectCard("stamp")));
        yield return new("p.watermark", "Watermark", "Protect", "watermark", null, T(() => OpenProtectCard("watermark")));
        yield return new("p.cert", "Sign with certificate", "Protect", "certification", null, T(() => OpenProtectCard("cert")), CertificateSignService.IsAvailable ? null : CertificateSignService.WaitingReason, "digital signature pfx");

        // View
        yield return new("v.zoomin", "Zoom in", "View", "zoom-in", null, T(ZoomIn));
        yield return new("v.zoomout", "Zoom out", "View", "zoom-out", null, T(ZoomOut));
        yield return new("v.fitw", "Fit width", "View", "fit-width", markOk ? null : "W", FitWidth);
        yield return new("v.fitp", "Fit page", "View", "fit-page", markOk ? null : "P", FitPage);
        yield return new("v.actual", "Actual size (100%)", "View", "actual-size", null, T(ActualSize));
        yield return new("v.single", "Single page", "View", "page-single", null, () => SetColumns(false));
        yield return new("v.two", "Two-page", "View", "page-facing", null, () => SetColumns(true));
        yield return new("v.cover", "Cover alone", "View", "page-cover", null, CoverLayout);
        yield return new("v.continuous", "Continuous scroll", "View", "page-continuous", null, ToggleContinuous);
        yield return new("v.rotr", "Rotate view right", "View", "rotate-cw", "Ctrl ]", T(() => RotateScopeBy(90)));
        yield return new("v.rotl", "Rotate view left", "View", "rotate-ccw", "Ctrl [", T(() => RotateScopeBy(-90)));
        yield return new("v.hand", "Hand (pan)", "View", "hand", markOk ? null : "H", () => SetTool(ReaderTool.Hand));
        yield return new("v.marquee", "Marquee zoom", "View", "zoom-area", markOk ? null : "Z", () => SetTool(ReaderTool.Marquee));
        yield return new("v.loupe", "Loupe", "View", "loupe", markOk ? null : "L", () => SetTool(ReaderTool.Loupe));
        yield return new("v.lock", "Lock scrolling", "View", "lock", null, T(ToggleScrollLock));
        yield return new("v.colour", "Page colour…", "View", "contrast", null, T(() => OpenCard("view")));
        yield return new("v.options", "View options", "View", "sliders", null, T(() => ToggleCard("view")));
    }

    private async Task CopyTextAsync(string text, string done)
    {
        try
        {
            await TextClip.SetTextAsync(text);
            Shell.Notify(done);
        }
        catch (Exception ex)
        {
            Shell.Notify("Couldn't copy: " + ex.Message, isError: true);
        }
    }

    // ── drawer ───────────────────────────────────────────────────────────
    private void TogglePagesDrawer()
    {
        _pagesDrawer = !_pagesDrawer;
        if (_pagesDrawer && Tab is { } t && !t.Format.IsViewOnly())
        {
            EnsureBookmarksLoaded();
            RenderThumbsNear();
        }
    }

    private void SetDrawerTab(string t)
    {
        _drawerTab = t;
        if (t == "pages")
        {
            RenderThumbsNear();
        }
    }

    private void EnsureBookmarksLoaded()
    {
        var path = Tab?.Path;
        if (path is null || _bmLoadedFor == path)
        {
            return;
        }

        _bmLoadedFor = path;
        LoadBookmarks();
    }

    /// <summary>Thumbnails reuse the page renders; only pages near the current one are
    /// rendered on demand so a large or heavy file doesn't render everything at once.</summary>
    private void RenderThumbsNear()
    {
        var d = Tab?.Session?.Document;
        if (d is null)
        {
            return;
        }

        var from = Math.Max(0, CurPage - 1 - 6);
        var to = Math.Min(d.PageCount - 1, CurPage - 1 + 6);
        for (var i = from; i <= to; i++)
        {
            EnsureRendered(i);
        }
    }

    private string ThumbAspect => "1 / 1.414";

    private IEnumerable<BookmarkNode> ChaptersAt(int pageIndex)
        => _bmTree is null ? Enumerable.Empty<BookmarkNode>() : _bmTree.Where(b => b.PageIndex == pageIndex);

    private void AddBookmarkHere()
    {
        if (Tab?.Session is null)
        {
            return;
        }

        EnsureBookmarksLoaded();
        BmAddCurrent();
        _pagesDrawer = true;
        _drawerTab = "outline";
        Shell.Notify($"Bookmark added at page {CurPage} — rename it, then Save bookmarks.");
    }

    // ── cards ────────────────────────────────────────────────────────────
    private void OpenCard(string card)
    {
        if (_card == "stamp" && card != "stamp")
        {
            CloseStampPreview();
        }

        _card = card;
    }

    private void ToggleCard(string card)
    {
        if (_card == card)
        {
            CloseCard();
        }
        else
        {
            OpenCard(card);
        }
    }

    private void CloseCard()
    {
        if (_card == "stamp")
        {
            CloseStampPreview();
        }

        _card = null;
    }

    /// <summary>
    /// The system Back gesture (Android; hosts without one never raise it): closes the open card
    /// (measure included) exactly as Escape does, and consumes the gesture. With no card open it
    /// returns false, so the host does its default (the previous screen, then leave the app).
    /// </summary>
    private bool OnBackRequested()
    {
        if (!_measureOn && _card is null)
        {
            return false;
        }

        _ = InvokeAsync(() =>
        {
            if (_measureOn)
            {
                CloseMeasure();
            }
            else
            {
                CloseCard();
            }

            StateHasChanged();
        });
        return true;
    }

    private void OpenProtectCard(string what)
    {
        if (LocalMode != DocMode.Protect)
        {
            SetLocalMode(DocMode.Protect);
        }

        if (what is "stamp" or "watermark")
        {
            OpenStamp(what);
        }
        else
        {
            OpenCard(what);
        }

        StateHasChanged();
    }

    private void OpenStamp(string focus)
    {
        _stampFocus = focus;
        if (Tab is { } t)
        {
            t.StampOpen = true;
        }

        _stampStatus = null;
        _card = "stamp";
    }

    private void CloseStamp() => CloseCard();

    private void CloseStampPreview()
    {
        if (Tab is { } t)
        {
            t.StampOpen = false;
        }
    }

    private Task OnCardMessage(string message)
    {
        var isError = message.StartsWith("Couldn't", StringComparison.OrdinalIgnoreCase)
                      || message.StartsWith("Could not", StringComparison.OrdinalIgnoreCase)
                      || message.Contains("failed", StringComparison.OrdinalIgnoreCase)
                      || message.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
        Shell.Notify(message, isError);
        return Task.CompletedTask;
    }

    private Task OnLayersChanged()
    {
        // The session dropped its render cache; re-attach the observers so visible pages render again.
        _rendering.Clear();
        _loadGen++;
        RenderThumbsNear();
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task OnSigReport(SignatureCheckReport report)
    {
        _sigReport = report;
        return Task.CompletedTask;
    }

    private async Task CheckSignaturesAsync(ReaderTab? tab)
    {
        if (tab is null || tab.Format.IsViewOnly() || tab.Path == _sigCheckedFor)
        {
            return;
        }

        _sigCheckedFor = tab.Path;
        try
        {
            var report = await SigCheck.CheckAsync(tab.Path);
            if (Tab?.Path == tab.Path)
            {
                _sigReport = report;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch
        {
            // a signature check is a nicety; never block reading
        }
    }

    private async Task RevealAsync(string path)
    {
        if (!await FileManager.RevealAsync(path))
        {
            Shell.Notify("Couldn't show the file in its folder", isError: true);
        }
    }

    private async Task OnProtectedSaved(string output)
    {
        try
        {
            await WriteBack.DeliverOrThrowAsync(output);
        }
        catch (Exception ex)
        {
            Shell.Notify(ex.Message, isError: true);
            return;
        }

        var name = System.IO.Path.GetFileName(output);
        Shell.Notify($"Saved → {name}", false, "Open", () => OpenOutputAsync(output));
        await Task.CompletedTask;
    }

    private async Task OpenOutputAsync(string output)
    {
        var name = System.IO.Path.GetFileName(output);
        var format = DocumentFormatExtensions.FromPath(output);
        await Recents.TouchAsync(new RecentItem(name, output, format, DateTimeOffset.Now, false));
        await Tabs.OpenAsync(output, name, format);
    }

    // ── certificate signing ─────────────────────────────────────────────
    private async Task PickCert()
    {
        var p = await Picker.PickCertificateAsync();
        if (string.IsNullOrEmpty(p))
        {
            if (p is null)
            {
                Shell.Notify("Choosing a certificate isn't available here.", isError: true);
            }

            return;
        }

        _certPath = p;
        _certSummary = null;
    }

    private async Task SignWithCertificate()
    {
        var source = Tab?.Path;
        if (source is null || _certPath is null || _certBusy)
        {
            return;
        }

        try
        {
            var who = CertSign.InspectPfx(_certPath, _certPwd);
            _certSummary = $"{who.SubjectName} · issued by {who.IssuerName}";
        }
        catch (Exception ex)
        {
            Shell.Notify("Couldn't read the certificate: " + ex.Message, isError: true);
            return;
        }

        var output = await Picker.PickSavePdfAsync(System.IO.Path.GetFileNameWithoutExtension(source) + " (signed).pdf");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        _certBusy = true;
        StateHasChanged();
        try
        {
            await CertSign.SignWithPfxAsync(source, output, _certPath, _certPwd, new CertificateSignOptions
            {
                Reason = string.IsNullOrWhiteSpace(_certReason) ? null : _certReason,
                Location = string.IsNullOrWhiteSpace(_certLocation) ? null : _certLocation,
            });
            _certPwd = string.Empty;
            await OnProtectedSaved(output);
        }
        catch (Exception ex)
        {
            Shell.Notify("Couldn't sign: " + ex.Message, isError: true);
        }
        finally
        {
            _certBusy = false;
            StateHasChanged();
        }
    }

    // ── Protect → redaction workspace ────────────────────────────────────
    private async Task GoRedact(bool find)
    {
        if (!Entitlements.CanRedact)
        {
            Shell.Notify("Redaction isn't included in this edition.", isError: true);
            return;
        }

        Shell.SetMode(DocMode.Protect, Tab?.Id);
        RedactHandoff.OpenFind = find;
        await SendToRedact();
    }

    // ── Mark helpers ─────────────────────────────────────────────────────
    private void PickMkTool(string tool)
    {
        _sigOpen = false;
        SetMkTool(tool);
    }

    private async Task PickImageTool()
    {
        _sigOpen = false;
        SetMkTool("image");
        await AddImageViaPicker();
    }

    private string MarkCountLabel =>
        $"{TextCount} text · {ShapeCount} shape{(ShapeCount == 1 ? "" : "s")} · {ImageCount} image{(ImageCount == 1 ? "" : "s")}";

    private static string SelMeta<T>(int page, Dictionary<int, List<T>> map, T item)
    {
        if (page < 0 || !map.TryGetValue(page, out var list))
        {
            return string.Empty;
        }

        var i = list.IndexOf(item);
        return $"p {page + 1} · {i + 1} of {list.Count}";
    }

    private void DuplicateSelected()
    {
        MkSnap();
        const double d = 0.02;
        if (_selText is { } t && _texts.TryGetValue(_selTextPage, out var tl))
        {
            var c = CloneText(t);
            c.X = Math.Min(1 - c.W, c.X + d);
            c.Y = Math.Min(1 - c.H, c.Y + d);
            tl.Add(c);
            _selText = c;
        }
        else if (_selShape is { } s && _shapes.TryGetValue(_selShapePage, out var sl))
        {
            var c = CloneShape(s);
            c.X += d;
            c.Y += d;
            if (c.Points is not null)
            {
                c.Points = c.Points.Select(p => (p.X + d, p.Y + d)).ToList();
            }

            sl.Add(c);
            _selShape = c;
        }
        else if (_selImage is { } im && _images.TryGetValue(_selImagePage, out var il))
        {
            var c = CloneImage(im);
            c.X = Math.Min(1 - c.W, c.X + d);
            c.Y = Math.Min(1 - c.H, c.Y + d);
            il.Add(c);
            _selImage = c;
        }
    }

    private static TextBox CloneText(TextBox t) => new()
    {
        X = t.X, Y = t.Y, W = t.W, H = t.H, Text = t.Text, FontFamily = t.FontFamily, Bold = t.Bold, Italic = t.Italic,
        FontSizePt = t.FontSizePt, ColorHex = t.ColorHex, Align = t.Align, Rotation = t.Rotation, LineHeight = t.LineHeight,
    };

    private static ShapeBox CloneShape(ShapeBox s) => new()
    {
        Kind = s.Kind, X = s.X, Y = s.Y, W = s.W, H = s.H, FillHex = s.FillHex, StrokeHex = s.StrokeHex,
        StrokeWidthPt = s.StrokeWidthPt, Rotation = s.Rotation, BehindContent = s.BehindContent,
        Points = s.Points?.ToList(),
    };

    private static ImageBox CloneImage(ImageBox i) => new()
    {
        X = i.X, Y = i.Y, W = i.W, H = i.H, Bytes = i.Bytes, DataUrl = i.DataUrl, Opacity = i.Opacity, Lock = i.Lock,
        Aspect = i.Aspect, Rotation = i.Rotation, BehindContent = i.BehindContent,
    };

    private MkState MkCapture()
    {
        var texts = _texts.ToDictionary(kv => kv.Key, kv => kv.Value.Select(CloneText).ToList());
        var shapes = _shapes.ToDictionary(kv => kv.Key, kv => kv.Value.Select(CloneShape).ToList());
        var images = _images.ToDictionary(kv => kv.Key, kv => kv.Value.Select(CloneImage).ToList());
        return new MkState(texts, shapes, images, MkSignature());
    }

    private string MkSignature()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (p, l) in _texts.OrderBy(k => k.Key))
        {
            foreach (var t in l)
            {
                sb.Append('t').Append(p).Append(t.X).Append(',').Append(t.Y).Append(',').Append(t.W).Append(',').Append(t.H)
                  .Append(t.Text).Append(t.FontFamily).Append(t.Bold).Append(t.Italic).Append(t.FontSizePt).Append(t.ColorHex)
                  .Append(t.Align).Append(t.Rotation).Append('|');
            }
        }

        foreach (var (p, l) in _shapes.OrderBy(k => k.Key))
        {
            foreach (var s in l)
            {
                sb.Append('s').Append(p).Append(s.Kind).Append(s.X).Append(',').Append(s.Y).Append(',').Append(s.W).Append(',').Append(s.H)
                  .Append(s.FillHex).Append(s.StrokeHex).Append(s.StrokeWidthPt).Append(s.Rotation).Append(s.BehindContent)
                  .Append(s.Points?.Count ?? 0).Append('|');
            }
        }

        foreach (var (p, l) in _images.OrderBy(k => k.Key))
        {
            foreach (var i in l)
            {
                sb.Append('i').Append(p).Append(i.X).Append(',').Append(i.Y).Append(',').Append(i.W).Append(',').Append(i.H)
                  .Append(i.Opacity).Append(i.Rotation).Append(i.BehindContent).Append(i.Bytes.Length).Append('|');
            }
        }

        return sb.ToString();
    }

    /// <summary>Push the current marks onto the undo stack (call before a change).</summary>
    private void MkSnap()
    {
        var s = MkCapture();
        if (_mkUndo.Count > 0 && _mkUndo.Peek().Signature == s.Signature)
        {
            return;
        }

        _mkUndo.Push(s);
        _mkRedo.Clear();
        if (_mkUndo.Count > 100)
        {
            var keep = _mkUndo.Take(100).Reverse().ToList();
            _mkUndo.Clear();
            foreach (var k in keep)
            {
                _mkUndo.Push(k);
            }
        }
    }

    /// <summary>Pointer edits: remember the state when the gesture starts…</summary>
    private void MkBeginGesture() => _pendingSnap ??= MkCapture();

    /// <summary>…and commit it to the undo stack only if the gesture changed something.</summary>
    private void MkEndGesture()
    {
        var before = _pendingSnap;
        _pendingSnap = null;
        if (before is null || before.Signature == MkSignature())
        {
            return;
        }

        _mkUndo.Push(before);
        _mkRedo.Clear();
    }

    private void MkRestore(MkState s)
    {
        void Copy<T>(Dictionary<int, List<T>> target, Dictionary<int, List<T>> src)
        {
            target.Clear();
            foreach (var (k, v) in src)
            {
                target[k] = v;
            }
        }

        Copy(_texts, s.Texts);
        Copy(_shapes, s.Shapes);
        Copy(_images, s.Images);
        _selText = null;
        _selShape = null;
        _selImage = null;
    }

    private void MkUndo()
    {
        if (_mkUndo.Count == 0)
        {
            return;
        }

        _mkRedo.Push(MkCapture());
        MkRestore(_mkUndo.Pop());
    }

    private void MkRedo()
    {
        if (_mkRedo.Count == 0)
        {
            return;
        }

        _mkUndo.Push(MkCapture());
        MkRestore(_mkRedo.Pop());
    }

    // ── Measure ──────────────────────────────────────────────────────────
    private void ToggleMeasure()
    {
        _measureOn = !_measureOn;
        _msA = _msB = null;
        _msPage = -1;
        if (_measureOn)
        {
            if (_markup)
            {
                SetLocalMode(DocMode.Read);
            }

            _card = "measure";
        }
        else if (_card == "measure")
        {
            _card = null;
        }
    }

    private void CloseMeasure()
    {
        _measureOn = false;
        _msA = _msB = null;
        _msPage = -1;
        _card = null;
    }

    private void ClearMeasure()
    {
        _msA = _msB = null;
        _measure = null;
        _msPage = -1;
    }

    private async Task MsDown(PointerEventArgs e, int page)
    {
        if (!_measureOn || e.Button != 0)
        {
            return;
        }

        if (!await CacheLayerRect(page))
        {
            return;
        }

        var p = new PagePoint(Clamp01((e.ClientX - _mkRect.L) / _mkRect.W), Clamp01((e.ClientY - _mkRect.T) / _mkRect.H));
        if (_msA is null || _msB is not null || _msPage != page)
        {
            _msPage = page;
            _msA = p;
            _msB = null;
            _measure = null;
            return;
        }

        _msB = p;
        var session = Tab?.Session;
        if (session is not null)
        {
            try
            {
                _measure = Measurer.Measure(session, page, _msA.Value, p, RotationFor(page));
            }
            catch (Exception ex)
            {
                Shell.Notify("Couldn't measure: " + ex.Message, isError: true);
            }
        }

        _card = "measure";
    }

    // ── view-only viewers ────────────────────────────────────────────────
    private Task OnViewerCounter(ViewerCounter c)
    {
        _viewerCounter = c;
        PublishShell();
        return Task.CompletedTask;
    }

    private Task OnViewerOutline(IReadOnlyList<ViewerOutlineEntry> outline)
    {
        _viewerOutline = outline;
        return Task.CompletedTask;
    }

    private Task OnViewerFindCount(int n)
    {
        _viewerFindCount = n;
        return Task.CompletedTask;
    }

    private Task OnViewerStatus(string status)
    {
        _viewerStatus = status;
        PublishShell();
        return Task.CompletedTask;
    }

    private async Task ViewerGoAnchor(string anchor)
    {
        if (_viewer is not null)
        {
            await _viewer.ScrollToAnchorAsync(anchor);
        }
    }

    private void OpenViewerFind()
    {
        _viewerFindOpen = true;
    }

    private void CloseViewerFind()
    {
        _viewerFindOpen = false;
        _viewerFind = null;
        _viewerFindInput = string.Empty;
        _viewerFindCount = 0;
    }

    private void OnViewerFindKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            _viewerFind = string.IsNullOrWhiteSpace(_viewerFindInput) ? null : _viewerFindInput.Trim();
        }
        else if (e.Key == "Escape")
        {
            CloseViewerFind();
        }
    }

    private async Task CopyAllViewerText()
    {
        if (_viewer is null)
        {
            return;
        }

        await CopyTextAsync(_viewer.GetAllText(), "Copied all text");
    }

    // ── misc rail actions ────────────────────────────────────────────────
    private async Task CoverLayout()
    {
        if (Tab is not { } t)
        {
            return;
        }

        if (!t.TwoPage)
        {
            await SetColumns(true);
        }

        if (!t.CoverAlone)
        {
            ToggleCover();
        }
    }

    private async Task OpenFindFromShell()
    {
        if (Tab is { } t && t.Format.IsViewOnly())
        {
            OpenViewerFind();
            StateHasChanged();
            return;
        }

        await OpenFind();
        StateHasChanged();
    }
}
