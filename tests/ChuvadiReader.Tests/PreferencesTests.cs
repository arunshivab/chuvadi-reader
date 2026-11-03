using ChuvadiReader.Core.Storage;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>In-memory <see cref="IAppStorage"/> for service tests.</summary>
internal sealed class MemoryStorage : IAppStorage
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }
}

public class PreferencesTests
{
    [Fact]
    public async Task Defaults_WhenNothingStored()
    {
        var prefs = new PreferencesService(new MemoryStorage());
        await prefs.HydrateAsync();

        Assert.True(prefs.IsHydrated);
        Assert.Equal(100, prefs.UiScale);
        Assert.False(prefs.ReduceMotion);
        Assert.Equal(ReaderDefaultFit.Width, prefs.DefaultFit);
        Assert.Equal(1, prefs.DesksPerRow);
        Assert.Equal(PageStepperStyle.Buttons, prefs.PageStepper);
        Assert.False(prefs.SplashOnLaunch);
        Assert.True(prefs.ShowTamilGreeting);
    }

    [Fact]
    public async Task EverySetting_RoundTripsThroughStorage()
    {
        var storage = new MemoryStorage();
        var a = new PreferencesService(storage);
        await a.SetUiScaleAsync(150);
        await a.SetReduceMotionAsync(true);
        await a.SetDefaultFitAsync(ReaderDefaultFit.Page);
        await a.SetDesksPerRowAsync(3);
        await a.SetPageStepperAsync(PageStepperStyle.Chevrons);
        await a.SetSplashOnLaunchAsync(true);
        await a.SetShowTamilGreetingAsync(false);

        var b = new PreferencesService(storage);
        await b.HydrateAsync();

        Assert.Equal(150, b.UiScale);
        Assert.True(b.ReduceMotion);
        Assert.Equal(ReaderDefaultFit.Page, b.DefaultFit);
        Assert.Equal(3, b.DesksPerRow);
        Assert.Equal(PageStepperStyle.Chevrons, b.PageStepper);
        Assert.True(b.SplashOnLaunch);
        Assert.False(b.ShowTamilGreeting);
    }

    [Theory]
    [InlineData(ReaderDefaultFit.Width, "width")]
    [InlineData(ReaderDefaultFit.Page, "page")]
    [InlineData(ReaderDefaultFit.Actual, "actual")]
    [InlineData(ReaderDefaultFit.LastUsed, "last")]
    public async Task DefaultFit_StoredAsStableKey(ReaderDefaultFit fit, string key)
    {
        var storage = new MemoryStorage();
        var prefs = new PreferencesService(storage);
        await prefs.SetDefaultFitAsync(fit);

        Assert.Equal(key, storage.Values[PreferencesService.DefaultFitKey]);

        var again = new PreferencesService(storage);
        await again.HydrateAsync();
        Assert.Equal(fit, again.DefaultFit);
    }

    [Fact]
    public async Task ExistingKeys_FromBeforeTheService_AreHonoured()
    {
        // Values written by the old Settings page, the Split page and Arrange (Bench).
        var storage = new MemoryStorage();
        storage.Values["splash"] = "on";
        storage.Values["page-stepper"] = "chevrons";
        storage.Values["bench.perRow"] = "2";

        var prefs = new PreferencesService(storage);
        await prefs.HydrateAsync();

        Assert.True(prefs.SplashOnLaunch);
        Assert.Equal(PageStepperStyle.Chevrons, prefs.PageStepper);
        Assert.Equal(2, prefs.DesksPerRow);
    }

    [Fact]
    public async Task WritesUseTheSameKeysAndValuesTheHostAndOtherPagesRead()
    {
        var storage = new MemoryStorage();
        var prefs = new PreferencesService(storage);
        await prefs.SetSplashOnLaunchAsync(true);
        await prefs.SetPageStepperAsync(PageStepperStyle.Buttons);
        await prefs.SetDesksPerRowAsync(2);

        Assert.Equal("on", storage.Values["splash"]);          // App.xaml.cs GetSync("splash")
        Assert.Equal("buttons", storage.Values["page-stepper"]); // Split.razor
        Assert.Equal("2", storage.Values["bench.perRow"]);      // Bench.razor
    }

    [Theory]
    [InlineData("80")]
    [InlineData("175")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task UnknownUiScale_FallsBackTo100(string stored)
    {
        var storage = new MemoryStorage();
        storage.Values[PreferencesService.UiScaleKey] = stored;
        var prefs = new PreferencesService(storage);
        await prefs.HydrateAsync();

        Assert.Equal(100, prefs.UiScale);
    }

    [Fact]
    public async Task SetUiScale_RejectsUnsupportedValues()
    {
        var prefs = new PreferencesService(new MemoryStorage());
        await prefs.SetUiScaleAsync(125);
        Assert.Equal(125, prefs.UiScale);

        await prefs.SetUiScaleAsync(300);
        Assert.Equal(100, prefs.UiScale);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(9, 3)]
    public async Task DesksPerRow_IsClampedToOneToThree(int asked, int expected)
    {
        var prefs = new PreferencesService(new MemoryStorage());
        await prefs.SetDesksPerRowAsync(asked);
        Assert.Equal(expected, prefs.DesksPerRow);
    }

    [Fact]
    public async Task Changed_IsRaisedOnHydrateAndEverySet()
    {
        var prefs = new PreferencesService(new MemoryStorage());
        var count = 0;
        prefs.Changed += () => count++;

        await prefs.HydrateAsync();
        await prefs.SetUiScaleAsync(125);
        await prefs.SetReduceMotionAsync(true);
        await prefs.SetDefaultFitAsync(ReaderDefaultFit.Actual);
        await prefs.SetDesksPerRowAsync(2);
        await prefs.SetPageStepperAsync(PageStepperStyle.Chevrons);
        await prefs.SetSplashOnLaunchAsync(true);
        await prefs.SetShowTamilGreetingAsync(false);

        Assert.Equal(8, count);
    }
}
