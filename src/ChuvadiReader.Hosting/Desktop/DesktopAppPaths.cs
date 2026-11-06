using ChuvadiReader.Platform;

namespace ChuvadiReader.Hosting.Desktop;

/// <summary>
/// Standard per-user folders on Windows, macOS and Linux. The Windows values equal what Core uses
/// today (%AppData%\Chuvadi, Documents); macOS uses ~/Library/Application Support/Chuvadi and Linux
/// follows XDG ($XDG_CONFIG_HOME or ~/.config).
/// The default save folder is the person's Documents folder even before it exists (it is created
/// on first save): .NET returns "" for a missing Documents folder, and Core would then save under
/// the temp folder, which Linux wipes at reboot (Linux chat, BUILD-LOG §17).
/// </summary>
public sealed class DesktopAppPaths : IAppPaths
{
    public DesktopAppPaths()
    {
        AppDataFolder = ResolveAppData();
        // Same folder Core uses today (Path.GetTempPath()), so moving Core to IAppPaths changes nothing on desktop.
        WorkingFolder = Path.GetTempPath();
        DefaultSaveFolder = ResolveSaveFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>
    /// The existing Documents folder when there is one (Windows: always, so nothing changes there);
    /// else where it would be (XDG_DOCUMENTS_DIR as .NET reads it), unless that is the home folder
    /// itself; else ~/Documents; null only when there is no home folder.
    /// </summary>
    public static string? ResolveSaveFolder(string? existing, string? unverified, string? home)
    {
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        static string Trim(string p) => p.TrimEnd('/', '\\');
        if (!string.IsNullOrEmpty(unverified) && Path.IsPathRooted(unverified)
            && (string.IsNullOrEmpty(home) || !string.Equals(Trim(unverified), Trim(home), StringComparison.Ordinal)))
        {
            return unverified;
        }

        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, "Documents");
    }

    public string AppDataFolder { get; }

    public string WorkingFolder { get; }

    public string? DefaultSaveFolder { get; }

    private static string ResolveAppData()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Chuvadi");
        }

        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var root = !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "Chuvadi");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chuvadi");
    }
}
