using System.Text.RegularExpressions;
using Xunit;

namespace ChuvadiReader.Hosting.Tests;

/// <summary>
/// Android Test round 5: the page box drifting on a newly shown document, and Find leaving the match off screen with
/// the page box on another page (AT-09). The live proof is tools/PixelProof/page-box.js on the tablet preview.
/// </summary>
public sealed class PageBoxTests
{
    private static string Ui(params string[] path) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "UiSources", .. path]));

    [Fact]
    public void Another_document_never_inherits_the_last_ones_page()
    {
        var js = Ui("wwwroot", "js", "reader.js");
        Assert.Contains("export function init(deskEl, dotnetRef, newDocument) {", js);
        Assert.Contains("anchor = !newDocument && anchor && deskEl", js);

        var razor = Ui("Pages", "Reader.razor");
        // Set when the active tab changes, used by the next init only.
        Assert.Matches(@"if \(id != _activeId\)\s*\{[^}]*_initNewDocument = true;", razor);
        Assert.Contains(@"await _js.InvokeVoidAsync(""init"", _desk, _ref, newDocument);", razor);
        // A document shown for the first time starts at its top, before the fit (HO16: "shown once" is on the tab, and
        // a tab that comes back on a later page jumps there after the fit).
        Assert.Matches(@"tab\.FittedOnce = true;[\s\S]*?InvokeVoidAsync\(""setScrollTop"", _desk, 0\)[\s\S]*?await ApplyFit\(", razor);
    }

    [Fact]
    public void Find_keeps_the_match_in_view_on_its_page()
    {
        var js = Ui("wwwroot", "js", "reader.js");
        var fn = Regex.Match(js, @"export async function scrollToMatch\(deskEl, index\) \{(?<b>[\s\S]*?)\n\}").Groups["b"].Value;
        Assert.NotEmpty(fn);
        Assert.Contains("anchor = { page: index, frac:", fn);              // the match stays put while heights change
        Assert.Contains("top = Math.max(wTop, wEnd - view);", fn);          // ends at the page's foot, not on the next page
        Assert.Contains("scrollSnapType", fn);                              // snapping pages place themselves
        Assert.True(fn.IndexOf("anchor = {", StringComparison.Ordinal) < fn.IndexOf("deskEl.scrollTo(", StringComparison.Ordinal));
    }
}
