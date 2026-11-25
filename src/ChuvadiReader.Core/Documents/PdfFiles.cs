using System.Collections.Concurrent;
using Chuvadi.Pdf.Documents;

namespace ChuvadiReader.Core.Documents;

/// <summary>
/// BUG-19 (D-207): encryption is never a restriction. The passwords a person typed to unlock a file in this run,
/// by file, in memory only (never persisted, never logged), so Find, Press, Protect, the Arrange shelf, Lift and
/// every other tool that opens the same file by its path reuse them instead of failing with "this PDF is
/// encrypted". Forgotten when the last tab with that file closes.
/// </summary>
public static class PdfPasswords
{
    private static readonly ConcurrentDictionary<string, (string? Open, string? Owner)> Known =
        new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static string Key(string path) => Path.GetFullPath(path);

    /// <summary>Notes the password that opened <paramref name="path"/> (and its owner password, when given).</summary>
    public static void Remember(string path, string? open, string? owner = null)
    {
        if (string.IsNullOrEmpty(open) && string.IsNullOrEmpty(owner)) return;
        Known.AddOrUpdate(Key(path), (open, owner), (_, was) => (string.IsNullOrEmpty(open) ? was.Open : open, string.IsNullOrEmpty(owner) ? was.Owner : owner));
    }

    /// <summary>The password to open <paramref name="path"/> with: the owner one when known (it may do everything),
    /// else the open one; null when none was given in this run.</summary>
    public static string? For(string path) =>
        Known.TryGetValue(Key(path), out var p) ? (string.IsNullOrEmpty(p.Owner) ? p.Open : p.Owner) : null;

    /// <summary>The open (user) password noted for <paramref name="path"/>, and its owner password.</summary>
    public static (string? Open, string? Owner) Both(string path) =>
        Known.TryGetValue(Key(path), out var p) ? p : (null, null);

    public static void Forget(string path) => Known.TryRemove(Key(path), out _);

    /// <summary>For tests.</summary>
    public static void ForgetAll() => Known.Clear();
}

/// <summary>Reading a file another program has open (Excel keeps its workbook open for writing, Word its
/// document): shared for reading, writing and deleting, so the open never fails with "used by another process".</summary>
public static class SharedRead
{
    public static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);

    public static byte[] AllBytes(string path)
    {
        using var f = Open(path);
        using var ms = new MemoryStream(f.CanSeek ? (int)Math.Min(f.Length, int.MaxValue) : 0);
        f.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>Opening a PDF by its path, with the password given for it in this run (<see cref="PdfPasswords"/>).
/// Tools use this instead of <see cref="PdfDocument.Open(string)"/>.</summary>
public static class PdfFiles
{
    /// <summary>Opens through <see cref="SharedRead"/>, so a file another program holds open for writing still
    /// opens on Windows (the library's own path open lets only other readers in). The document owns the stream.
    /// <paramref name="password"/> overrides the one kept for the path.</summary>
    public static PdfDocument Open(string path, string? password = null)
    {
        var pw = string.IsNullOrEmpty(password) ? PdfPasswords.For(path) : password;
        var stream = SharedRead.Open(path);
        try
        {
            return string.IsNullOrEmpty(pw) ? PdfDocument.Open(stream, leaveOpen: false) : PdfDocument.Open(stream, pw, false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>As <see cref="Open"/>.</summary>
    public static async Task<PdfDocument> OpenAsync(string path, CancellationToken ct = default)
    {
        var pw = PdfPasswords.For(path);
        var stream = SharedRead.Open(path);
        try
        {
            return await PdfDocument.OpenAsync(stream, new PdfOpenOptions { Password = string.IsNullOrEmpty(pw) ? null : pw }, ct).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>D-207: a new file made from a locked file's pages (merge, split, desk Bind and Lift, flatten) must
    /// not come out unlocked, and how such a file is locked is still to be decided, so these tools stop here.</summary>
    public static string LockedSourceText(string path) =>
        $"{Path.GetFileName(path)} is locked with a password. A new file made from its pages has to be locked too, and how is still being decided, so this was stopped. Nothing was written.";

    /// <summary>Opens a source for a tool that writes a new file from it: refused when the source is locked.</summary>
    public static PdfDocument OpenForNewFile(string path)
    {
        var doc = Open(path);
        if (doc.Encryption is null) return doc;
        doc.Dispose();
        throw new InvalidOperationException(LockedSourceText(path));
    }

    /// <summary>As <see cref="OpenForNewFile"/>.</summary>
    public static async Task<PdfDocument> OpenForNewFileAsync(string path, CancellationToken ct = default)
    {
        var doc = await OpenAsync(path, ct).ConfigureAwait(false);
        if (doc.Encryption is null) return doc;
        doc.Dispose();
        throw new InvalidOperationException(LockedSourceText(path));
    }
}
