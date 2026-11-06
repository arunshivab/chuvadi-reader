using System.Diagnostics;
using System.IO;
using ChuvadiReader.Platform;

namespace ChuvadiReader.Windows.Platform;

/// <summary>"Show in folder" on Windows: opens File Explorer with the file selected.</summary>
public sealed class WpfFileManager : IFileManager
{
    public bool IsAvailable => true;

    public Task<bool> RevealAsync(string localPath, CancellationToken ct = default)
    {
        try
        {
            var full = Path.GetFullPath(localPath);
            if (!File.Exists(full))
            {
                return Task.FromResult(false);
            }

            // explorer.exe /select,"<path>" — Windows paths can't contain quotes, so quoting is safe.
            using var p = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"") { UseShellExecute = true });
            return Task.FromResult(p is not null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            return Task.FromResult(false);
        }
    }
}
