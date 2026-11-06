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
}
