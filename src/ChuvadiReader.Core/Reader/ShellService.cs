namespace ChuvadiReader.Core.Reader;

/// <summary>The four modes of a PDF tab (D-10 left bar).</summary>
public enum DocMode
{
    Read,
    Mark,
    Arrange,
    Protect,
}

/// <summary>One entry in the Ctrl K command bar.</summary>
/// <param name="Id">Stable id (used to de-duplicate and for tests).</param>
/// <param name="Label">What people see and search.</param>
/// <param name="Group">Section heading in the command bar (Go to, Document, View, Mark, …).</param>
/// <param name="Icon">LiPicons key.</param>
/// <param name="Keys">Shortcut hint, e.g. "Ctrl P"; null for none.</param>
/// <param name="Run">The action.</param>
/// <param name="DisabledReason">When not null the command is shown greyed with this reason.</param>
/// <param name="Keywords">Extra search words.</param>
public sealed record ShellCommand(
    string Id,
    string Label,
    string Group,
    string Icon,
    string? Keys,
    Func<Task> Run,
    string? DisabledReason = null,
    string? Keywords = null)
{
    public bool IsEnabled => DisabledReason is null;

    /// <summary>Shown greyed out although it runs: running it tells the person why the action is off here
    /// (a touch screen has no tooltip to carry <see cref="DisabledReason"/>; D-187).</summary>
    public bool Dimmed { get; init; }
}

/// <summary>A message shown bottom-right after an action (States &amp; Feedback).</summary>
public sealed record ShellMessage(
    Guid Id,
    string Text,
    bool IsError,
    string? ActionLabel,
    Func<Task>? Action);

/// <summary>
/// Shared state for the D-10 shell: which mode the active tab is in, the page counter the
/// top bar shows, the Document menu, the Ctrl K command registry, the status-bar activity
/// line and messages. Pages (Reader, Arrange, Protect) publish into it; the layout reads it.
/// Host-agnostic: no UI or JS here.
/// </summary>
public sealed class ShellService
{
    private readonly Dictionary<string, DocMode> _tabModes = new();
    private readonly Dictionary<object, IReadOnlyList<ShellCommand>> _commands = new();
    private readonly List<ShellMessage> _messages = new();
    private readonly object _gate = new();

    /// <summary>Raised whenever anything the shell chrome shows changes.</summary>
    public event Action? Changed;

    // ── Mode ──────────────────────────────────────────────────────────────

    /// <summary>The current mode of the active document view.</summary>
    public DocMode Mode { get; private set; } = DocMode.Read;

    /// <summary>The page that handles mode requests while it is mounted (the Reader).
    /// Returns true when it handled the request.</summary>
    public Func<DocMode, Task<bool>>? ModeHandler { get; set; }

    /// <summary>Remember the mode for a tab and make it current.</summary>
    public void SetMode(DocMode mode, string? tabId = null)
    {
        if (tabId is not null)
        {
            lock (_gate)
            {
                _tabModes[tabId] = mode;
            }
        }

        if (Mode != mode)
        {
            Mode = mode;
            Changed?.Invoke();
        }
    }

    /// <summary>The last mode a tab was in (Read when unknown).</summary>
    public DocMode ModeFor(string? tabId)
    {
        lock (_gate)
        {
            return tabId is not null && _tabModes.TryGetValue(tabId, out var m) ? m : DocMode.Read;
        }
    }

    /// <summary>Forget a closed tab's mode.</summary>
    public void ForgetTab(string tabId)
    {
        lock (_gate)
        {
            _tabModes.Remove(tabId);
        }
    }

    /// <summary>The verb used on Home resume cards: Reading, Marking, Arranging, Protecting.</summary>
    public static string Gerund(DocMode mode) => mode switch
    {
        DocMode.Mark => "Marking",
        DocMode.Arrange => "Arranging",
        DocMode.Protect => "Protecting",
        _ => "Reading",
    };

    // ── Top-bar counter (‹ 2 / 13 ›) ──────────────────────────────────────

    public int CounterCurrent { get; private set; }

    public int CounterTotal { get; private set; }

    /// <summary>"page", "section", "line" or "sheet".</summary>
    public string CounterUnit { get; private set; } = "page";

    public bool HasCounter => CounterTotal > 0;

    /// <summary>How far ‹ and › move (2 in a two-page spread).</summary>
    public int CounterStep { get; private set; } = 1;

