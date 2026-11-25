using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Primitives;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>D-207 / D-210 (BUG-23, BUG-24, BUG-26, MK30, SF12, SF13): same lock in, same lock out, and no silent
/// saves onto a file that moved or changed.</summary>
public class SaveSafetyTests
{
    private static readonly PdfPermissions Limited = PdfPermissions.All & ~(PdfPermissions.ModifyContents | PdfPermissions.Print | PdfPermissions.PrintHighQuality);

    private static byte[] Locked(string user, string owner, PdfPermissions perms, bool aes256 = true)
    {
        using var doc = PdfDocument.Open(TestPdf.TextPage("Locked page"));
        var enc = (aes256 ? EncryptionOptions.Aes256(user, owner) : EncryptionOptions.Aes128(user, owner)).WithPermissions(perms);
        using var ms = new MemoryStream();
        doc.Save(ms, enc);
        return ms.ToArray();
    }

    private static PageText Note(string text) =>
        new(0, new[] { new TextAnno(0.1, 0.1, 0.5, 0.2, text, "Helvetica", false, false, 12, "#E03131", "left", 0, 1.2) });

    private static async Task<byte[]> MarkAsync(byte[] source, SaveLock saveLock) =>
        await new RedactService().FlattenAsync(source, saveLock, new Dictionary<int, PageRedaction>(), "#000000",
            new Dictionary<int, PageText> { [0] = Note("Checked") }, new Dictionary<int, PageImage>(), new Dictionary<int, PageShape>());

    [Fact]
    public void A_restricted_file_asks_for_its_owner_password_before_changes_and_before_saving_into_it()
    {
        var src = Locked("", "own-pw", Limited);
        Assert.True(SourceLock.ChangesForbidden(src, null, null));
        Assert.False(SourceLock.ChangesForbidden(src, null, "own-pw"));
        var e = Assert.Throws<LockNeedsPasswordException>(() => SourceLock.Plan(src, null, null, intoSource: true));
        Assert.Equal(LockNeed.OwnerPassword, e.Need);
    }

    [Fact]
    public async Task With_the_owner_password_a_marked_restricted_file_keeps_the_same_lock()
    {
        var src = Locked("", "own-pw", Limited);
        var plan = SourceLock.Plan(src, null, "own-pw", intoSource: true);
        Assert.NotNull(plan.Encryption);
        Assert.False(plan.NewOwnerPassword);

        var outBytes = await MarkAsync(src, plan);
        Assert.False(PdfDocument.RequiresPassword(new MemoryStream(outBytes)));   // still opens without a password
        using var doc = PdfDocument.Open(new MemoryStream(outBytes));
        var e = Assert.IsType<EncryptionInfo>(doc.Encryption);                        // never written unlocked (BUG-26)
        Assert.Equal(EncryptionAlgorithm.Aes_256, e.Algorithm);
        Assert.Equal(PdfPermissions.None, (e.Flags ^ Limited) & PdfPermissions.All); // same permissions
        Assert.Equal(PdfPasswordKind.Owner, SourceLock.Kind(outBytes, "own-pw"));     // same owner password
    }

    [Fact]
    public async Task A_password_file_saves_into_itself_only_with_both_passwords_and_save_as_never_unlocks()
    {
        var src = Locked("user-pw", "own-pw", PdfPermissions.All, aes256: false);
        var into = Assert.Throws<LockNeedsPasswordException>(() => SourceLock.Plan(src, "user-pw", null, intoSource: true));
        Assert.Equal(LockNeed.OwnerPassword, into.Need);

        // Save as (D-210 until Pdf 3.36): same open password and permissions, a new permissions password.
        var copy = SourceLock.Plan(src, "user-pw", null, intoSource: false);
        Assert.True(copy.NewOwnerPassword);
        var outBytes = await MarkAsync(src, copy);
        Assert.True(PdfDocument.RequiresPassword(new MemoryStream(outBytes)));
        Assert.Equal(PdfPasswordKind.User, SourceLock.Kind(outBytes, "user-pw"));
        using (var doc = PdfDocument.Open(new MemoryStream(outBytes), "user-pw", false))
        {
            Assert.Equal(EncryptionAlgorithm.Aes_128, doc.Encryption!.Algorithm);   // AES-128 stays AES-128
        }

        // With both passwords it saves into the file with the very same lock.
        var both = SourceLock.Plan(src, "user-pw", "own-pw", intoSource: true);
        var inPlace = await MarkAsync(src, both);
        Assert.Equal(PdfPasswordKind.User, SourceLock.Kind(inPlace, "user-pw"));
        Assert.Equal(PdfPasswordKind.Owner, SourceLock.Kind(inPlace, "own-pw"));
    }

