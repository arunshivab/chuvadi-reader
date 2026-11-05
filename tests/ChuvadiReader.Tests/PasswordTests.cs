using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>Password-protect a copy (AES-256 + permissions) and remove a password.</summary>
public class PasswordTests
{
    private static string Text(PdfDocument doc) =>
        new TextExtractor(doc.Objects, ExtractionStrategy.Layout).ExtractText(doc.Pages[0]) ?? string.Empty;

    [Fact]
    public async Task Protected_copy_needs_the_password_and_the_original_is_untouched()
    {
        var src = TestPdf.TextPage("Confidential");
        var before = File.ReadAllBytes(src);
        var output = TestPdf.TempOutput();
        var svc = new PasswordService();

        await svc.ProtectAsync(src, output, new PasswordProtectOptions { OpenPassword = "open-sesame" });

        Assert.Equal(before, File.ReadAllBytes(src));
        Assert.False(svc.RequiresPassword(src));
        Assert.True(svc.RequiresPassword(output));

        var none = Assert.Throws<PdfPasswordRequiredException>(() => PdfDocument.Open(output));
        Assert.False(none.PasswordSupplied);
        var wrong = Assert.Throws<PdfPasswordRequiredException>(() => PdfDocument.Open(output, "nope"));
        Assert.True(wrong.PasswordSupplied);

        using var doc = PdfDocument.Open(output, "open-sesame");
        Assert.Contains("Confidential", Text(doc));
        var info = svc.GetProtection(output, "open-sesame");
        Assert.True(info.IsEncrypted);
        Assert.Equal("AES-256", info.Algorithm);
        Assert.True(info.AllowPrint && info.AllowCopy && info.AllowModify);
        Assert.Equal(6, doc.Encryption!.Revision);
    }

    [Fact]
    public async Task Permissions_wait_on_the_library_but_a_permissions_password_still_opens()
    {
        var src = TestPdf.TextPage("Restricted");
        var svc = new PasswordService();
        Assert.False(PasswordService.PermissionsAvailable);

        await Assert.ThrowsAsync<NotSupportedException>(() => svc.ProtectAsync(src, TestPdf.TempOutput(), new PasswordProtectOptions
        {
            OpenPassword = "user-pw",
            AllowPrint = false,
        }));

        var output = TestPdf.TempOutput();
        await svc.ProtectAsync(src, output, new PasswordProtectOptions { OpenPassword = "user-pw", PermissionsPassword = "owner-pw" });
        using var owner = PdfDocument.Open(output, "owner-pw");
        Assert.Equal(1, owner.PageCount);
        Assert.True(svc.GetProtection(output, "user-pw").AllowPrint);
    }

    [Fact]
    public void Permission_mask_clears_only_the_denied_bits()
    {
        Assert.Equal(-4, PasswordService.BuildPermissionMask(new PasswordProtectOptions { OpenPassword = "x" }));
        int noPrint = PasswordService.BuildPermissionMask(new PasswordProtectOptions { OpenPassword = "x", AllowPrint = false });
        Assert.Equal(0, noPrint & PasswordService.BitPrint);
        Assert.Equal(0, noPrint & PasswordService.BitPrintHighQuality);
        Assert.NotEqual(0, noPrint & PasswordService.BitCopy);
        int noModify = PasswordService.BuildPermissionMask(new PasswordProtectOptions { OpenPassword = "x", AllowModify = false });
        Assert.Equal(0, noModify & PasswordService.BitModify);
        Assert.Equal(0, noModify & PasswordService.BitAssemble);
        Assert.NotEqual(0, noModify & PasswordService.BitFillForms);
    }

    [Fact]
    public async Task Remove_password_writes_a_plain_copy()
    {
        var src = TestPdf.TextPage("Unlock me");
        var locked = TestPdf.TempOutput();
        var plain = TestPdf.TempOutput();
        var svc = new PasswordService();
        await svc.ProtectAsync(src, locked, new PasswordProtectOptions { OpenPassword = "pw" });

        await svc.RemovePasswordAsync(locked, "pw", plain);

        Assert.False(svc.RequiresPassword(plain));
        using var doc = PdfDocument.Open(plain);
        Assert.Null(doc.Encryption);
        Assert.Contains("Unlock me", Text(doc));
        Assert.True(svc.RequiresPassword(locked)); // the locked file stays locked
    }

    [Fact]
    public async Task Remove_password_is_refused_when_changes_are_forbidden_or_the_password_is_wrong()
    {
        var src = TestPdf.TextPage("No changes");
        var locked = TestPdf.TempOutput();
        var svc = new PasswordService();
        TestSigning.ProtectRestricted(src, locked, "pw", PasswordService.BuildPermissionMask(new PasswordProtectOptions { OpenPassword = "pw", AllowModify = false }));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RemovePasswordAsync(locked, "pw", TestPdf.TempOutput()));
        Assert.Equal(PasswordService.RemoveRefusedMessage, refused.Message);
        Assert.Contains("owner", refused.Message);
        await Assert.ThrowsAsync<PdfPasswordRequiredException>(() => svc.RemovePasswordAsync(locked, "bad", TestPdf.TempOutput()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RemovePasswordAsync(src, "pw", TestPdf.TempOutput()));
    }

    [Fact]
    public async Task Never_overwrites_the_source_and_needs_an_open_password()
    {
        var src = TestPdf.TextPage("Same path");
        var before = File.ReadAllBytes(src);
        var svc = new PasswordService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ProtectAsync(src, src, new PasswordProtectOptions { OpenPassword = "pw" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ProtectAsync(src, TestPdf.TempOutput(), new PasswordProtectOptions { OpenPassword = "" }));
        Assert.Equal(before, File.ReadAllBytes(src));
    }

    [Fact]
    public async Task A_protected_copy_opens_in_the_reader_with_its_password()
    {
        var src = TestPdf.TextPage("Reader");
        var locked = TestPdf.TempOutput();
        await new PasswordService().ProtectAsync(src, locked, new PasswordProtectOptions { OpenPassword = "pw" });

        using var session = await new ChuvadiPdfReader().OpenAsync(locked, "locked.pdf", "pw");
        Assert.True(session.Document.IsEncrypted);
        Assert.Contains("<svg", await session.RenderPageSvgAsync(0));
    }

    [Fact]
    public void Strength_hint_grows_with_length_and_variety()
    {
        Assert.Equal(0, PasswordService.RatePassword("").Score);
        Assert.True(PasswordService.RatePassword("abc").Score <= 1);
        var strong = PasswordService.RatePassword("Tr1vandrum#2026");
        Assert.Equal(4, strong.Score);
        Assert.Equal("Strong · 15 characters", strong.Label);
    }
}