    public void SetCounterStep(int step)
    {
        step = Math.Max(1, step);
        if (step != CounterStep)
        {
            CounterStep = step;
            Changed?.Invoke();
        }
    }

    /// <summary>D-190: set by the Reader page while it is shown. Saves the active tab's unsaved marks, into the
    /// file (false) or as a new PDF the person picks (true). Returns null when they are saved, an empty string
    /// when the person backed out of the save, or the reason it failed.</summary>
    public Func<bool, Task<string?>>? SaveMarksHandler { get; set; }

    /// <summary>D-195: set by the Reader page while it is shown. Saves the active tab's edited bookmarks into the
    /// file, as "Save bookmarks" does. Returns null when they are saved, otherwise why not.</summary>
    public Func<Task<string?>>? SaveBookmarksHandler { get; set; }

    /// <summary>D-198: set by the layout. Shows what a page operation will leave out of each file and returns
    /// whether the person chose to go on.</summary>
    public Func<IReadOnlyList<ChuvadiReader.Core.Documents.PageOperationFile>, Task<bool>>? PageOperationAskHandler { get; set; }

    private readonly HashSet<string> _pageOpsAccepted = new(StringComparer.Ordinal);

    /// <summary>D-198, until Pdf 3.35.1: call before every page operation (Merge, Split and extract, anything
    /// Arrange writes) with the files it takes pages from. Every file is asked about, naming what the result
    /// will not have and whether it has layers; a file the person agreed to is not asked about again while the
    /// app runs. True means go on.</summary>
    public async Task<bool> ConfirmPageOperationAsync(IEnumerable<string?> paths)
    {
        var list = paths.Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).Distinct(StringComparer.Ordinal).ToList();
        lock (_pageOpsAccepted) list.RemoveAll(_pageOpsAccepted.Contains);
        if (list.Count == 0 || PageOperationAskHandler is not { } handler) return true;

