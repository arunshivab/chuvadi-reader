using ChuvadiReader.Core.Theme;
using Xunit;

namespace ChuvadiReader.Tests;

public class ThemeServiceTests
{
    [Theory]
    [InlineData(ChuvadiTheme.Light)]
    [InlineData(ChuvadiTheme.Dark)]
    [InlineData(ChuvadiTheme.Palm)]
    [InlineData(ChuvadiTheme.Indigo)]
    [InlineData(ChuvadiTheme.Contrast)]
    public async Task EachTheme_PersistsAndRestores(ChuvadiTheme theme)
    {
        var storage = new MemoryStorage();
        var a = new ThemeService(storage);
        await a.SetAsync(theme);

        var b = new ThemeService(storage);
        await b.HydrateAsync();

        Assert.Equal(theme, b.Current);
    }

    [Theory]
    [InlineData("Dark", ChuvadiTheme.Dark)]   // written by the two-theme build
    [InlineData("Light", ChuvadiTheme.Light)]
    [InlineData("dark", ChuvadiTheme.Dark)]
    [InlineData("contrast", ChuvadiTheme.Contrast)]
    public async Task StoredValues_IncludingLegacy_Parse(string stored, ChuvadiTheme expected)
    {
        var storage = new MemoryStorage();
        storage.Values["theme"] = stored;
        var svc = new ThemeService(storage);
        await svc.HydrateAsync();

        Assert.Equal(expected, svc.Current);
    }

    [Fact]
    public async Task MissingOrUnknown_StaysCreamPaper()
    {
        var storage = new MemoryStorage();
        var svc = new ThemeService(storage);
        await svc.HydrateAsync();
        Assert.Equal(ChuvadiTheme.Light, svc.Current);

        storage.Values["theme"] = "sepia";
        await svc.HydrateAsync();
        Assert.Equal(ChuvadiTheme.Light, svc.Current);
    }

    [Fact]
    public void FiveThemes_HaveDistinctKeysAndNames()
    {
        var all = Enum.GetValues<ChuvadiTheme>();
        Assert.Equal(5, all.Length);
        Assert.Equal(new[] { "light", "dark", "palm", "indigo", "contrast" }, all.Select(t => t.Key()).ToArray());
        Assert.Equal(new[] { "Cream paper", "Night paper", "Palm leaf", "Indigo ink", "High contrast" }, all.Select(t => t.DisplayName()).ToArray());
    }

    [Fact]
    public async Task Toggle_GoesToNightPaper_ThenBackToCreamPaper()
    {
        var svc = new ThemeService(new MemoryStorage());
        await svc.SetAsync(ChuvadiTheme.Palm);

        await svc.ToggleAsync();
        Assert.Equal(ChuvadiTheme.Dark, svc.Current);

        await svc.ToggleAsync();
        Assert.Equal(ChuvadiTheme.Light, svc.Current);
    }
}
