using System.Runtime.InteropServices;
using ChuvadiReader.Platform;

namespace ChuvadiReader.Hosting.Desktop;

/// <summary>What this launch became.</summary>
public enum InstanceRole
{
    /// <summary>This process is the running copy: show the window, then <see cref="DesktopSingleInstance.StartListening"/>.</summary>
    Primary,

    /// <summary>The running copy took the files: exit now, without building anything.</summary>
    HandedOver,

    /// <summary>A copy is running but didn't answer in time (frozen, or still starting): open our own
    /// window as before (F-1). This copy doesn't listen; the running one stays the primary.</summary>
    Standalone,
}

/// <summary>
/// Single instance for Windows and Linux (decisions W-A, L-A, F-1, 7 Nov 2026; docs/foundation/SINGLE-INSTANCE.md).
/// <list type="bullet">
/// <item>Which copy runs: Windows — a named mutex per user and session, created (never acquired) by
/// the running copy (core library; it disappears with the running copy's handle). Linux and other Unix — an exclusive lock file in <c>$XDG_RUNTIME_DIR</c> (the kernel
/// frees it if the copy dies).</item>
/// <item>Hand-over: Windows — a named pipe only this user can open; Unix — a Unix socket in the same
/// 0700 folder. The running copy starts listening after its first paint, so a first launch pays only
/// for the mutex or lock file.</item>
/// <item>Frozen copy (F-1): the new launch waits up to <see cref="AckWait"/> for the running copy's UI to
/// take the files, then opens its own window. The running copy gives up after <see cref="UiWait"/> and
/// drops the files, so a file is never opened twice.</item>
/// </list>
/// </summary>
public sealed class DesktopSingleInstance : IAppInstance, IDisposable
{
    /// <summary>How long a new launch waits for a running copy that is still starting to listen.</summary>
    public static readonly TimeSpan ConnectWait = TimeSpan.FromSeconds(3);

    /// <summary>How long a new launch waits for the running copy to accept, once connected.</summary>
    public static readonly TimeSpan AckWait = TimeSpan.FromSeconds(1.5);

    /// <summary>How long the running copy waits for its own UI thread to take the files.</summary>
    public static readonly TimeSpan UiWait = TimeSpan.FromSeconds(1.0);

    private readonly InstanceName _name;
    private Mutex? _mutex;
    private FileStream? _lock;
    private IDisposable? _server;

    private DesktopSingleInstance(InstanceName name, InstanceRole role, Mutex? mutex, FileStream? lockFile)
    {
        _name = name;
        Role = role;
        _mutex = mutex;
        _lock = lockFile;
    }

    public InstanceRole Role { get; }

    /// <summary>The last hand-over's activation token (Wayland's <c>XDG_ACTIVATION_TOKEN</c> from the new
    /// launch), set on the UI thread just before <see cref="FilesReceived"/>. The Linux host passes it to
    /// GTK so the window may come to the front; null when the launcher gave none (and always on Windows).</summary>
    public string? ActivationToken { get; private set; }

    /// <inheritdoc />
    public event Action<IReadOnlyList<string>>? FilesReceived;

    /// <summary>
    /// Call first thing at startup, before building services or any window.
    /// </summary>
    /// <param name="args">The launch's file arguments (full or relative paths; others are ignored by the router).</param>
    /// <param name="appId">One running copy per id; tests pass their own.</param>
    /// <param name="beforeHandOff">Runs before handing over, e.g. Windows' <c>AllowSetForegroundWindow</c>
    /// so the running copy may come to the front.</param>
    public static DesktopSingleInstance Claim(IReadOnlyList<string> args, string appId = "Chuvadi.Reader", Action? beforeHandOff = null)
    {
        var name = InstanceName.For(appId);
        if (TryBecomePrimary(name, out var mutex, out var lockFile))
        {
            return new DesktopSingleInstance(name, InstanceRole.Primary, mutex, lockFile);
        }

        beforeHandOff?.Invoke();
        var files = args.Select(FullPathOrNull).OfType<string>().ToArray();
        var token = OperatingSystem.IsWindows() ? null : Environment.GetEnvironmentVariable("XDG_ACTIVATION_TOKEN");
        var accepted = InstanceChannel.TrySend(name, files, token, ConnectWait, AckWait);
        return new DesktopSingleInstance(name, accepted ? InstanceRole.HandedOver : InstanceRole.Standalone, null, null);
    }

    /// <summary>
    /// <see cref="Claim"/> on a background thread, so it overlaps the host's own start-up (building
    /// services, creating the window). Await it just before showing the window; on
    /// <see cref="InstanceRole.HandedOver"/> exit without showing it. Recommended where the claim
    /// touches the file system (Linux: ~1–3 ms cold, measured); on Windows the synchronous claim is one
    /// named mutex.
    /// </summary>
    public static Task<DesktopSingleInstance> ClaimAsync(IReadOnlyList<string> args, string appId = "Chuvadi.Reader", Action? beforeHandOff = null) =>
        Task.Run(() => Claim(args, appId, beforeHandOff));