        var files = await Task.Run(() => list.Select(ChuvadiReader.Core.Documents.PageOperationCheck.Inspect).ToList()).ConfigureAwait(false);
        if (!await handler(files).ConfigureAwait(false)) return false;
        lock (_pageOpsAccepted) foreach (var path in list) _pageOpsAccepted.Add(path);
        return true;
    }

    /// <summary>Set by the page that owns the counter; called with a 1-based target.</summary>
    public Func<int, Task>? CounterHandler { get; set; }

    public void SetCounter(int current, int total, string unit = "page")
    {
        if (current == CounterCurrent && total == CounterTotal && unit == CounterUnit)
        {
            return;
        }

        CounterCurrent = current;
        CounterTotal = total;
        CounterUnit = unit;
        Changed?.Invoke();
    }

    public void ClearCounter() => SetCounter(0, 0);

    public Task GoToAsync(int target)
    {
        if (CounterHandler is null || CounterTotal <= 0)
        {
            return Task.CompletedTask;
        }

        target = Math.Clamp(target, 1, CounterTotal);
        return CounterHandler(target);
    }

    // ── Document menu ─────────────────────────────────────────────────────

    /// <summary>Items for the top-bar Document ▾ menu, published by the active page.</summary>
    public IReadOnlyList<ShellCommand> DocumentMenu { get; private set; } = Array.Empty<ShellCommand>();

    public void SetDocumentMenu(IReadOnlyList<ShellCommand>? items)
    {
        DocumentMenu = items ?? Array.Empty<ShellCommand>();
        Changed?.Invoke();
    }

    // ── Global commands ───────────────────────────────────────────────────

    private string _globalSignature = string.Empty;

    /// <summary>The shell's own commands (Open, Settings, Go to, modes, themes, tabs) as MainLayout
    /// last published them — the ones the Ctrl K bar lists before the page commands. Hosts read them
    /// through IAppCommands for a native menu bar (Mac Test M-01, M-11).</summary>
    public IReadOnlyList<ShellCommand> GlobalCommands { get; private set; } = Array.Empty<ShellCommand>();

    /// <summary>Publishes the shell's own commands. Raises <see cref="Changed"/> only when an id, label,
    /// shortcut or disabled reason differs, so calling it on every render can't loop.</summary>
    public void SetGlobalCommands(IReadOnlyList<ShellCommand>? commands)
    {
        var list = commands ?? Array.Empty<ShellCommand>();
        var signature = string.Join('\n', list.Select(c => $"{c.Id}\t{c.Label}\t{c.Keys}\t{c.DisabledReason}"));
        if (signature == _globalSignature)
        {
            GlobalCommands = list; // fresh delegates, same content
            return;
        }

        _globalSignature = signature;
        GlobalCommands = list;
        Changed?.Invoke();
    }

    // ── Ctrl K ────────────────────────────────────────────────────────────

    /// <summary>Publish (or replace) the commands an owner contributes.</summary>
    public void RegisterCommands(object owner, IReadOnlyList<ShellCommand> commands)
    {
        lock (_gate)
        {
            _commands[owner] = commands;
        }

        Changed?.Invoke();
    }

    public void UnregisterCommands(object owner)
    {
        bool removed;
        lock (_gate)
        {
            removed = _commands.Remove(owner);
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Every registered command, de-duplicated by id (the latest registration wins).</summary>
    public IReadOnlyList<ShellCommand> Commands
    {
        get
        {
            var byId = new Dictionary<string, ShellCommand>();
            List<IReadOnlyList<ShellCommand>> lists;
            lock (_gate)
            {
                lists = _commands.Values.ToList();
            }

            foreach (var list in lists)
            {
                foreach (var c in list)
                {
                    byId[c.Id] = c;
                }
            }

            return byId.Values.ToList();
        }
    }

    /// <summary>Simple ranked search: label prefix, then word prefix, then substring, then keywords.</summary>
    public static IReadOnlyList<ShellCommand> Search(IEnumerable<ShellCommand> commands, string? query)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return commands.ToList();
        }

        static int Score(ShellCommand c, string q)
        {
            var label = c.Label;
            if (label.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            foreach (var w in label.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (w.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                {
                    return 1;
                }
            }

            if (label.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if ((c.Keywords?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || c.Group.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            return -1;
        }

        return commands
            .Select(c => (c, s: Score(c, q)))
            .Where(x => x.s >= 0)
            .OrderBy(x => x.s)
            .ThenBy(x => x.c.IsEnabled ? 0 : 1)
            .ThenBy(x => x.c.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.c)
            .ToList();
    }

    /// <summary>Raised to open the command bar (Ctrl K from any page).</summary>
    public event Action? CommandBarRequested;

    public void OpenCommandBar() => CommandBarRequested?.Invoke();

    /// <summary>A Protect tool the Reader should open when it next mounts (set by the
    /// redaction view: "stamp", "watermark", "flatten", "password" or "cert").</summary>
    public string? PendingTool { get; set; }

    /// <summary>Set by the page that owns Find (Ctrl F / left bar Find).</summary>
    public Func<Task>? FindHandler { get; set; }

    /// <summary>Set by the page that can search the open document for given text (the top-bar
    /// command field offers "Find “…” in this document").</summary>
    public Func<string, Task>? FindTextHandler { get; set; }

    // ── Status bar + messages ─────────────────────────────────────────────

    /// <summary>Activity text in the status bar, e.g. "Read · page 2 of 13".</summary>
    public string Activity { get; private set; } = string.Empty;

    public void SetActivity(string? text)
    {
        text ??= string.Empty;
        if (text != Activity)
        {
            Activity = text;
            Changed?.Invoke();
        }
    }

    /// <summary>Long-job progress 0..1 shown in the status bar; null when idle.</summary>
    public double? Progress { get; private set; }

    public string? ProgressLabel { get; private set; }

    public void SetProgress(double? progress, string? label = null)
    {
        Progress = progress is null ? null : Math.Clamp(progress.Value, 0, 1);
        ProgressLabel = progress is null ? null : label;
        Changed?.Invoke();
    }

    public IReadOnlyList<ShellMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToList();
            }
        }
    }

    /// <summary>Show a message bottom-right. Success messages fade after 6 s, errors and messages with an action
    /// after 15 s (the layout times them, UI31); at most three are shown at once.</summary>
    public ShellMessage Notify(string text, bool isError = false, string? actionLabel = null, Func<Task>? action = null)
    {
        var m = new ShellMessage(Guid.NewGuid(), text, isError, actionLabel, action);
        lock (_gate)
        {
            _messages.Add(m);
            if (_messages.Count > 3)
            {
                _messages.RemoveAt(0);
            }
        }

        Changed?.Invoke();
        return m;
    }

    public void Dismiss(Guid id)
    {
        int removed;
        lock (_gate)
        {
            removed = _messages.RemoveAll(m => m.Id == id);
        }

        if (removed > 0)
        {
            Changed?.Invoke();
        }
    }
}
