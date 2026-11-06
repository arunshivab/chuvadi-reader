using Chuvadi.Pdf.Documents;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>Bug fixes from the platform programme's second combined patch (L-8, F-2, F-3).</summary>
public sealed class PlatformFixesTests
{
    // ── L-8: a file whose permissions forbid printing doesn't print, on any platform ──

    [Fact]
    public async Task A_no_printing_file_is_refused()
    {
        var src = TestPdf.TextPage("No printing");
        var locked = TestPdf.TempOutput();
        TestSigning.ProtectRestricted(src, locked, "pw", PrintPermissionMask(allowPrint: false));

        using (var doc = PdfDocument.Open(locked, "pw"))
        {
            Assert.False(PrintPermission.Allows(doc));
        }

        using var session = await new ChuvadiPdfReader().OpenAsync(locked, "locked.pdf", "pw");
        Assert.False(session.Document.AllowsPrinting);
    }

    [Fact]
    public async Task Plain_and_print_allowed_files_still_print()
    {
        var plain = TestPdf.TextPage("Plain");
        using (var doc = PdfDocument.Open(plain))
        {
            Assert.True(PrintPermission.Allows(doc));
        }

        var locked = TestPdf.TempOutput();
        TestSigning.ProtectRestricted(plain, locked, "pw", PrintPermissionMask(allowPrint: true));
        using var session = await new ChuvadiPdfReader().OpenAsync(locked, "locked.pdf", "pw");
        Assert.True(session.Document.AllowsPrinting);
    }

    private static int PrintPermissionMask(bool allowPrint) =>
        PasswordService.BuildPermissionMask(new PasswordProtectOptions { OpenPassword = "pw", AllowPrint = allowPrint });

    // ── F-2: the greeting doesn't double a full stop; the name is shown as typed ──

    [Theory]
    [InlineData(9, null, "Good morning.")]
    [InlineData(9, "  ", "Good morning.")]
    [InlineData(13, "Arun", "Good afternoon, Arun.")]
    [InlineData(20, "Arun S.", "Good evening, Arun S.")]
    [InlineData(20, "Dr.  arun s. ", "Good evening, Dr.  arun s. ")]
    [InlineData(11, "A.R.U.N", "Good morning, A.R.U.N.")]
    public void Greeting(int hour, string? name, string expected) =>
        Assert.Equal(expected, GreetingText.For(hour, name));

    // ── F-3: requests made before the Reader runs each open, in order, once ──

    [Fact]
    public void Open_requests_queue_in_order_and_are_taken_once()
    {
        var open = new OpenDocumentService();
        var raised = 0;
        open.Requested += () => raised++;

        open.Request("/a/one.pdf");
        open.Request("/a/two.pdf", "Two", 3);

        Assert.Equal(2, raised);
        Assert.Equal(("/a/one.pdf", "one.pdf", (int?)null), (open.PendingPath, open.PendingName, open.PendingPage));

        Assert.True(open.TryTake(out var first));
        Assert.Equal(new PendingOpen("/a/one.pdf", "one.pdf", null), first);
        Assert.True(open.TryTake(out var second));
        Assert.Equal(new PendingOpen("/a/two.pdf", "Two", 3), second);
        Assert.False(open.TryTake(out _));
        Assert.Null(open.PendingPath);
    }

    [Fact]
    public void Clear_drops_every_waiting_request()
    {
        var open = new OpenDocumentService();
        open.Request("/a/one.pdf");
        open.Request("/a/two.pdf");
        open.Clear();
        Assert.False(open.TryTake(out _));
    }
}
