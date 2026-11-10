using System.Text.RegularExpressions;
using Chuvadi.Pdf.Authoring;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Platform;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>D-158 part 2: raster pages are drawn from an in-app address, not carried inline.</summary>
[Collection(nameof(MemoryAfterCloseTests))] // heavy pages: run alone, as the memory tests do
public sealed class PageImagesTests
{
    private sealed class Owner(string who) : IPageImageOwner
    {
        public string Current => who;
    }

    /// <summary>A large-format page (drawn as one PNG, the path CAD sheets take) with a picture on it.</summary>
    private static string RasterPage()
    {
        var b = PdfDocumentBuilder.Create();
        var page = b.AddPage(new PageSize(1300, 1200)); // over the large-format line, so it is drawn as one PNG
        var rnd = new Random(5);
        for (var i = 0; i < 400; i++)
        {
            page.DrawLine(rnd.Next(1300), rnd.Next(1200), rnd.Next(1300), rnd.Next(1200), Color.FromHex("#335599"), 1.2);
        }

        page.DrawText("Sheet A1 — Ground floor", 120, 120, "Helvetica", 40, Color.FromHex("#000000"));
        return TestPdf.WriteTemp(b.ToByteArray());
    }

    private static readonly Regex Href = new(@"href=""([^""]*)""");

    [Fact]
    public void Tokens_are_128_random_bits_and_never_repeat()
    {
        var store = new PageImages();
        var tokens = Enumerable.Range(0, 5000).Select(_ => store.Add("local", [1])).ToList();

        Assert.All(tokens, t => Assert.Matches("^[0-9a-f]{32}$", t));
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void A_session_that_ends_frees_every_image_it_drew_and_no_one_elses()
    {
        // WT-21: the portal calls RemoveOwner when a session expires or signs out.
        var images = new PageImages();
        var mine = new[] { images.Add("user-1|session-a", [1, 2, 3]), images.Add("user-1|session-a", [4, 5]) };
        var theirs = images.Add("user-2|session-b", [6]);

        Assert.Equal(2, images.RemoveOwner("user-1|session-a"));
        Assert.All(mine, t => Assert.False(images.TryGet(t, "user-1|session-a", out _)));
        Assert.True(images.TryGet(theirs, "user-2|session-b", out _));
        Assert.Equal(1, images.Count);
        Assert.Equal(0, images.RemoveOwner("user-1|session-a"));   // nothing left, nothing thrown
    }

    [Fact]
    public void An_image_is_served_only_to_its_owner_and_never_after_it_is_freed()
    {
        var store = new PageImages();
        var token = store.Add("user-a/session-1", [9, 8, 7]);

        Assert.True(store.TryGet(token, "user-a/session-1", out var png));
        Assert.Equal(new byte[] { 9, 8, 7 }, png.ToArray());
        Assert.False(store.TryGet(token, "user-b/session-2", out _));
        Assert.False(store.TryGet(token, "user-a/session-2", out _));   // the same person in another session: no
        Assert.False(store.TryGet(new string('0', 32), "user-a/session-1", out _));

        store.Remove(token);
        Assert.False(store.TryGet(token, "user-a/session-1", out _));
    }

    [Theory]
    [InlineData("_chvd/pages/0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("/_chvd/pages/0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("_chvd/pages/0123456789ABCDEF0123456789abcdef.png", false)]
    [InlineData("_chvd/pages/../secret.png", false)]
    [InlineData("_chvd/pages/0123456789abcdef0123456789abcdef.svg", false)]
    [InlineData("_chvd/pages/0123456789abcdef.png", false)]
    [InlineData("", false)]
    public void Only_a_well_formed_page_address_is_read(string path, bool ok) =>
        Assert.Equal(ok, PageImages.TryParseAddress(path, out _));

    [Fact]
    public async Task A_raster_page_draws_from_its_address_with_the_same_picture_as_before()
    {
        var path = RasterPage();
        string inline;
        using (var before = await new ChuvadiPdfReader().OpenAsync(path, "a1.pdf"))
        {
            inline = await before.RenderPageSvgAsync(0);
        }

        var store = new PageImages();
        using var session = await new ChuvadiPdfReader(store, new Owner("local")).OpenAsync(path, "a1.pdf");
        var byAddress = await session.RenderPageSvgAsync(0);

        // The page's SVG is the same, character for character, except for the image's href.
        var address = Href.Match(byAddress).Groups[1].Value;
        Assert.StartsWith(PageImages.PathPrefix, address);
        Assert.DoesNotContain("data:", byAddress);
        Assert.Equal(Href.Replace(inline, "href=\"#\""), Href.Replace(byAddress, "href=\"#\""));

        // And the picture behind the address is byte for byte the PNG that was inline.
        Assert.True(PageImages.TryParseAddress(address, out var token));
        Assert.True(store.TryGet(token, "local", out var png));
        var inlinePng = Convert.FromBase64String(Href.Match(inline).Groups[1].Value["data:image/png;base64,".Length..]);
        Assert.Equal(inlinePng, png.ToArray());
        Assert.True(byAddress.Length < 1000, $"the page's text is {byAddress.Length} characters");
    }

    [Fact]
    public async Task Images_are_freed_when_their_pages_are_dropped_and_when_the_document_closes()
    {
        var path = RasterPage();
        var store = new PageImages();
        var session = await new ChuvadiPdfReader(store, new Owner("local")).OpenAsync(path, "a1.pdf");

        await session.RenderPageSvgAsync(0);
        Assert.Equal(1, store.Count);
        session.DropRenderCache();                 // a tab in the background lets its pages go
        Assert.Equal(0, store.Count);

        var again = await session.RenderPageSvgAsync(0);
        Assert.Equal(1, store.Count);
        Assert.True(PageImages.TryParseAddress(Href.Match(again).Groups[1].Value, out var token));
        session.Dispose();                          // the tab closes

        Assert.Equal(0, store.Count);
        Assert.False(store.TryGet(token, "local", out _));
    }

    [Fact]
    public async Task A_page_drawn_for_a_document_that_closed_meanwhile_is_never_kept()
    {
        var store = new PageImages();
        var session = (ChuvadiPdfSession)await new ChuvadiPdfReader(store, new Owner("local")).OpenAsync(RasterPage(), "a1.pdf");
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        session.BeforeRenderForTests = _ => { started.Set(); release.Wait(TimeSpan.FromSeconds(10)); };

        var render = session.RenderPageSvgAsync(0);
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        session.Dispose();
        release.Set();
        await render.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(0, store.Count);
    }
}