    [Fact]
    public void Opened_with_the_owner_password_an_AES256_file_also_needs_its_open_password()
    {
        var src = Locked("user-pw", "own-pw", PdfPermissions.All);
        var e = Assert.Throws<LockNeedsPasswordException>(() => SourceLock.Plan(src, "own-pw", null, intoSource: true));
        Assert.Equal(LockNeed.OpenPassword, e.Need);
        Assert.NotNull(SourceLock.Plan(src, "user-pw", "own-pw", intoSource: true).Encryption);
    }

    [Fact]
    public async Task An_RC4_file_is_written_AES128_with_the_same_passwords_and_permissions()
    {
        var src = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pdf", "rc4-restricted.pdf"));
        PdfPermissions before;
        using (var d = PdfDocument.Open(new MemoryStream(src)))
        {
            Assert.Equal(EncryptionAlgorithm.Rc4_128, d.Encryption!.Algorithm);
            before = d.Encryption.Flags;
        }

        var plan = SourceLock.Plan(src, null, "chuvadi-owner", intoSource: true);
        var outBytes = await MarkAsync(src, plan);
        using var doc = PdfDocument.Open(new MemoryStream(outBytes));
        Assert.Equal(EncryptionAlgorithm.Aes_128, doc.Encryption!.Algorithm);
        Assert.Equal(before & PdfPermissions.All, doc.Encryption.Flags & PdfPermissions.All);
        Assert.Equal(PdfPasswordKind.Owner, SourceLock.Kind(outBytes, "chuvadi-owner"));
    }

    [Fact]
    public void An_unlocked_file_is_read_and_written_as_it_is()
    {
        var src = File.ReadAllBytes(TestPdf.TextPage("Plain"));
        var plan = SourceLock.Plan(src, null, null, intoSource: true);
        Assert.Null(plan.Encryption);
        Assert.Same(src, SourceLock.Plain(src, null));
    }

    [Fact]
    public void A_moved_or_replaced_source_stops_the_save_into_it()
    {
        var path = TestPdf.TextPage("Original");
        var opened = FileStamp.Of(path);
        var copy = SourceGuard.Take(path, opened);
        SourceGuard.CheckUnchanged(path, opened, copy);   // unchanged: fine

        // Replaced with other content of the same size and time (BUG-24): the hash notices.
        var other = File.ReadAllBytes(path);
        other[^10] ^= 0x20;
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, other);
        File.SetLastWriteTimeUtc(path, time);
        var changed = Assert.Throws<SourceChangedException>(() => SourceGuard.CheckUnchanged(path, opened, copy));
        Assert.False(changed.Missing);

        // Moved away (BUG-23, SF12, SF13): said, not silent.
        File.Move(path, path + ".moved");
        var gone = Assert.Throws<SourceChangedException>(() => SourceGuard.CheckUnchanged(path, opened, copy));
        Assert.True(gone.Missing);
        Assert.Equal(SourceGuard.MovedMessage, gone.Message);
    }

    [Fact]
    public void Save_as_works_from_the_kept_copy_after_the_source_moved()
    {
        var path = TestPdf.TextPage("Kept");
        var tab = new ReaderTab { Id = "t1", Path = path, FileName = "kept.pdf", Opened = FileStamp.Of(path) };
        var kept = TabSave.BeginWork(tab);
        File.Move(path, path + ".moved");

        Assert.Throws<SourceChangedException>(() => TabSave.Prepare(tab, intoSource: true));
        var (source, plan) = TabSave.Prepare(tab, intoSource: false);
        Assert.Same(kept.Bytes, source);
        Assert.Null(plan.Encryption);
    }

    [Fact]
    public void Replacing_the_source_leaves_no_temporary_file_behind()
    {
        var path = TestPdf.TextPage("Replace me");
        var dir = Path.GetDirectoryName(path)!;
        var before = Directory.GetFiles(dir, "~*.chuvadi-tmp").Length;
        SourceGuard.ReplaceAtomically(path, File.ReadAllBytes(TestPdf.TextPage("New")));
        Assert.Equal(before, Directory.GetFiles(dir, "~*.chuvadi-tmp").Length);
    }

    [Fact]
    public void The_owner_password_is_checked_before_it_is_kept()
    {
        var path = TestPdf.WriteTemp(Locked("", "own-pw", Limited));
        var tab = new ReaderTab { Id = "t2", Path = path, FileName = "locked.pdf", Opened = FileStamp.Of(path) };
        Assert.True(TabSave.NeedsOwnerToChange(tab));
        Assert.Equal(SourceLock.NotOwnerPassword, TabSave.GiveOwnerPassword(tab, "wrong"));
        Assert.Null(tab.OwnerPassword);
        Assert.Null(TabSave.GiveOwnerPassword(tab, "own-pw"));
        Assert.False(TabSave.NeedsOwnerToChange(tab));
    }
}
