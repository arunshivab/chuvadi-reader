namespace ChuvadiReader.Core.Window;

/// <summary>
/// Interface size (Settings › Appearance / Accessibility: 100 / 125 / 150 %), applied by the host
/// as a whole-window zoom so layout, breakpoints and pointer positions all scale together
/// (decision 2026-11-06, option B). A host without whole-window zoom reports
/// <see cref="IsSupported"/> = false and the UI falls back to CSS zoom.
/// </summary>
public interface IUiScale
{
    /// <summary>True when the host can zoom the whole window.</summary>
    bool IsSupported { get; }

    /// <summary>Apply a scale in percent (100, 125 or 150; anything else means 100).</summary>
    void SetScale(int percent);
}

/// <summary>For hosts without whole-window zoom (tests, the browser preview, a future web host).</summary>
public sealed class NoopUiScale : IUiScale
{
    public bool IsSupported => false;

    public void SetScale(int percent)
    {
    }
}

/// <summary>Opens a web link in the person's own browser (outside Chuvadi).</summary>
public interface IExternalLinks
{
    /// <summary>False when the host can't open links (the UI then just shows the address).</summary>
    bool IsAvailable { get; }

    /// <summary>Opens <paramref name="url"/>. Only http, https and mailto are accepted.</summary>
    Task<bool> OpenAsync(string url);

    /// <summary>True for the link kinds Chuvadi will hand to the system.</summary>
    static bool IsAllowed(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeMailto);
}

/// <summary>For hosts that can't open external links.</summary>
public sealed class NoExternalLinks : IExternalLinks
{
    public bool IsAvailable => false;

    public Task<bool> OpenAsync(string url) => Task.FromResult(false);
}