    /// <summary>
    /// Primary only: starts taking hand-overs, on a background thread. Call after the first paint
    /// (<c>IAppReadySignal.Ready</c>). <paramref name="runOnUi"/> runs an action on the UI thread and
    /// returns true when it ran within the time given; when it returns false the action must never run.
    /// </summary>
    public void StartListening(Func<Action, TimeSpan, bool> runOnUi)
    {
        ArgumentNullException.ThrowIfNull(runOnUi);
        if (Role != InstanceRole.Primary || _server is not null)
        {
            return;
        }

        _server = InstanceChannel.Listen(_name, handOver => runOnUi(
            () =>
            {
                ActivationToken = handOver.ActivationToken;
                FilesReceived?.Invoke(handOver.Files);
            },
            UiWait));
    }

    public void Dispose()
    {
        _server?.Dispose();
        _server = null;
        _lock?.Dispose();
        _lock = null;
        _mutex?.Dispose();
        _mutex = null;
    }

    private static bool TryBecomePrimary(InstanceName name, out Mutex? mutex, out FileStream? lockFile)
    {
        mutex = null;
        lockFile = null;
        if (OperatingSystem.IsWindows())
        {
            // The running copy is whoever created the named mutex; it is never acquired, only kept open.
            // Ownership would tie it to one thread (re-entrant on that thread, abandoned if that thread
            // ends — e.g. a pool thread under ClaimAsync). When the running copy exits or crashes its
            // handle closes, the mutex disappears, and the next launch creates it again.
            var m = new Mutex(initiallyOwned: false, name.MutexName, out var createdNew);
            if (createdNew)
            {
                mutex = m;
                return true;
            }

            m.Dispose();
            return false;
        }

        try
        {
            Directory.CreateDirectory(name.Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            lockFile = new FileStream(name.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Can't use the folder at all: behave as before (each launch its own window).
            return true;
        }
    }

    private static string? FullPathOrNull(string arg)
    {
        try
        {
            return string.IsNullOrWhiteSpace(arg) ? null : Path.GetFullPath(arg);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

/// <summary>Per-user (and on Windows per-session) names. No cryptography: the hash only separates users.</summary>
internal sealed class InstanceName
{
    private InstanceName(string appId, string user)
    {
        Key = appId + "-" + Hex(Fnv1a(user));
        Folder = UnixFolder(appId);
    }

    /// <summary>App id + user hash, e.g. "Chuvadi.Reader-1a2b3c4d".</summary>
    public string Key { get; }

    /// <summary>Windows mutex, per logon session (the Local namespace).</summary>
    public string MutexName => @"Local\" + Key;

    /// <summary>Windows pipe name: pipes are machine-wide, so the session id is added.</summary>
    public string PipeName => Key + "-" + WindowsSessionId();

    /// <summary>Unix: a 0700 folder of this user's.</summary>
    public string Folder { get; }

    public string LockPath => Path.Combine(Folder, "instance.lock");

    public string SocketPath => Path.Combine(Folder, "instance.sock");

    // Kept free of culture-aware formatting and casing: those initialise globalization (ICU on Linux),
    // which would move milliseconds onto the first launch's critical path.
    // On Unix the folder is already per user ($XDG_RUNTIME_DIR, or a /tmp folder named after $USER), so
    // the user database (getpwuid, ~2 ms cold) isn't consulted; Windows names need the user.
    public static InstanceName For(string appId) =>
        new(appId, OperatingSystem.IsWindows() ? Environment.UserDomainName + "\\" + Environment.UserName : UnixUser());

    private static string UnixUser() =>
        Environment.GetEnvironmentVariable("USER") is { Length: > 0 } user ? user : "user";

    private static string UnixFolder(string appId)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var root = !string.IsNullOrEmpty(runtime) && Path.IsPathRooted(runtime) ? runtime : Path.GetTempPath();
        var folder = AsciiLower(appId);
        return string.IsNullOrEmpty(runtime) ? Path.Combine(root, folder + "-" + UnixUser()) : Path.Combine(root, folder);
    }

    // Plain loops over stack buffers: generic helpers such as string.Create<T> would be JIT-compiled
    // at startup (ReadyToRun can't precompile that instantiation).
    private static string Hex(uint v)
    {
        Span<char> span = stackalloc char[8];
        for (var i = 7; i >= 0; i--, v >>= 4)
        {
            span[i] = "0123456789abcdef"[(int)(v & 0xF)];
        }

        return new string(span);
    }

    private static string AsciiLower(string s)
    {
        Span<char> span = s.Length <= 128 ? stackalloc char[s.Length] : new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            span[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
        }

        return new string(span);
    }

    private static uint Fnv1a(string s)
    {
        uint h = 2166136261;
        foreach (var c in s)
        {
            h = (h ^ c) * 16777619;
        }

        return h;
    }

    private static uint WindowsSessionId() =>
        OperatingSystem.IsWindows() && ProcessIdToSessionId((uint)Environment.ProcessId, out var id) ? id : 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
}
