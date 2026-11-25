using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Primitives;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>BUG-19 / PR15 (D-207): once a file is unlocked, every tool that opens it by its path reuses the password;
/// what a tool writes from it keeps the lock or is stopped, never unlocked; Press says honestly when it didn't shrink.</summary>
[Collection("PdfPasswords")]
public class UnlockedToolsTests
{
    private static string LockedFile(string user, string owner, PdfPermissions perms, string text = "Invoice 4471")
    {
        using var doc = PdfDocument.Open(TestPdf.TextPage(text));
        using var ms = new MemoryStream();
        doc.Save(ms, EncryptionOptions.Aes256(user, owner).WithPermissions(perms));
        return TestPdf.WriteTemp(ms.ToArray());
    }

    [Fact]
    public async Task Find_works_on_an_unlocked_file_and_fails_plainly_before()
    {
        PdfPasswords.ForgetAll();
        var path = LockedFile("open-me", "own", PdfPermissions.All);
        var redact = new RedactService();
        await Assert.ThrowsAsync<PdfPasswordRequiredException>(() => redact.SearchAsync(path, "4471", false, false));

        PdfPasswords.Remember(path, "open-me");
        var found = await redact.SearchAsync(path, "4471", false, false);
        Assert.NotEmpty(found);
        PdfPasswords.ForgetAll();
    }

    [Fact]
    public async Task Press_of_an_unlocked_file_keeps_its_open_password_and_permissions()
    {
        PdfPasswords.ForgetAll();
        var perms = PdfPermissions.All & ~PdfPermissions.CopyContents;
        var path = LockedFile("open-me", "own", perms);
        PdfPasswords.Remember(path, "open-me");
        var folder = Path.Combine(Path.GetTempPath(), "chvd-press-" + Guid.NewGuid().ToString("N"));
        var press = new PressService(new SaveFolderService(new MemoryStorage()));

        var r = await press.PressAsync(path, folder, PressOptions.For(PressPreset.Light));
        Assert.True(r.NewOwnerPassword);
        Assert.True(PdfDocument.RequiresPassword(r.OutputPath));
        using (var doc = PdfDocument.Open(r.OutputPath, "open-me"))
        {
            Assert.Equal(perms & PdfPermissions.All, doc.Encryption!.Flags & PdfPermissions.All);
        }

        Assert.Empty(Directory.GetFiles(folder, "*.chuvadi-tmp"));
        PdfPasswords.ForgetAll();
    }

    [Fact]
    public void Press_says_no_smaller_rather_than_a_negative_saving()
    {
        var bigger = new PressResult("x.pdf", 1000, 1003, 0, 0, 0);
        Assert.Equal("no smaller: 0.3% bigger", bigger.SavedLabel.Replace(',', '.'));
        Assert.Equal("no smaller", new PressResult("x.pdf", 1000, 1000, 0, 0, 0).SavedLabel);
        Assert.StartsWith("50", new PressResult("x.pdf", 1000, 500, 0, 0, 0).SavedLabel);
    }

    [Fact]
    public async Task A_new_file_from_a_locked_files_pages_is_stopped_not_written_unlocked()
    {
        PdfPasswords.ForgetAll();
        var path = LockedFile("open-me", "own", PdfPermissions.All);
        PdfPasswords.Remember(path, "open-me");
        var output = TestPdf.TempOutput();
        var tools = new PdfToolsService();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tools.MergeAsync(new[] { path, TestPdf.TextPage("Plain") }, output));
        Assert.Equal(PdfFiles.LockedSourceText(path), ex.Message);
        Assert.False(File.Exists(output) && new FileInfo(output).Length > 0);
        Assert.Equal(1, await tools.GetPageCountAsync(path));   // reading it is fine
        PdfPasswords.ForgetAll();
    }

    [Fact]
    public async Task Re_protecting_a_change_locked_file_needs_its_permissions_password()
    {
        PdfPasswords.ForgetAll();
        var path = LockedFile("open-me", "own", PdfPermissions.All & ~PdfPermissions.ModifyContents);
        var svc = new PasswordService();
        var ex = await Assert.ThrowsAsync<PasswordRemovalRefusedException>(() =>
            svc.ProtectAsync(path, TestPdf.TempOutput(), new PasswordProtectOptions { OpenPassword = "new" }, "open-me"));
        Assert.Equal(PasswordService.ReprotectRefusedMessage, ex.Message);

        var output = TestPdf.TempOutput();
        await svc.ProtectAsync(path, output, new PasswordProtectOptions { OpenPassword = "new" }, "own");
        Assert.Equal(PdfPasswordKind.User, SourceLock.Kind(File.ReadAllBytes(output), "new"));
    }
}

[CollectionDefinition("PdfPasswords", DisableParallelization = true)]
public sealed class PdfPasswordsCollection
{
}
