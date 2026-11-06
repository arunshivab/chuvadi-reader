namespace ChuvadiReader.Platform;

// ─────────────────────────────────────────────────────────────────────────────
// File model (decision pending: option C "working path + write-back", see PLATFORM-INTERFACE.md §3).
//
// Core works on local file paths today, and the existing IFilePicker returns paths. Every host
// therefore guarantees that what it hands to Core is a readable local path:
//   * Windows / macOS / Linux: the real path.
//   * Android: a working copy in the app's private storage, made from the content URI.
//   * Web: a working copy in the signed-in person's encrypted, per-user server storage.
// After Core or Ui writes a file the person chose (Save, Save as, export to a folder), it calls
// IFileWriteBack.CommitAsync(path). Desktop hosts do nothing; Android writes the bytes back to
// the URI; Web stores the file and/or starts the browser download.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Outcome of <see cref="IFileWriteBack.CommitAsync"/>.</summary>
public enum WriteBackOutcome
{
    /// <summary>Nothing to do: the path is the real file (desktop hosts).</summary>
    NotNeeded,

    /// <summary>The bytes reached their destination.</summary>
    Done,

    /// <summary>The person cancelled (e.g. a browser download prompt).</summary>
    Cancelled,

    /// <summary>The destination refused the write. <see cref="WriteBackResult.Message"/> is user-facing.</summary>
    Failed,
}

/// <summary>Result of a write-back. <paramref name="Message"/> is short and user-facing.</summary>
public sealed record WriteBackResult(WriteBackOutcome Outcome, string? Message = null)
{
    public static readonly WriteBackResult NotNeeded = new(WriteBackOutcome.NotNeeded);

    public bool Succeeded => Outcome is WriteBackOutcome.NotNeeded or WriteBackOutcome.Done;
}

/// <summary>
/// Sends a file Chuvadi has just written at a local path back to where the person meant it to go.
/// Call it after every write to a path that came from a picker or from an opened document.
/// </summary>
public interface IFileWriteBack
{
    /// <summary>False on hosts where every path is the real file (desktop). Callers may skip the call.</summary>
    bool IsNeeded { get; }

    /// <summary>Commits one file, or every file inside a folder path returned by a folder picker.</summary>
    Task<WriteBackResult> CommitAsync(string localPath, CancellationToken ct = default);
}

/// <summary>Host default for desktop: paths are real files.</summary>
public sealed class NoFileWriteBack : IFileWriteBack
{
    public bool IsNeeded => false;

    public Task<WriteBackResult> CommitAsync(string localPath, CancellationToken ct = default) =>
        Task.FromResult(WriteBackResult.NotNeeded);
}

/// <summary>Folders the app uses. Replaces direct use of Environment.SpecialFolder in Core and Ui.</summary>
public interface IAppPaths
{
    /// <summary>Settings, sessions, recovery drafts. Per user on Web.</summary>
    string AppDataFolder { get; }

    /// <summary>Scratch files (renders, intermediate PDFs). Per user on Web; cleared by the host.</summary>
    string WorkingFolder { get; }

    /// <summary>Suggested default save folder, or null when the host has no visible file system (Web).</summary>
    string? DefaultSaveFolder { get; }
}

/// <summary>Who is using the app. Desktop: the local account name, never signed in until Sangam is added.</summary>
public interface IAccount
{
    bool IsSignedIn { get; }

    /// <summary>Name for the user chip, e.g. "Arun S.".</summary>
    string DisplayName { get; }

    /// <summary>Sangam ID when signed in.</summary>
    string? Email { get; }

    /// <summary>Organisation unit shown after the Sangam ID ("arun@apulki · Records", design 12b), or null.
    /// Default null, so existing implementations need no change.</summary>
    string? Unit => null;

    event Action? Changed;

    /// <summary>Starts Sangam sign-in. Returns false where sign-in isn't available yet.</summary>
    Task<bool> SignInAsync(CancellationToken ct = default);

    Task SignOutAsync(CancellationToken ct = default);
}

/// <summary>Text and image clipboard. Supersedes IImageClipboard and the JS navigator.clipboard call.</summary>
public interface IClipboard
{
    bool CanCopyText { get; }

    bool CanCopyImage { get; }

    Task SetTextAsync(string text, CancellationToken ct = default);

    /// <summary>Copies a PNG image (raw bytes).</summary>
    Task SetImageAsync(byte[] pngBytes, CancellationToken ct = default);
}

/// <summary>
/// "Show in folder": opens the system file manager with the file selected (Explorer, Finder,
/// the Linux desktop's file manager). Not available on Android or Web.
/// </summary>
public interface IFileManager
{
    bool IsAvailable { get; }

    Task<bool> RevealAsync(string localPath, CancellationToken ct = default);
}

/// <summary>Default for hosts without a file manager (Android, Web).</summary>
public sealed class NoFileManager : IFileManager
{
    public bool IsAvailable => false;

    public Task<bool> RevealAsync(string localPath, CancellationToken ct = default) => Task.FromResult(false);
}

