using System.Security.Cryptography;

namespace ChuvadiReader.Core.Reader;

/// <summary>A file's size and last-written time, as cheap to read as a directory entry.</summary>
public sealed record FileStamp(long Length, DateTime LastWriteUtc)
{
    /// <summary>The stamp of <paramref name="path"/> now, or null when there is no file there.</summary>
    public static FileStamp? Of(string path)
    {
        var fi = new FileInfo(path);
        return fi.Exists ? new FileStamp(fi.Length, fi.LastWriteTimeUtc) : null;
    }
}

/// <summary>The source file as it was when the work being saved began: its bytes, kept in memory, and their hash.
/// Save as writes from these bytes, so it works even when the file has since moved, and marks always land on the
/// pages they were placed on.</summary>
public sealed class SourceCopy
{
    public SourceCopy(byte[] bytes, FileStamp stamp)
    {
        Bytes = bytes;
        Stamp = stamp;
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
    }

    public byte[] Bytes { get; }

    public FileStamp Stamp { get; }

    public string Sha256 { get; }
}

/// <summary>The source moved, was renamed or deleted, or changed on disk since it was opened: nothing was written
/// (BUG-23, BUG-24, SF12, SF13).</summary>
public sealed class SourceChangedException(bool missing, string message) : IOException(message)
{
    /// <summary>True when there is no file at the source's path any more.</summary>
    public bool Missing { get; } = missing;
}

/// <summary>
/// No silent saves. A save into the source checks first that the file at its path is still the one that was opened
/// (size and time, and the content hash when the work kept a copy); otherwise it stops with a plain sentence and the
/// person is offered Save as. Nothing is ever stamped onto a file that changed underneath the work.
/// </summary>
public static class SourceGuard
{
    public const string MovedMessage =
        "This file isn't where it was opened from any more: it was moved, renamed or deleted. Nothing was saved. Use Save as to keep your work in a new file.";

    public const string ReplacedMessage =
        "This file was changed on disk after you opened it, so nothing was saved into it: your work belongs to the pages as they were. Use Save as to keep it in a new file.";

    /// <summary>Takes the copy the work keeps, checking the file is still the one opened (<paramref name="opened"/>).</summary>
    public static SourceCopy Take(string path, FileStamp? opened)
    {
        var before = FileStamp.Of(path) ?? throw new SourceChangedException(true, MovedMessage);
        if (opened is not null && before != opened) throw new SourceChangedException(false, ReplacedMessage);
        var bytes = File.ReadAllBytes(path);
        var after = FileStamp.Of(path);
        if (after != before || bytes.LongLength != before.Length) throw new SourceChangedException(false, ReplacedMessage);
        return new SourceCopy(bytes, before);
    }

    /// <summary>Throws unless the file at <paramref name="path"/> is still the one the work began from.</summary>
    public static void CheckUnchanged(string path, FileStamp? opened, SourceCopy? copy)
    {
        var now = FileStamp.Of(path) ?? throw new SourceChangedException(true, MovedMessage);
        var expected = copy?.Stamp ?? opened;
        if (expected is not null && now != expected) throw new SourceChangedException(false, ReplacedMessage);
        if (copy is not null)
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(f)), copy.Sha256, StringComparison.Ordinal))
            {
                throw new SourceChangedException(false, ReplacedMessage);
            }
        }
    }

    /// <summary>Writes <paramref name="bytes"/> over <paramref name="path"/> through a temporary file in the same
    /// folder, so a failure leaves the original as it was; the temporary file never stays behind (SC14).</summary>
    public static void ReplaceAtomically(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var temp = Path.Combine(dir, "~" + Path.GetFileNameWithoutExtension(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".chuvadi-tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
