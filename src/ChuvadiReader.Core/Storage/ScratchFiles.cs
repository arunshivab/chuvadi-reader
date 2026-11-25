using ChuvadiReader.Platform;

namespace ChuvadiReader.Core.Storage;

/// <summary>
/// Where intermediate files go (renders, temp PDFs while composing). The host decides the folder through
/// <see cref="IAppPaths.WorkingFolder"/>: desktop = the system temp folder (unchanged), Web = the
/// person's session-only encrypted folder. Without a host value it falls back to the system temp folder.
/// </summary>
public static class ScratchFiles
{
    /// <summary>The working folder to use: the host's, else the system temp folder.</summary>
    public static string Folder(IAppPaths? paths)
    {
        var dir = paths?.WorkingFolder;
        if (string.IsNullOrWhiteSpace(dir))
        {
            return Path.GetTempPath();
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A new, unique, empty file in <paramref name="folder"/> — the same contract as
    /// <see cref="Path.GetTempFileName"/>, in a folder the host chooses.</summary>
    public static string NewFile(string folder)
    {
        var path = Path.Combine(folder, $"tmp{Guid.NewGuid():N}.tmp");
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }

        return path;
    }

    /// <summary>RD35: true for a working copy Chuvadi made in its scratch folder (a desk bound to open in the Reader,
    /// named "name-&lt;32 hex&gt;.pdf"), which is not the person's file: saving "into" it must ask where instead.</summary>
    public static bool IsWorkingCopy(IAppPaths? paths, string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        var scratch = Path.GetFullPath(Folder(paths)).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(dir?.TrimEnd(Path.DirectorySeparatorChar), scratch, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
            && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), "-[0-9a-f]{32}\\.pdf$");
    }
}
