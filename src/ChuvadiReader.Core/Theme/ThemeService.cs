using ChuvadiReader.Core.Storage;

namespace ChuvadiReader.Core.Theme;

/// <summary>
/// Holds the current theme, persists the choice, and raises <see cref="Changed"/>.
/// The UI layer is responsible for applying the theme to the document (it sets
/// the <c>data-theme</c> attribute via JS interop). This service stays free of
/// any JS / UI dependency so it remains portable across hosts.
/// </summary>
public sealed class ThemeService
{
    private const string StorageKey = "theme";
    private readonly IAppStorage _storage;

    public ThemeService(IAppStorage storage) => _storage = storage;

    public ChuvadiTheme Current { get; private set; } = ChuvadiTheme.Light;

    public event Action? Changed;

    public async Task HydrateAsync(CancellationToken ct = default)
    {
        var stored = await _storage.GetAsync(StorageKey, ct).ConfigureAwait(false);
        if (Enum.TryParse<ChuvadiTheme>(stored, ignoreCase: true, out var theme) && theme != Current)
        {
            Current = theme;
            Changed?.Invoke();
        }
    }

    public async Task SetAsync(ChuvadiTheme theme, CancellationToken ct = default)
    {
        if (theme == Current)
        {
            return;
        }

        Current = theme;
        await _storage.SetAsync(StorageKey, theme.ToString(), ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public Task ToggleAsync(CancellationToken ct = default)
        => SetAsync(Current == ChuvadiTheme.Light ? ChuvadiTheme.Dark : ChuvadiTheme.Light, ct);
}
