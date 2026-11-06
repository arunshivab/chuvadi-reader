using ChuvadiReader.Core.Storage;
using ChuvadiReader.Platform;

namespace ChuvadiReader.Core.Documents;

/// <summary>
/// Supplies the greeting name. Priority: a preferred name the user set in
/// Settings, else the OS username, else nothing (the UI then shows a
/// time-only greeting). No accounts, no login, nothing leaves the device.
/// </summary>
public sealed class UserProfileService
{
    private const string StorageKey = "name";
    private readonly IAppStorage _storage;
    private string? _preferred;

    private readonly IAccount? _account;

    /// <param name="account">Who is using the app (desktop: the OS account; Web: the Sangam identity).</param>
    public UserProfileService(IAppStorage storage, IAccount? account = null)
    {
        _storage = storage;
        _account = account;
    }

    public async Task HydrateAsync(CancellationToken ct = default)
        => _preferred = await _storage.GetAsync(StorageKey, ct).ConfigureAwait(false);

    public async Task SetPreferredNameAsync(string? name, CancellationToken ct = default)
    {
        _preferred = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await _storage.SetAsync(StorageKey, _preferred ?? string.Empty, ct).ConfigureAwait(false);
    }

    /// <summary>Display name, or null when the user opted out and no OS name is available.</summary>
    public string? Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_preferred))
            {
                return _preferred;
            }

            try
            {
                var os = _account?.DisplayName ?? Environment.UserName;
                return string.IsNullOrWhiteSpace(os) ? null : Capitalize(os);
            }
            catch
            {
                return null;
            }
        }
    }

    private static string Capitalize(string s)
        => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
