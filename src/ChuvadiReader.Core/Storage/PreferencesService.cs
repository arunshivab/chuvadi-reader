namespace ChuvadiReader.Core.Storage;

/// <summary>How a newly opened PDF is zoomed (Settings › Reading › Default zoom).</summary>
public enum ReaderDefaultFit
{
    /// <summary>Fit the page width to the window (default).</summary>
    Width,

    /// <summary>Fit the whole page in the window.</summary>
    Page,

    /// <summary>100 % (actual size).</summary>
    Actual,

    /// <summary>Whatever zoom the tab was last left at.</summary>
    LastUsed,
}

/// <summary>Style of the From / To page-number steppers (Split, and later the shell).</summary>
public enum PageStepperStyle
{
    /// <summary>Separate − / + buttons beside the number.</summary>
    Buttons,

    /// <summary>Small chevrons inside the number field.</summary>
    Chevrons,
}

/// <summary>
/// App-wide user preferences that are not owned by a more specific service (theme lives in
/// <c>ThemeService</c>, save folders in <c>SaveFolderService</c>, the greeting name in
/// <c>UserProfileService</c>). Every value persists through <see cref="IAppStorage"/> as a plain
/// string under a stable key, and every change raises <see cref="Changed"/> so the shell can
/// re-apply it (UI scale and reduce motion are applied to the document via JS by the UI layer).
/// Keys already used before this service existed are kept, so nothing a user chose is lost:
/// <c>splash</c> (also read by the WPF host before Blazor starts), <c>page-stepper</c> (Split page)
/// and <c>bench.perRow</c> (Arrange desks per row).
/// </summary>
public sealed class PreferencesService
{
    public const string UiScaleKey = "ui-scale";
    public const string ReduceMotionKey = "reduce-motion";
    public const string DefaultFitKey = "reading.default-fit";
    public const string DesksPerRowKey = "bench.perRow";
    public const string PageStepperKey = "page-stepper";
    public const string SplashKey = "splash";
    public const string TamilGreetingKey = "greeting.tamil";

    /// <summary>The interface sizes offered in Settings (percent).</summary>
    public static readonly IReadOnlyList<int> UiScales = new[] { 100, 125, 150 };

    private readonly IAppStorage _storage;

    public PreferencesService(IAppStorage storage) => _storage = storage;

    /// <summary>Interface size in percent: 100, 125 or 150.</summary>
    public int UiScale { get; private set; } = 100;

    /// <summary>No sliding / fading when true.</summary>
    public bool ReduceMotion { get; private set; }

    public ReaderDefaultFit DefaultFit { get; private set; } = ReaderDefaultFit.Width;

    /// <summary>Arrange: desks per row, 1–3.</summary>
    public int DesksPerRow { get; private set; } = 1;

    public PageStepperStyle PageStepper { get; private set; } = PageStepperStyle.Buttons;

    /// <summary>Show the splash window on launch (takes effect next launch; read by the host).</summary>
    public bool SplashOnLaunch { get; private set; }

    /// <summary>Show "வணக்கம்." after the Home greeting.</summary>
    public bool ShowTamilGreeting { get; private set; } = true;

    /// <summary>True once <see cref="HydrateAsync"/> has completed at least once.</summary>
    public bool IsHydrated { get; private set; }

    public event Action? Changed;

    public async Task HydrateAsync(CancellationToken ct = default)
    {
        UiScale = ParseScale(await _storage.GetAsync(UiScaleKey, ct).ConfigureAwait(false));
        ReduceMotion = IsOn(await _storage.GetAsync(ReduceMotionKey, ct).ConfigureAwait(false), fallback: false);
        DefaultFit = ParseFit(await _storage.GetAsync(DefaultFitKey, ct).ConfigureAwait(false));
        DesksPerRow = ParsePerRow(await _storage.GetAsync(DesksPerRowKey, ct).ConfigureAwait(false));
        PageStepper = string.Equals(await _storage.GetAsync(PageStepperKey, ct).ConfigureAwait(false), "chevrons", StringComparison.OrdinalIgnoreCase)
            ? PageStepperStyle.Chevrons
            : PageStepperStyle.Buttons;
        SplashOnLaunch = IsOn(await _storage.GetAsync(SplashKey, ct).ConfigureAwait(false), fallback: false);
        ShowTamilGreeting = IsOn(await _storage.GetAsync(TamilGreetingKey, ct).ConfigureAwait(false), fallback: true);
        IsHydrated = true;
        Changed?.Invoke();
    }

    public async Task SetUiScaleAsync(int percent, CancellationToken ct = default)
    {
        var value = UiScales.Contains(percent) ? percent : 100;
        UiScale = value;
        await _storage.SetAsync(UiScaleKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task SetReduceMotionAsync(bool on, CancellationToken ct = default)
    {
        ReduceMotion = on;
        await _storage.SetAsync(ReduceMotionKey, OnOff(on), ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task SetDefaultFitAsync(ReaderDefaultFit fit, CancellationToken ct = default)
    {
        DefaultFit = fit;
        await _storage.SetAsync(DefaultFitKey, FitKey(fit), ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task SetDesksPerRowAsync(int perRow, CancellationToken ct = default)
    {
        DesksPerRow = Math.Clamp(perRow, 1, 3);
        await _storage.SetAsync(DesksPerRowKey, DesksPerRow.ToString(System.Globalization.CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task SetPageStepperAsync(PageStepperStyle style, CancellationToken ct = default)
    {
        PageStepper = style;
        await _storage.SetAsync(PageStepperKey, style == PageStepperStyle.Chevrons ? "chevrons" : "buttons", ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task SetSplashOnLaunchAsync(bool on, CancellationToken ct = default)
    {
        SplashOnLaunch = on;
        await _storage.SetAsync(SplashKey, OnOff(on), ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task SetShowTamilGreetingAsync(bool on, CancellationToken ct = default)
    {
        ShowTamilGreeting = on;
        await _storage.SetAsync(TamilGreetingKey, OnOff(on), ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>The stored string for a fit mode (also what the JS/shell side can read).</summary>
    public static string FitKey(ReaderDefaultFit fit) => fit switch
    {
        ReaderDefaultFit.Page => "page",
        ReaderDefaultFit.Actual => "actual",
        ReaderDefaultFit.LastUsed => "last",
        _ => "width",
    };

    private static ReaderDefaultFit ParseFit(string? stored) => stored?.Trim().ToLowerInvariant() switch
    {
        "page" => ReaderDefaultFit.Page,
        "actual" => ReaderDefaultFit.Actual,
        "last" => ReaderDefaultFit.LastUsed,
        _ => ReaderDefaultFit.Width,
    };

    private static int ParseScale(string? stored)
        => int.TryParse(stored, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) && UiScales.Contains(n)
            ? n
            : 100;

    private static int ParsePerRow(string? stored)
        => int.TryParse(stored, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 3
            ? n
            : 1;

    private static bool IsOn(string? stored, bool fallback)
    {
        if (string.Equals(stored, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(stored, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fallback;
    }

    private static string OnOff(bool on) => on ? "on" : "off";
}
