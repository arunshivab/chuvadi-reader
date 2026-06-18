using System;
using System.IO;

namespace ChuvadiReader.Windows.Platform;

/// <summary>
/// Resolves the folder WebView2 uses for its user data (which holds its on-disk
/// HTTP cache for the app's CSS/JS).
///
/// The folder is keyed to the app *version*. Within a version it stays the same,
/// so WebView2 reuses one profile and there is no first-run profile setup on each
/// launch (that setup is what caused the brief white flashes). When the app is
/// updated to a new version, the key changes, so the update lands on a fresh,
/// empty folder that physically cannot contain a previous version's assets —
/// guaranteed on any machine, not just in development. The MainWindow also passes
/// --disable-http-cache so edited assets always reload during development without
/// needing to wipe anything.
///
/// Nothing important is lost when the folder rotates across versions: theme and
/// preferences are stored on disk via IAppStorage, not in WebView2 storage.
/// </summary>
internal static class WebViewCache
{
    /// <summary>
    /// Returns the version-keyed user-data folder to hand to WebView2, ensuring it
    /// exists and pruning folders left by other app versions.
    /// </summary>
    public static string ResolveAndPrune()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Chuvadi", "Reader", "WebView2");
        Directory.CreateDirectory(root);

        var folder = Path.Combine(root, "v-" + VersionToken());
        Directory.CreateDirectory(folder);
        Prune(root, keep: folder);
        return folder;
    }

    /// <summary>The app version plus the UI build's module id, sanitised for a folder
    /// name. The module id changes on every rebuild, so edited CSS/JS always lands on a
    /// fresh cache folder even though the assembly version is fixed.</summary>
    private static string VersionToken()
    {
        var v = typeof(App).Assembly.GetName().Version;
        var ver = v is null ? "1-0-0-0" : $"{v.Major}-{v.Minor}-{v.Build}-{v.Revision}";
        var mvid = typeof(ChuvadiReader.Ui.App).Assembly.ManifestModule.ModuleVersionId.ToString("N");
        return ver + "-" + mvid.Substring(0, 12);
    }

    private static void Prune(string root, string keep)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(root, "v-*"))
            {
                if (!string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase))
                    TryDelete(dir);
            }

            // Remove folders from the earlier build-token scheme, if any remain.
            foreach (var dir in Directory.GetDirectories(root, "cache-*"))
                TryDelete(dir);
        }
        catch
        {
            // Best-effort cleanup; never fatal.
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // A folder may be locked by another running instance — leave it.
        }
    }
}
