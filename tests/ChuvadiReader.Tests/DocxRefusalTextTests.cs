using Chuvadi.Docs.Model;
using ChuvadiReader.Core.Documents;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>A Word file refused by a security limit is told which: a tiny file nested too deeply is not "too large".</summary>
public class DocxRefusalTextTests
{
    [Fact]
    public void Each_limit_has_its_own_words()
    {
        Assert.Contains("nested more deeply", DocxViewService.RefusalText(DocxLimit.XmlDepth));
        Assert.DoesNotContain("too large", DocxViewService.RefusalText(DocxLimit.XmlDepth));
        Assert.Contains("more parts", DocxViewService.RefusalText(DocxLimit.Elements));
        Assert.Contains("more parts", DocxViewService.RefusalText(DocxLimit.EntryCount));
        Assert.Contains("password protection", DocxViewService.RefusalText(DocxLimit.SpinCount));
        foreach (var size in new[] { DocxLimit.TotalUncompressedBytes, DocxLimit.CompressionRatio, DocxLimit.PartBytes, DocxLimit.PackageBytes })
            Assert.Contains("too large", DocxViewService.RefusalText(size));
    }
}
