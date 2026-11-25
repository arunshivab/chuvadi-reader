using System.Security.Cryptography;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Primitives;

namespace ChuvadiReader.Core.Documents;

/// <summary>How a save writes the file: the password that opens the source (to read it), and the lock the
/// written file gets. <see cref="Encryption"/> null means the source had no lock and neither does the output.</summary>
public sealed record SaveLock(string? OpenPassword, EncryptionOptions? Encryption, bool NewOwnerPassword = false)
{
    /// <summary>A source with no lock: read as it is, written as it is.</summary>
    public static SaveLock None { get; } = new(null, null);
}

/// <summary>What a save still needs from the person before it can keep the file's lock.</summary>
public enum LockNeed
{
    /// <summary>The permissions (owner) password: without it the lock can't be written again the same.</summary>
    OwnerPassword,

    /// <summary>The password that opens the file (the file was opened with its owner password, which can't give
    /// the open password back for AES-256 files).</summary>
    OpenPassword,
}

/// <summary>A save was stopped before anything was written, because keeping the file's lock needs a password the
/// tab doesn't have (D-207, D-210). Nothing is ever written unlocked instead.</summary>
public sealed class LockNeedsPasswordException(LockNeed need, string message) : InvalidOperationException(message)
{
    public LockNeed Need { get; } = need;
}

/// <summary>
/// D-207 / D-210: same lock in, same lock out. Every tool that writes a PDF reads its source through
/// <see cref="Plain"/> (decrypted in memory only, never on disk) and writes through <see cref="Write"/> with the
/// <see cref="SaveLock"/> that <see cref="Plan"/> made: the same open password, the same permissions and the same
/// metadata setting. AES-256 sources are written AES-256; AES-128 and RC4 sources are written AES-128 (RC4 can
/// only be read, and AES-128 is what older readers handle best — D-210).
/// </summary>
/// <remarks>chuvadi-pdf 3.32.0 can write a lock only from both passwords. When the tab doesn't have the owner
/// password, a save into the file is refused (<see cref="LockNeed.OwnerPassword"/>); a Save as writes the new copy
/// with the same open password and permissions and a new permissions password that nobody holds
/// (<see cref="SaveLock.NewOwnerPassword"/>), never unlocked. Chuvadi.Pdf 3.36 adds "save with the file's own
/// lock" (the source's file key and lock entries), which needs neither.</remarks>
public static class SourceLock
{
    public const string OwnerNeededToSave =
        "This file is locked with a permissions password. To save into it and keep its lock exactly as it is, Chuvadi needs that password. Enter it, or use Save as to write a new copy that keeps the same open password and permissions.";

    public const string OwnerNeededToChange =
        "This file's permissions don't allow changes. Enter its permissions password to mark it; the saved file keeps the same lock.";

    public const string OpenPasswordNeeded =
        "This file was opened with its permissions password. To keep its lock the same, Chuvadi also needs the password that opens it.";

    public const string NotOwnerPassword = "That isn't this file's permissions password.";

    public const string NotOpenPassword = "That isn't the password that opens this file.";

    /// <summary>What the source's lock is, read with <paramref name="password"/>; null when it has none.</summary>
    public static EncryptionInfo? Inspect(byte[] source, string? password)
    {
        using var doc = Open(source, password);
        return doc.Encryption;
    }

    /// <summary>Which password <paramref name="password"/> is for this file, or null when it opens nothing.</summary>
    public static PdfPasswordKind? Kind(byte[] source, string password)
    {
        try
        {
            using var doc = Open(source, password);
            return doc.Encryption?.OpenedWith;
        }
        catch (PdfPasswordRequiredException)
        {
            return null;
        }
    }

    /// <summary>True when the source is locked and the person holding <paramref name="openPassword"/> may not change
    /// its contents (D-207: Mark and every writing tool then ask for the owner password first).</summary>
    public static bool ChangesForbidden(byte[] source, string? openPassword, string? ownerPassword)
    {
        if (!string.IsNullOrEmpty(ownerPassword)) return false;
        var e = Inspect(source, openPassword);
        return e is not null && !e.EffectiveFlags.HasFlag(PdfPermissions.ModifyContents);
    }

