using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>Covers the #342 session export/import, JSON round-trip, desk templates, and the
/// skip-missing-source behaviour. Uses <see cref="PlaceholderPdfReader"/> (3-page synthetic docs)
/// with real temp files on disk, since import re-opens sources by path.</summary>
public sealed class BenchSessionTests : IDisposable
{
    private readonly List<string> _temp = new();

    private string TempPdf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chuvadi-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // "%PDF"
        _temp.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var p in _temp)
        {
            try { File.Delete(p); } catch { /* best effort */ }
        }
    }

    private static BenchService NewBench() =>
        new(new PlaceholderPdfReader(), new BenchComposer(), new ExportService());

    [Fact]
    public async Task ExportThenImport_RoundTripsDesksAndPages()
    {
        var bench = NewBench();
        var path = TempPdf();
        await bench.AddSourceAsync(path, "doc.pdf");
        var desk = bench.Desks[0];
        bench.AddPageToDesk(desk.Id, 0, 0);
        bench.AddPageToDesk(desk.Id, 0, 2);
        bench.AddBlankToDesk(desk.Id);
        bench.SetDeskNumbering(desk.Id, new DeskNumbering { Style = NumberStyle.Bates, Prefix = "AS", PadWidth = 3 });

        var dto = bench.ExportSession();

        var fresh = NewBench();
        var result = await fresh.ImportSessionAsync(dto);

        Assert.Equal(1, result.SourcesLoaded);
        Assert.Equal(0, result.SourcesMissing);
        Assert.Single(fresh.Desks);
        Assert.Equal(3, fresh.Desks[0].Pages.Count);
        Assert.NotNull(fresh.Desks[0].Numbering);
        Assert.Equal("AS", fresh.Desks[0].Numbering!.Prefix);
        Assert.True(fresh.Desks[0].Pages[2].IsBlank);
    }

    [Fact]
    public async Task SaveThenLoad_JsonFileRoundTrips()
    {
        var bench = NewBench();
        var path = TempPdf();
        await bench.AddSourceAsync(path, "doc.pdf");
        bench.AddPageToDesk(bench.Desks[0].Id, 0, 1);

        var svc = new SessionService();
        var file = Path.Combine(Path.GetTempPath(), $"chuvadi-session-{Guid.NewGuid():N}.json");
        _temp.Add(file);

        await svc.SaveSessionAsync(file, bench.ExportSession());
        var loaded = await svc.LoadSessionAsync(file);

        Assert.True(File.Exists(file));
        Assert.Single(loaded.Sources);
        Assert.Single(loaded.Desks);
        Assert.Equal(1, loaded.Desks[0].Pages[0].OriginalIndex);
    }

    [Fact]
    public async Task ImportSkipsMissingSource_AndItsPages()
    {
        // A session referencing a path that no longer exists.
        var dto = new BenchSessionDto
        {
            Sources = { new SessionSourceDto { Index = 0, Path = "/nonexistent/gone.pdf", FileName = "gone.pdf" } },
            Desks =
            {
                new SessionDeskDto
                {
                    Name = "Desk 1",
                    Pages =
                    {
                        new SessionPageDto { SourceIndex = 0, OriginalIndex = 0, SourcePath = "/nonexistent/gone.pdf", SourceName = "gone.pdf" },
                        new SessionPageDto { IsBlank = true },
                    },
                },
            },
        };

        var bench = NewBench();
        var result = await bench.ImportSessionAsync(dto);

        Assert.Equal(0, result.SourcesLoaded);
        Assert.Equal(1, result.SourcesMissing);
        Assert.Equal(1, result.PagesSkipped);          // the source-backed page dropped
        Assert.True(result.HasWarnings);
        Assert.Single(bench.Desks[0].Pages);            // only the blank survived
        Assert.True(bench.Desks[0].Pages[0].IsBlank);
    }

    [Fact]
    public void CaptureThenApplyTemplate_CopiesSettingsNotPages()
    {
        var bench = NewBench();
        bench.EnsureDesk();
        var source = bench.Desks[0];
        bench.AddBlankToDesk(source.Id);
        bench.SetDeskWatermark(source.Id, "DRAFT", "Helvetica", false, false, 48, "#808080", 0.25, 45, true);
        bench.SetDeskColour(source.Id, "#C2693B");

        var tpl = bench.CaptureTemplate(source.Id, "My preset");
        Assert.NotNull(tpl);
        Assert.Equal("DRAFT", tpl!.Watermark!.Text);

        var target = bench.AddDesk();
        bench.ApplyTemplate(target.Id, tpl);

        Assert.Equal("DRAFT", target.Watermark!.Text);
        Assert.Equal("#C2693B", target.ColorHex);
        Assert.Empty(target.Pages);   // pages are not part of a template
    }
}
