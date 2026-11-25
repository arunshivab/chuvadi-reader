using Xunit;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;

namespace ChuvadiReader.Tests;

/// <summary>D-198, until Pdf 3.35.1: every page operation asks first, naming what each file will lose.</summary>
public class PageOperationAskTests
{
    [Fact]
    public void What_a_file_will_lose_is_named()
    {
        string layered = LayersMeasureSignatureTests.LayeredPdf();
        string plain = TestPdf.BlankPage();
        string rich = LayersMeasureSignatureTests.RawPdf(
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R /PageLabels << /Nums [0 << /S /r >>] >> /Lang (en-IN) /Names << /Dests 6 0 R >> /AcroForm << /Fields [] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            "<< /Type /Outlines /First 5 0 R /Last 5 0 R /Count 1 >>",
            "<< /Title (Start) /Parent 4 0 R /Dest [3 0 R /Fit] >>",
            "<< /Names [(start) [3 0 R /Fit]] >>");
        try
        {
            var l = PageOperationCheck.Inspect(layered);
            Assert.True(l.HasLayers);
            Assert.Empty(l.Lost);

            var p = PageOperationCheck.Inspect(plain);
            Assert.False(p.HasLayers);
            Assert.Empty(p.Lost);

            var r = PageOperationCheck.Inspect(rich);
            Assert.Equal(["bookmarks", "form fields", "links to places in the file", "page labels", "language"], r.Lost);
            Assert.Equal(Path.GetFileName(rich), r.FileName);

            var missing = PageOperationCheck.Inspect(Path.Combine(Path.GetTempPath(), "no-such-file-" + Guid.NewGuid().ToString("N") + ".pdf"));
            Assert.Empty(missing.Lost);
            Assert.False(missing.HasLayers);
        }
        finally
        {
            File.Delete(layered);
            File.Delete(plain);
            File.Delete(rich);
        }
    }

    [Fact]
    public async Task Every_file_is_asked_about_until_agreed()
    {
        string layered = LayersMeasureSignatureTests.LayeredPdf();
        string plain = TestPdf.BlankPage();
        try
        {
            var shell = new ShellService();
            Assert.True(await shell.ConfirmPageOperationAsync([plain]));             // nobody to ask: never blocks

            var asked = new List<IReadOnlyList<PageOperationFile>>();
            bool answer = false;
            shell.PageOperationAskHandler = files => { asked.Add(files); return Task.FromResult(answer); };

            Assert.True(await shell.ConfirmPageOperationAsync([null, ""]));          // no file: no question
            Assert.Empty(asked);

            Assert.False(await shell.ConfirmPageOperationAsync([plain, layered, layered])); // Cancel
            var first = Assert.Single(asked);
            Assert.Equal([Path.GetFileName(plain), Path.GetFileName(layered)], first.Select(f => f.FileName));
            Assert.Equal([false, true], first.Select(f => f.HasLayers));

            Assert.False(await shell.ConfirmPageOperationAsync([plain]));            // a Cancel is not remembered
            Assert.Equal(2, asked.Count);

            answer = true;
            Assert.True(await shell.ConfirmPageOperationAsync([plain]));             // Continue
            Assert.True(await shell.ConfirmPageOperationAsync([plain]));             // agreed: not asked again
            Assert.Equal(3, asked.Count);
            Assert.True(await shell.ConfirmPageOperationAsync([plain, layered]));    // only the new file is asked about
            Assert.Equal([Path.GetFileName(layered)], asked[3].Select(f => f.FileName));
        }
        finally
        {
            File.Delete(layered);
            File.Delete(plain);
        }
    }
}
