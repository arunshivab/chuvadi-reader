namespace ChuvadiReader.Platform;

/// <summary>Which host the app is running in. One value per shipped build.</summary>
public enum PlatformKind
{
    Windows,
    MacOS,
    Linux,
    Android,
    Web,
}

/// <summary>
/// The layout family the Ui renders (design handout: Reader Side Rails for desktop,
/// Portal &amp; Android for tablet and phone). Web uses <see cref="Desktop"/> chrome with the
/// portal additions described by <see cref="ShellChrome"/>.
/// </summary>
public enum FormFactor
{
    /// <summary>Mouse-first window; D-10 rails at 62 / 64 / 54 px.</summary>
    Desktop,

    /// <summary>Touch-first, rails at 72 / 72 / 60 px with 48 px targets (Android tablet).</summary>
    Tablet,

    /// <summary>Rails fold: bottom bar, tool strip, View sheet (Android phone).</summary>
    Phone,
}

/// <summary>Where the window buttons go, if the Ui draws any.</summary>
public enum WindowButtons
{
    /// <summary>No window buttons (Android, Web).</summary>
    None,

    /// <summary>Minimise · maximise · close at the far right (Windows, Linux default).</summary>
    Right,

    /// <summary>Close · minimise · zoom at the far left (macOS; Linux when the desktop's button layout says so).</summary>
    Left,
}

/// <summary>Pointer kinds the device has. A tablet with a pen reports Touch | Pen.</summary>
[Flags]
public enum InputKinds
{
    None = 0,
    Mouse = 1,
    Touch = 2,
    Pen = 4,
    Keyboard = 8,
}

/// <summary>The frame around the shared Ui: what the host draws natively and what the Ui must draw.</summary>
/// <param name="Buttons">Window buttons the Ui draws in the top bar.</param>
/// <param name="NativeMenuBar">True on macOS: File/Edit/View/Document/Window/Help live in the system menu bar
/// (the host builds it and routes items to <see cref="IAppCommands"/>).</param>
/// <param name="AccountMenu">True on Web: an account menu replaces the window buttons.</param>
/// <param name="TitleBarDrag">True when pressing the Ui's top bar should move the native window.</param>
/// <param name="CornerRadiusPx">Window corner radius the Ui should respect (Linux 12, Web 0, others 0 = host handles it).</param>
public sealed record ShellChrome(
    WindowButtons Buttons,
    bool NativeMenuBar,
    bool AccountMenu,
    bool TitleBarDrag,
    int CornerRadiusPx);

/// <summary>
/// What this host can do. The Ui hides or locks a control whose capability is false
/// (exact behaviour per control is listed in docs/foundation/EDITS-FOR-APPROVAL.md).
/// </summary>
public sealed record PlatformCapabilities
{
    /// <summary>A folder picker exists (output folders, watched folders, Library roots).</summary>
    public bool PickFolders { get; init; }

    /// <summary>"Save" can overwrite the file the person opened (desktop: the file itself; Android: written back to the URI).</summary>
    public bool SaveInPlace { get; init; }

    /// <summary>"Save" and "Save as" deliver a download instead of writing a file the person keeps
    /// here (Web this release: no server storage; working copies are session-only).</summary>
    public bool SavesAsDownload { get; init; }

    /// <summary>Folders can be watched for new files (Library, Watched folder).</summary>
    public bool WatchFolders { get; init; }

    /// <summary>"Show in folder" / "Copy file path" make sense (a real file manager and real paths).</summary>
    public bool RevealInFolder { get; init; }

    /// <summary>Files can be dropped onto the window from outside the app.</summary>
    public bool DropFiles { get; init; }

    /// <summary>A share sheet exists (<see cref="IShare"/>).</summary>
    public bool Share { get; init; }

    /// <summary>Several windows of the app can be open at once.</summary>
    public bool MultipleWindows { get; init; }

    /// <summary>Processing happens on a server shared by many people (Web). Per-user isolation applies.</summary>
    public bool ServerSide { get; init; }
}

/// <summary>Turns canonical shortcut text ("Ctrl Shift S") into what this platform shows ("⌘⇧S" on macOS).</summary>
public interface IKeyLabels
{
    /// <summary>Label of the primary modifier: "Ctrl", or "⌘" on macOS.</summary>
    string Primary { get; }

    /// <summary>Formats canonical shortcut text written with Ctrl / Shift / Alt.</summary>
    string Format(string canonical);
}

/// <summary>Facts about the host, read by the Ui to pick chrome, layout and wording.</summary>
public interface IPlatformInfo
{
    PlatformKind Kind { get; }

    /// <summary>Current layout family. Can change at run time (Android fold/unfold, browser resize on Web).</summary>
    FormFactor FormFactor { get; }

    /// <summary>Raised on the host's UI thread after <see cref="FormFactor"/> changes.</summary>
    event Action? FormFactorChanged;

    InputKinds Input { get; }

    ShellChrome Chrome { get; }

    PlatformCapabilities Capabilities { get; }

    IKeyLabels Keys { get; }
}
