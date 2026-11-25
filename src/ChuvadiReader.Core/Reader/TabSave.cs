using ChuvadiReader.Core.Documents;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// D-207 / D-210: what every save from a PDF tab goes through. The work keeps the source as it was when it began
/// (<see cref="BeginWork"/>); a save into the source first checks the file is still that one
/// (<see cref="SourceGuard"/>), and every save keeps the source's lock (<see cref="SourceLock"/>). A save that
/// can't do both stops with a plain sentence and writes nothing.
/// </summary>
public static class TabSave
{
    /// <summary>Keeps the source as it is now for this tab's unsaved work, once (the first change). Throws
    /// <see cref="SourceChangedException"/> when the file already moved or changed since the tab opened it.</summary>
    public static SourceCopy BeginWork(ReaderTab tab)
    {
        if (tab.WorkSource is { } kept) return kept;
        if (tab.Path is null) throw new InvalidOperationException("This tab has no file.");
        return tab.WorkSource = SourceGuard.Take(tab.Path, tab.Opened);
    }

    /// <summary>Forgets the kept source (the work was saved into the file, or discarded).</summary>
    public static void EndWork(ReaderTab tab) => tab.WorkSource = null;

    /// <summary>The source bytes and lock for a save. <paramref name="intoSource"/>: the save replaces the source.</summary>
    /// <exception cref="SourceChangedException">Saving into a source that moved or changed.</exception>
    /// <exception cref="LockNeedsPasswordException">Keeping the lock needs a password the tab doesn't have.</exception>
    public static (byte[] Source, SaveLock Lock) Prepare(ReaderTab tab, bool intoSource)
    {
        if (tab.Path is null) throw new InvalidOperationException("This tab has no file.");
        if (intoSource) SourceGuard.CheckUnchanged(tab.Path, tab.Opened, tab.WorkSource);
        var source = tab.WorkSource?.Bytes ?? SourceGuard.Take(tab.Path, tab.Opened).Bytes;
        return (source, SourceLock.Plan(source, tab.Password, tab.OwnerPassword, intoSource));
    }

    /// <summary>D-207: true when the file's permissions forbid changes and the tab doesn't hold its owner password,
    /// so Mark and the writing tools must ask for it first.</summary>
    public static bool NeedsOwnerToChange(ReaderTab tab)
    {
        if (tab.Path is null || !string.IsNullOrEmpty(tab.OwnerPassword)) return false;
        var source = tab.WorkSource?.Bytes ?? File.ReadAllBytes(tab.Path);
        return SourceLock.ChangesForbidden(source, tab.Password, tab.OwnerPassword);
    }

    /// <summary>Checks <paramref name="password"/> is the file's owner password and keeps it for this tab.
    /// Null when it is; otherwise the sentence to show.</summary>
    public static string? GiveOwnerPassword(ReaderTab tab, string password)
    {
        if (tab.Path is null || string.IsNullOrEmpty(password)) return SourceLock.NotOwnerPassword;
        var source = tab.WorkSource?.Bytes ?? File.ReadAllBytes(tab.Path);
        if (SourceLock.Kind(source, password) != Chuvadi.Pdf.Encryption.PdfPasswordKind.Owner) return SourceLock.NotOwnerPassword;
        tab.OwnerPassword = password;
        PdfPasswords.Remember(tab.Path, tab.Password, password);
        return null;
    }

    /// <summary>Checks <paramref name="password"/> opens the file as its open (user) password and keeps it for this tab.</summary>
    public static string? GiveOpenPassword(ReaderTab tab, string password)
    {
        if (tab.Path is null || string.IsNullOrEmpty(password)) return SourceLock.NotOpenPassword;
        var source = tab.WorkSource?.Bytes ?? File.ReadAllBytes(tab.Path);
        if (SourceLock.Kind(source, password) != Chuvadi.Pdf.Encryption.PdfPasswordKind.User) return SourceLock.NotOpenPassword;
        // The tab opened with the owner password: keep that as the owner one and this as the open one.
        if (string.IsNullOrEmpty(tab.OwnerPassword) && !string.IsNullOrEmpty(tab.Password)
            && SourceLock.Kind(source, tab.Password!) == Chuvadi.Pdf.Encryption.PdfPasswordKind.Owner)
        {
            tab.OwnerPassword = tab.Password;
        }

        tab.Password = password;
        PdfPasswords.Remember(tab.Path, password, tab.OwnerPassword);
        return null;
    }
}