    /// <summary>The source's bytes with no lock, in memory only. Unlocked sources come back as they are.</summary>
    public static byte[] Plain(byte[] source, string? openPassword)
    {
        using var doc = Open(source, openPassword);
        if (doc.Encryption is null) return source;
        using var ms = new MemoryStream();
        doc.Save(ms, null);
        return ms.ToArray();
    }

    /// <summary>The finished file's bytes with <paramref name="saveLock"/>'s lock (as they are when it has none).</summary>
    public static byte[] Lock(byte[] plain, SaveLock saveLock)
    {
        if (saveLock.Encryption is null) return plain;
        using var doc = PdfDocument.Open(new MemoryStream(plain, writable: false));
        using var ms = new MemoryStream();
        doc.Save(ms, saveLock.Encryption);
        return ms.ToArray();
    }

    /// <summary>How to keep <paramref name="source"/>'s lock when writing it again. <paramref name="intoSource"/>:
    /// the write replaces the source (Save); false for a new file (Save as).</summary>
    /// <exception cref="LockNeedsPasswordException">A password is missing; nothing has been written.</exception>
    public static SaveLock Plan(byte[] source, string? openPassword, string? ownerPassword, bool intoSource)
    {
        using var doc = Open(source, string.IsNullOrEmpty(ownerPassword) ? openPassword : ownerPassword);
        var e = doc.Encryption;
        if (e is null) return SaveLock.None;

        bool opensWithout = !PdfDocument.RequiresPassword(new MemoryStream(source, writable: false));
        string? owner = !string.IsNullOrEmpty(ownerPassword) && e.OpenedWith == PdfPasswordKind.Owner ? ownerPassword : null;
        string? user = opensWithout ? "" : null;
        if (!string.IsNullOrEmpty(openPassword))
        {
            switch (Kind(source, openPassword))
            {
                case PdfPasswordKind.Owner:
                    owner ??= openPassword;
                    break;
                case PdfPasswordKind.User:
                    user ??= openPassword;
                    break;
            }
        }

        if (user is null)
        {
            throw new LockNeedsPasswordException(LockNeed.OpenPassword, OpenPasswordNeeded);
        }

        bool newOwner = false;
        if (owner is null)
        {
            if (intoSource)
            {
                throw new LockNeedsPasswordException(LockNeed.OwnerPassword, OwnerNeededToSave);
            }

            owner = NewOwnerPassword();
            newOwner = true;
        }

        var options = e.Algorithm == EncryptionAlgorithm.Aes_256
            ? EncryptionOptions.Aes256(user, owner)
            : EncryptionOptions.Aes128(user, owner);
        options = options.WithPermissions(e.Flags).WithEncryptMetadata(e.EncryptMetadata);
        // Reading needs whichever password the tab has; the lock is written from both.
        return new SaveLock(string.IsNullOrEmpty(openPassword) ? (opensWithout ? null : user) : openPassword, options, newOwner);
    }

    /// <summary>A line for the save's confirmation when <see cref="SaveLock.NewOwnerPassword"/> applies.</summary>
    public const string NewOwnerNote =
        "The copy opens with the same password and keeps the same permissions; its permissions password is new, and no one holds it.";

    /// <summary>BUG-03: what a rewrite (marks, stamps) dropped of the source's document information comes back:
    /// Title, Author, Subject and Keywords, as the source holds them. (Its dates can't be written back with
    /// Chuvadi.Pdf 3.32.0; the library is asked.)</summary>
    public static byte[] KeepInfo(byte[] plainSource, byte[] result)
    {
        string? t, a, su, k;
        using (var src = PdfDocument.Open(new MemoryStream(plainSource, writable: false)))
        {
            (t, a, su, k) = (src.Title, src.Author, src.Subject, src.Keywords);
        }

        if (t is null && a is null && su is null && k is null) return result;
        using var doc = PdfDocument.Open(new MemoryStream(result, writable: false));
        if (doc.Title == t && doc.Author == a && doc.Subject == su && doc.Keywords == k) return result;
        using var ms = new MemoryStream();
        Chuvadi.Pdf.Operations.DocumentInfo.Apply(ms, doc, t, a, su, k);
        return ms.ToArray();
    }

    private static PdfDocument Open(byte[] source, string? password) =>
        string.IsNullOrEmpty(password)
            ? PdfDocument.Open(new MemoryStream(source, writable: false))
            : PdfDocument.Open(new MemoryStream(source, writable: false), password, false);

    private static string NewOwnerPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
}
