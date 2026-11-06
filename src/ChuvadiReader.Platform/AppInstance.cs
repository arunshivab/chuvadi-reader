namespace ChuvadiReader.Platform;

/// <summary>
/// Single instance, tabbed (approved programme-wide, 7 Nov 2026): one running copy per person on a
/// desktop. Opening a file while Chuvadi runs hands it to the running copy, which opens it as a new
/// tab. Windows and Linux use the kit's <c>DesktopSingleInstance</c>; macOS (LaunchServices) and
/// Android (<c>singleTask</c>) are single-instance natively and raise this from their own callbacks.
/// </summary>
public interface IAppInstance
{
    /// <summary>
    /// Files another launch (or the operating system) handed to this running copy, in order. Raised on
    /// the UI thread. The kit's <c>IncomingFiles</c> router subscribes and opens them.
    /// An empty list means "come to the front" (a launch with no file).
    /// </summary>
    event Action<IReadOnlyList<string>>? FilesReceived;
}

/// <summary>Default where nothing hands files over (Web, tests).</summary>
public sealed class NoAppInstance : IAppInstance
{
    public event Action<IReadOnlyList<string>>? FilesReceived { add { } remove { } }
}