/// <summary>Share sheet (Android, macOS). No Ui calls this yet; adding a Share action is a design decision for Arun.</summary>
public interface IShare
{
    bool IsAvailable { get; }

    Task<bool> ShareFileAsync(string localPath, string title, CancellationToken ct = default);
}

/// <summary>App lifecycle events the host raises on its UI thread.</summary>
public interface IAppLifecycle
{
    /// <summary>The app is going to the background or the page is closing: flush drafts and sessions now.</summary>
    event Func<Task>? Suspending;

    event Action? Resumed;

    /// <summary>
    /// Android back button / gesture. Handlers return true when they consumed it (closed a sheet,
    /// a card or a drawer). When nobody does, the host performs its default (leave the app).
    /// </summary>
    event Func<bool>? BackRequested;
}

/// <summary>One command the Ui offers (the same list the Ctrl K command bar shows).</summary>
/// <param name="Id">Stable id, e.g. "doc.print".</param>
/// <param name="Label">User-facing label.</param>
/// <param name="Group">Section it belongs to (File, Tabs, Document, Read, Mark, Arrange, Protect, View…).</param>
/// <param name="Keys">Canonical shortcut text ("Ctrl P"), or null. Show it with <see cref="IKeyLabels.Format"/>.</param>
/// <param name="IsEnabled">False when the command can't run now.</param>
/// <param name="DisabledReason">Why it is disabled (tooltip text), or null.</param>
/// <param name="IsSeparator">A divider between groups of menu items (Document menu only).</param>
public sealed record AppCommandInfo(
    string Id,
    string Label,
    string Group,
    string? Keys,
    bool IsEnabled,
    string? DisabledReason = null,
    bool IsSeparator = false);

/// <summary>
/// The Ui's commands for a native menu (macOS menu bar). Filled host-side from the shell's own
/// command registry — the Ui needs no hook. Read <see cref="Commands"/> / <see cref="DocumentMenu"/>
/// after each <see cref="Changed"/> and rebuild the menu.
/// </summary>
public interface IAppCommands
{
    /// <summary>Every command the Ctrl K bar can run right now (global + the mounted page's).</summary>
    IReadOnlyList<AppCommandInfo> Commands { get; }

    /// <summary>The top bar's Document ▾ menu, in order, separators included.</summary>
    IReadOnlyList<AppCommandInfo> DocumentMenu { get; }

    /// <summary>Raised when either list, or any enabled state, may have changed.</summary>
    event Action? Changed;

    /// <summary>Runs a command by id. Returns false when the id is unknown or the command is disabled.
    /// Call it on the Blazor dispatcher (e.g. <c>BlazorWebView.Dispatcher</c>), like any Ui action.</summary>
    Task<bool> InvokeAsync(string id);
}

/// <summary>
/// One extra third-party notices text a host ships itself, shown as its own tab on the Licences page
/// (coordinator, 7 Nov 2026) — e.g. the .NET runtime notices a self-contained host bundles (Android,
/// Linux, macOS). Read only when that tab opens, never at startup.
/// </summary>
public interface IHostNotices
{
    /// <summary>Tab label, e.g. ".NET runtime". Null: the host adds nothing (no tab).</summary>
    string? Title { get; }

    /// <summary>The full text, exactly as shipped. Called when the tab opens.</summary>
    string ReadText();
}

/// <summary>Default: no extra notices (Windows, which uses the installed .NET; Web).</summary>
public sealed class NoHostNotices : IHostNotices
{
    public string? Title => null;

    public string ReadText() => string.Empty;
}

/// <summary>One file dropped on the portal window (D-89 A). <paramref name="OpenRead"/> streams it from the
/// browser; read it within <see cref="IDroppedFiles.ReceiveAsync"/> (it can't be reopened later).</summary>
public sealed record DroppedFile(string Name, long Size, Func<CancellationToken, Stream> OpenRead);

/// <summary>
/// Files dropped on the web portal's window (D-89 option A). The Ui catches the drop and hands the
/// files here; the Web host stores each as the session's working copy (as its upload does) and opens
/// it through the kit's <c>IncomingFiles</c> router (Bench shelf or one tab each). Other hosts keep the
/// default: their windows take drops natively.
/// </summary>
public interface IDroppedFiles
{
    /// <summary>False: the Ui doesn't catch drops (default; desktop and Android).</summary>
    bool IsAvailable { get; }

    /// <summary>Largest file the host accepts, in bytes.</summary>
    long MaxFileBytes { get; }

    /// <summary>Stores and opens <paramref name="files"/>. Returns one short, user-facing message per
    /// file that couldn't be taken (e.g. "report.xlsx is larger than 50 MB"); empty when all were.</summary>
    Task<IReadOnlyList<string>> ReceiveAsync(IReadOnlyList<DroppedFile> files, CancellationToken ct = default);
}

/// <summary>Default: no Ui drop catcher.</summary>
public sealed class NoDroppedFiles : IDroppedFiles
{
    public bool IsAvailable => false;

    public long MaxFileBytes => 0;

    public Task<IReadOnlyList<string>> ReceiveAsync(IReadOnlyList<DroppedFile> files, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
